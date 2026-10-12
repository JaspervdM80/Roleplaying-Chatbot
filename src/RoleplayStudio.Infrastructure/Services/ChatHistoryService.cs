using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.Infrastructure.Services;

/// <summary>Rewriting a chat's past: editing a message, taking back a reply and branching, each undoing what the removed turns left behind.</summary>
public sealed class ChatHistoryService(IDbContextFactory<ApplicationDbContext> dbFactory, ICurrentUser currentUser, TimeProvider time, ILogger<ChatHistoryService> logger)
{
    /// <summary>Rewords one of the user's messages and removes everything after it, so the next reply answers the new words.</summary>
    public Task<Result> EditUserMessageAsync(Guid id, Guid messageId, string text) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "edit the message", CancellationToken.None, ownerId =>
        {
            if (TextFields.Clean(text) is not { } content)
            {
                return Task.FromResult(Result.Failure("Write something first"));
            }

            return RewriteAsync(ownerId, id, async (db, session) =>
            {
                var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId && m.SessionId == id);
                if (message is not { Role: MessageRole.User })
                {
                    logger.LogWarning("Message {MessageId} is not one of the user's messages in chat {SessionId}", messageId, id);
                    return Result.Failure("Only your own messages can be edited");
                }

                message.Content = content;
                await RewindAsync(db, session, message.Sequence, message.Sequence + 1);
                logger.LogInformation("Edited message {MessageId} of chat {SessionId} and removed what followed it", messageId, id);
                return Result.Success();
            });
        });

    /// <summary>
    /// Removes the model's messages after the user's last one, so a reply can be written again. Succeeds without a change
    /// when there is nothing to take back, and never takes the scenario's opening.
    /// </summary>
    public Task<Result> TakeBackReplyAsync(Guid id) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "take back the reply", CancellationToken.None, ownerId =>
            RewriteAsync(ownerId, id, async (db, session) =>
            {
                var lastUserLine = await db.Messages.Where(m => m.SessionId == id && m.Role == MessageRole.User).MaxAsync(m => (long?)m.Sequence);
                if (lastUserLine is null || !await db.Messages.AnyAsync(m => m.SessionId == id && m.Sequence > lastUserLine))
                {
                    return Result.Success();
                }

                await RewindAsync(db, session, lastUserLine.Value + 1, lastUserLine.Value + 1);
                logger.LogInformation("Took back the reply after message {Sequence} of chat {SessionId}", lastUserLine, id);
                return Result.Success();
            }));

    /// <summary>A new chat that holds this one up to and including the message, and goes on from there on its own.</summary>
    public Task<Result<ChatSession>> BranchAsync(Guid id, Guid messageId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "branch the chat", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.CharacterStates).AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id).To<ChatSession>();
            }

            var upTo = await db.Messages.Where(m => m.Id == messageId && m.SessionId == id).Select(m => (long?)m.Sequence).SingleOrDefaultAsync();
            if (upTo is null)
            {
                logger.LogWarning("Message {MessageId} is not in chat {SessionId}", messageId, id);
                return Result.Failure<ChatSession>("That message no longer exists");
            }

            // The message after the branch point carries the scene as it stood at that point, when tracking started there.
            var messages = await db.Messages.Where(m => m.SessionId == id && m.Sequence <= upTo + 1).OrderBy(m => m.Sequence).AsNoTracking().ToListAsync();
            var memories = await db.Memories.Where(m => m.SessionId == id).AsNoTracking().ToListAsync();
            var (branch, kept) = session.Branch(upTo.Value, messages, memories, time.GetUtcNow());

            db.ChatSessions.Add(branch);
            db.Memories.AddRange(kept);
            await db.SaveChangesAsync();
            logger.LogInformation("Branched chat {SessionId} at message {Sequence} into chat {BranchId}", id, upTo, branch.Id);
            return branch;
        });

    /// <summary>
    /// Runs <paramref name="rewrite"/> on the chat with its row locked, so upkeep can neither save between the rewrite's
    /// reads and its save nor, once the version has moved on, after it.
    /// </summary>
    private async Task<Result> RewriteAsync(string ownerId, Guid id, Func<ApplicationDbContext, ChatSession, Task<Result>> rewrite)
    {
        await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "ChatSessions" WHERE "Id" = {id} AND "OwnerId" = {ownerId} FOR UPDATE""");
            var session = await db.ChatSessions.Include(s => s.CharacterStates).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null)
            {
                return NotFound(id);
            }

            var result = await rewrite(db, session);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync();
            }

            return result;
        });
    }

    /// <summary>Removes the messages from <paramref name="removeFrom"/> on and undoes what the messages from <paramref name="rewindFrom"/> on left behind, in one save.</summary>
    private async Task RewindAsync(ApplicationDbContext db, ChatSession session, long rewindFrom, long removeFrom)
    {
        var removed = await db.Messages.Where(m => m.SessionId == session.Id && m.Sequence >= removeFrom).ToListAsync();
        var checkpoints = await db.Messages
            .Where(m => m.SessionId == session.Id && m.Sequence <= rewindFrom && m.SceneBefore != null)
            .OrderByDescending(m => m.Sequence)
            .Take(1)
            .AsNoTracking()
            .ToListAsync();
        var memories = await db.Memories.Where(m => m.SessionId == session.Id && m.SourceToSequence >= rewindFrom).ToListAsync();

        var rewound = session.Rewind(rewindFrom, checkpoints, memories);
        db.Memories.RemoveRange(rewound.DroppedMemories);
        db.CharacterStates.RemoveRange(rewound.DroppedStates);
        db.Messages.RemoveRange(removed);

        // A picture of a removed turn stays in the chat's gallery, under no message.
        var removedIds = removed.Select(m => (Guid?)m.Id).ToList();
        foreach (var picture in await db.Images.Where(i => i.SessionId == session.Id && removedIds.Contains(i.MessageId)).ToListAsync())
        {
            picture.MessageId = null;
        }

        session.LastActivityAt = time.GetUtcNow();
        await db.SaveChangesAsync();
    }

    private Result NotFound(Guid id) => ServiceOperation.NotFound(logger, "chat", id);
}
