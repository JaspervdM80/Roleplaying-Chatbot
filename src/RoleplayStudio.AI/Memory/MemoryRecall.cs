using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Memory;

public sealed class MemoryRecall(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ICurrentUser currentUser,
    MemoryEmbeddings embeddings,
    TimeProvider time,
    ILogger<MemoryRecall> logger)
{
    public const int Recalled = 8;
    public const int MaxPinned = 8;
    private const int Candidates = 32;
    private const int QueryMessages = 3;

    /// <summary>
    /// The chat's pinned memories, then those closest to its latest messages. A memory with no vector, or every
    /// memory when the messages cannot be embedded, competes on importance instead.
    /// </summary>
    public Task<Result<IReadOnlyList<MemoryEntry>>> RecallAsync(Guid sessionId, IReadOnlyList<Message> messages, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunOwnerAsync<IReadOnlyList<MemoryEntry>>(currentUser, logger, "recall memories", cancellationToken, async ownerId =>
        {
            await using var db = await dbFactory.CreateForOwnerAsync(ownerId, cancellationToken);
            if (!await db.ChatSessions.AnyAsync(s => s.Id == sessionId, cancellationToken))
            {
                logger.LogWarning("Chat {SessionId} was not found", sessionId);
                return Result.Failure<IReadOnlyList<MemoryEntry>>("That chat no longer exists");
            }

            var memories = db.Memories.Where(m => m.SessionId == sessionId);
            var pinned = await memories
                .Where(m => m.IsPinned)
                .OrderByDescending(m => m.Importance)
                .ThenByDescending(m => m.CreatedAt)
                .Take(MaxPinned)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var query = Transcript.Of(messages.OrderByDescending(m => m.Sequence).Take(QueryMessages));
            var vector = query.Length == 0 ? null : (await embeddings.EmbedAsync(db, [query], cancellationToken))[0];
            List<MemoryHit> hits = vector is null ? [] : await NearestAsync(db, memories, vector, cancellationToken);

            // Memories saved while embedding failed have no vector, so they compete on importance alone.
            hits.AddRange(await memories
                .Where(m => !m.IsPinned && (vector == null || m.Embedding == null))
                .OrderByDescending(m => m.Importance)
                .ThenByDescending(m => m.CreatedAt)
                .Take(Candidates)
                .Select(m => new MemoryHit(m, 1))
                .AsNoTracking()
                .ToListAsync(cancellationToken));

            var now = time.GetUtcNow();
            var recalled = pinned.Concat(MemoryRanking.Rank(hits, now, Recalled)).ToList();
            var recalledIds = recalled.Select(m => m.Id).ToList();
            await memories.Where(m => recalledIds.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.LastRecalledAt, now), cancellationToken);
            logger.LogDebug("Recalled {MemoryCount} memories for chat {SessionId}", recalled.Count, sessionId);
            return Result.Success<IReadOnlyList<MemoryEntry>>(recalled);
        });

    private static Task<List<MemoryHit>> NearestAsync(ApplicationDbContext db, IQueryable<MemoryEntry> memories, Vector vector, CancellationToken cancellationToken) =>
        // The app's retrying execution strategy refuses a transaction opened outside it.
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // The HNSW index filters after its search; without iterative scans, other chats' memories can crowd out every hit.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = relaxed_order", cancellationToken);
            var hits = await memories
                .Where(m => !m.IsPinned && m.Embedding != null)
                .OrderBy(m => m.Embedding!.CosineDistance(vector))
                .Take(Candidates)
                .Select(m => new MemoryHit(m, m.Embedding!.CosineDistance(vector)))
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return hits;
        });
}
