using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.Tests.Domain;

public class MemoryRankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static MemoryHit Hit(string text, double distance, int importance = 5, int daysOld = 0) =>
        new(new MemoryEntry { Text = text, Importance = importance, CreatedAt = Now.AddDays(-daysOld) }, distance);

    [Fact]
    public void A_memory_close_to_the_conversation_outranks_a_more_important_distant_one()
    {
        var ranked = MemoryRanking.Rank([Hit("distant", 1.0, importance: 10), Hit("close", 0.1, importance: 3)], Now, 2);

        Assert.Equal(["close", "distant"], ranked.Select(m => m.Text));
    }

    [Fact]
    public void Among_equally_close_memories_importance_then_recency_decide()
    {
        var ranked = MemoryRanking.Rank(
            [Hit("old trivia", 0.3, importance: 2, daysOld: 30), Hit("new trivia", 0.3, importance: 2), Hit("old key fact", 0.3, importance: 9, daysOld: 30)],
            Now,
            2);

        Assert.Equal(["old key fact", "new trivia"], ranked.Select(m => m.Text));
    }
}
