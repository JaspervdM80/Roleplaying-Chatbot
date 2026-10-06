using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Data;

namespace RoleplayStudio.AI.Memory;

/// <summary>After a turn: mines new messages for memories, then folds old ones into the summary, with the owner's utility model.</summary>
public sealed class MemoryUpkeep(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IChatClientFactory clients,
    MemoryEmbeddings embeddings,
    TimeProvider time,
    ILogger<MemoryUpkeep> logger)
{
    private const int KnownMemoriesShown = 40;

    public async Task RunAsync(UpkeepJob job, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateForOwnerAsync(job.OwnerId, cancellationToken);
        var session = await db.ChatSessions
            .Include(s => s.Persona)
            .Include(s => s.CharacterStates).ThenInclude(s => s.Character)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == job.SessionId, cancellationToken);
        if (session is null)
        {
            logger.LogDebug("Chat {SessionId} is gone; skipped memory upkeep", job.SessionId);
            return;
        }

        using var utility = await UtilityModel.OpenAsync(db, clients, logger, cancellationToken);
        if (utility is null)
        {
            return;
        }

        var from = Math.Min(session.MemoriesExtractedUpToSequence, session.Summary.CoveredUpToSequence);
        var messages = await db.Messages
            .Where(m => m.SessionId == session.Id && m.Sequence > from)
            .OrderBy(m => m.Sequence)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        await ExtractAsync(db, session, messages, utility, cancellationToken);
        await SummarizeAsync(db, session, messages, utility, cancellationToken);
    }

    private async Task ExtractAsync(ApplicationDbContext db, ChatSession session, IReadOnlyList<Message> messages, UtilityModel utility, CancellationToken cancellationToken)
    {
        var pending = messages.Where(m => m.Sequence > session.MemoriesExtractedUpToSequence).Take(MemoryExtraction.MaxMessages).ToList();
        if (pending.Count < MemoryExtraction.MinNewMessages)
        {
            return;
        }

        var known = await db.Memories
            .Where(m => m.SessionId == session.Id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(KnownMemoriesShown)
            .Select(m => m.Text)
            .ToListAsync(cancellationToken);
        var characters = session.CharacterStates.Select(s => s.Character).ToList();
        var reply = await utility.AskAsync(MemoryExtraction.Prompt(pending, known, session.Persona.Name, characters), cancellationToken);
        if (reply.IsFailure)
        {
            return;
        }

        var extracted = MemoryExtraction.Parse(reply.Value, characters);
        if (extracted is null)
        {
            // Skipped rather than retried, so a model that cannot write the JSON does not pay for the same messages every turn.
            logger.LogWarning("The utility model's memories for chat {SessionId} were not readable JSON; skipped messages {From} to {To}", session.Id, pending[0].Sequence, pending[^1].Sequence);
        }
        else
        {
            var vectors = await embeddings.EmbedAsync(db, extracted.Select(m => m.Text).ToList(), cancellationToken);
            var now = time.GetUtcNow();
            db.Memories.AddRange(extracted.Select((m, i) => new MemoryEntry
            {
                SessionId = session.Id,
                Type = m.Type,
                Text = m.Text,
                Importance = m.Importance,
                RelatedCharacterIds = m.CharacterIds.ToList(),
                Embedding = vectors[i],
                SourceFromSequence = pending[0].Sequence,
                SourceToSequence = pending[^1].Sequence,
                CreatedAt = now,
            }));
        }

        session.MemoriesExtractedUpToSequence = pending[^1].Sequence;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Extracted {MemoryCount} memories from chat {SessionId} up to message {Sequence}", extracted?.Count ?? 0, session.Id, pending[^1].Sequence);
    }

    private async Task SummarizeAsync(ApplicationDbContext db, ChatSession session, IReadOnlyList<Message> messages, UtilityModel utility, CancellationToken cancellationToken)
    {
        var fold = SessionSummarizing.ToFold(messages, session.Summary.CoveredUpToSequence);
        if (fold.Count == 0)
        {
            return;
        }

        var reply = await utility.AskAsync(SessionSummarizing.Prompt(session.Summary.Text, fold, session.Persona.Name), cancellationToken);
        if (reply.IsFailure)
        {
            return;
        }

        if (reply.Value.Trim() is not { Length: > 0 } text)
        {
            logger.LogWarning("The utility model wrote an empty summary for chat {SessionId}", session.Id);
            return;
        }

        session.Summary = new SessionSummary { Text = text, CoveredUpToSequence = fold[^1].Sequence };
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Summarized chat {SessionId} up to message {Sequence}", session.Id, fold[^1].Sequence);
    }
}
