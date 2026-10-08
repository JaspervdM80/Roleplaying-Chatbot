using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Memory;

/// <summary>What the user reads, writes and corrects in a chat's long-term memory and summary.</summary>
public sealed class MemoryService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ICurrentUser currentUser,
    MemoryEmbeddings embeddings,
    UpkeepQueue upkeepQueue,
    TimeProvider time,
    ILogger<MemoryService> logger)
{
    /// <summary>The chat's memories, pinned first and then newest first, loaded without their vectors.</summary>
    public Task<Result<IReadOnlyList<MemoryEntry>>> ListAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<MemoryEntry>>(currentUser, logger, "list the memories", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            if (!await db.ChatSessions.AnyAsync(s => s.Id == sessionId, cancellationToken))
            {
                return ChatNotFound(sessionId).To<IReadOnlyList<MemoryEntry>>();
            }

            var memories = await db.Memories
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.IsPinned)
                .ThenByDescending(m => m.CreatedAt)
                .Select(m => new MemoryEntry
                {
                    Id = m.Id,
                    SessionId = m.SessionId,
                    Type = m.Type,
                    Text = m.Text,
                    Importance = m.Importance,
                    RelatedCharacterIds = m.RelatedCharacterIds,
                    SourceFromSequence = m.SourceFromSequence,
                    SourceToSequence = m.SourceToSequence,
                    IsPinned = m.IsPinned,
                    CreatedAt = m.CreatedAt,
                    LastRecalledAt = m.LastRecalledAt,
                })
                .ToListAsync(cancellationToken);
            logger.LogDebug("Listed {MemoryCount} memories of chat {SessionId}", memories.Count, sessionId);
            return Result.Success<IReadOnlyList<MemoryEntry>>(memories);
        });

    public Task<Result<MemoryEntry>> AddAsync(Guid sessionId, MemoryEntry input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "add the memory", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.CharacterStates).SingleOrDefaultAsync(s => s.Id == sessionId);
            if (session is null)
            {
                return ChatNotFound(sessionId).To<MemoryEntry>();
            }

            var memory = new MemoryEntry { SessionId = sessionId, CreatedAt = time.GetUtcNow() };
            memory.CopyEditableFieldsFrom(input);
            if (Refuse(memory, session) is { } refused)
            {
                return refused;
            }

            memory.Embedding = (await embeddings.EmbedAsync(db, [memory.Text], CancellationToken.None))[0];
            db.Memories.Add(memory);
            await db.SaveChangesAsync();
            logger.LogInformation("Added memory {MemoryId} to chat {SessionId}", memory.Id, sessionId);
            return memory;
        });

    /// <summary>Saves the user's corrections; a changed text is embedded again so recall finds it by what it says now.</summary>
    public Task<Result<MemoryEntry>> UpdateAsync(Guid sessionId, MemoryEntry input) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the memory", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.Include(s => s.CharacterStates).SingleOrDefaultAsync(s => s.Id == sessionId);
            var memory = session is null ? null : await db.Memories.SingleOrDefaultAsync(m => m.Id == input.Id && m.SessionId == sessionId);
            if (session is null || memory is null)
            {
                return MemoryNotFound(sessionId, input.Id).To<MemoryEntry>();
            }

            var oldText = memory.Text;
            memory.CopyEditableFieldsFrom(input);
            if (Refuse(memory, session) is { } refused)
            {
                return refused;
            }

            if (memory.Text != oldText)
            {
                memory.Embedding = (await embeddings.EmbedAsync(db, [memory.Text], CancellationToken.None))[0];
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Saved memory {MemoryId} of chat {SessionId}", memory.Id, sessionId);
            memory.Embedding = null;
            return memory;
        });

    public Task<Result> SetPinnedAsync(Guid sessionId, Guid memoryId, bool pinned) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "pin the memory", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            if (await FindAsync(db, sessionId, memoryId) is not { } memory)
            {
                return MemoryNotFound(sessionId, memoryId);
            }

            memory.IsPinned = pinned;
            await db.SaveChangesAsync();
            logger.LogInformation("{Pinned} memory {MemoryId} of chat {SessionId}", pinned ? "Pinned" : "Unpinned", memoryId, sessionId);
            return Result.Success();
        });

    public Task<Result> DeleteAsync(Guid sessionId, Guid memoryId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "delete the memory", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            if (await FindAsync(db, sessionId, memoryId) is not { } memory)
            {
                return MemoryNotFound(sessionId, memoryId);
            }

            db.Memories.Remove(memory);
            await db.SaveChangesAsync();
            logger.LogInformation("Deleted memory {MemoryId} of chat {SessionId}", memoryId, sessionId);
            return Result.Success();
        });

    /// <summary>
    /// Replaces the summary's text; it still covers the same messages, so the next fold builds on the user's version. Refused when
    /// the summary has moved past <paramref name="editedCoveredUpToSequence"/>, or the text would no longer describe what it covers.
    /// </summary>
    public Task<Result<SessionSummary>> SaveSummaryAsync(Guid sessionId, string? text, long editedCoveredUpToSequence) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "save the summary", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            var session = await db.ChatSessions.SingleOrDefaultAsync(s => s.Id == sessionId);
            if (session is null)
            {
                return ChatNotFound(sessionId).To<SessionSummary>();
            }

            if (session.Summary.CoveredUpToSequence != editedCoveredUpToSequence)
            {
                logger.LogWarning("Refused to save the summary of chat {SessionId}: it moved on while it was edited", sessionId);
                return Result.Failure<SessionSummary>("The summary moved on while you edited it; check the new one before saving");
            }

            session.Summary = new SessionSummary { Text = TextFields.Clean(text) ?? "", CoveredUpToSequence = session.Summary.CoveredUpToSequence };
            await db.SaveChangesAsync();
            logger.LogInformation("Saved the summary of chat {SessionId}", sessionId);
            return session.Summary;
        });

    /// <summary>Queues the summary to be written again from the whole chat; refused without a utility model, since the job would do nothing.</summary>
    public Task<Result> RebuildSummaryAsync(Guid sessionId) =>
        ServiceOperation.RunOwnerAsync(currentUser, logger, "rebuild the summary", CancellationToken.None, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId);
            if (!await db.ChatSessions.AnyAsync(s => s.Id == sessionId))
            {
                return ChatNotFound(sessionId);
            }

            if (!await db.ModelProfiles.PreferredFor(ModelRole.Utility).AnyAsync())
            {
                logger.LogWarning("Refused to rebuild the summary of chat {SessionId}: there is no utility model", sessionId);
                return Result.Failure("Add a utility model first; it writes the summary");
            }

            if (!upkeepQueue.Enqueue(new UpkeepJob(ownerId, sessionId, RebuildSummary: true)))
            {
                return Result.Failure("Background work is busy; try again in a moment");
            }

            logger.LogInformation("Queued a rebuild of the summary of chat {SessionId}", sessionId);
            return Result.Success();
        });

    private static Task<MemoryEntry?> FindAsync(ApplicationDbContext db, Guid sessionId, Guid memoryId) =>
        db.Memories.SingleOrDefaultAsync(m => m.Id == memoryId && m.SessionId == sessionId && db.ChatSessions.Any(s => s.Id == sessionId));

    private Result<MemoryEntry>? Refuse(MemoryEntry memory, ChatSession session)
    {
        if (memory.FindProblem(session.CharacterStates.Select(s => s.CharacterId).ToList()) is not { } problem)
        {
            return null;
        }

        logger.LogWarning("Refused memory {MemoryId} of chat {SessionId}: {Field} is not valid", memory.Id, session.Id, problem.Field);
        return Result.Failure<MemoryEntry>(problem.Message);
    }

    private Result ChatNotFound(Guid sessionId)
    {
        logger.LogWarning("Chat {SessionId} was not found", sessionId);
        return Result.Failure("That chat no longer exists");
    }

    private Result MemoryNotFound(Guid sessionId, Guid memoryId)
    {
        logger.LogWarning("Memory {MemoryId} of chat {SessionId} was not found", memoryId, sessionId);
        return Result.Failure("That memory no longer exists");
    }
}
