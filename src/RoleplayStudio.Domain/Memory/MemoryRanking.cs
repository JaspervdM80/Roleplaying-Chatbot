namespace RoleplayStudio.Domain.Memory;

/// <summary>A memory found by vector search; <see cref="Distance"/> is cosine distance, 0 (same) to 2 (opposite).</summary>
public sealed record MemoryHit(MemoryEntry Memory, double Distance);

public static class MemoryRanking
{
    private static readonly TimeSpan RecencyHalfLife = TimeSpan.FromDays(7);

    /// <summary>The best <paramref name="take"/> hits, weighing closeness to the conversation over importance over age.</summary>
    public static IReadOnlyList<MemoryEntry> Rank(IEnumerable<MemoryHit> hits, DateTimeOffset now, int take) =>
        hits
            .OrderByDescending(h => Score(h, now))
            .ThenByDescending(h => h.Memory.CreatedAt)
            .Take(take)
            .Select(h => h.Memory)
            .ToList();

    public static double Score(MemoryHit hit, DateTimeOffset now)
    {
        var similarity = 1 - Math.Clamp(hit.Distance, 0, 2) / 2;
        var importance = Math.Clamp(hit.Memory.Importance, MemoryEntry.MinImportance, MemoryEntry.MaxImportance) / (double)MemoryEntry.MaxImportance;
        var age = now - hit.Memory.CreatedAt;
        var recency = age <= TimeSpan.Zero ? 1 : Math.Pow(0.5, age / RecencyHalfLife);
        return 0.6 * similarity + 0.25 * importance + 0.15 * recency;
    }
}
