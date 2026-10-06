using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    /// The chat's pinned memories, then those closest to its latest messages. Without an embedding model the
    /// most important memories stand in for the closest.
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
            List<MemoryHit> hits;
            if (vector is not null)
            {
                // The HNSW index filters after its search; without iterative scans, other chats' memories can crowd out every hit.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = relaxed_order", cancellationToken);
                hits = await memories
                    .Where(m => !m.IsPinned && m.Embedding != null)
                    .OrderBy(m => m.Embedding!.CosineDistance(vector))
                    .Take(Candidates)
                    .Select(m => new MemoryHit(m, m.Embedding!.CosineDistance(vector)))
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                hits = await memories
                    .Where(m => !m.IsPinned)
                    .OrderByDescending(m => m.Importance)
                    .ThenByDescending(m => m.CreatedAt)
                    .Take(Candidates)
                    .Select(m => new MemoryHit(m, 1))
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
            }

            var recalled = pinned.Concat(MemoryRanking.Rank(hits, time.GetUtcNow(), Recalled)).ToList();
            logger.LogDebug("Recalled {MemoryCount} memories for chat {SessionId}", recalled.Count, sessionId);
            return Result.Success<IReadOnlyList<MemoryEntry>>(recalled);
        });
}
