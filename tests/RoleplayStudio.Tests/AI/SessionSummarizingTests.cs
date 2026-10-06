using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.AI;

public class SessionSummarizingTests
{
    private const int TokensPerMessage = 250;

    private static List<Message> Chat(int count) =>
        Enumerable.Range(1, count).Select(i => new Message { Sequence = i, Content = new string('x', TokensPerMessage * 4) }).ToList();

    [Fact]
    public void Nothing_is_folded_until_the_unsummarized_part_outgrows_its_threshold()
    {
        var atThreshold = Chat(SessionSummarizing.SummarizeAboveTokens / TokensPerMessage);

        Assert.Empty(SessionSummarizing.ToFold(atThreshold, coveredUpToSequence: 0));
    }

    [Fact]
    public void Folding_starts_after_the_summary_and_keeps_the_latest_messages_verbatim()
    {
        var messages = Chat(30);

        var fold = SessionSummarizing.ToFold(messages, coveredUpToSequence: 10);

        var kept = messages.Where(m => m.Sequence > fold[^1].Sequence).Sum(m => m.Content.Length / 4);
        Assert.Equal(11, fold[0].Sequence);
        Assert.InRange(kept, SessionSummarizing.KeepRecentTokens - TokensPerMessage, SessionSummarizing.KeepRecentTokens);
    }

    [Fact]
    public void A_long_backlog_is_folded_a_bounded_piece_at_a_time()
    {
        var fold = SessionSummarizing.ToFold(Chat(200), coveredUpToSequence: 0);

        Assert.Equal(SessionSummarizing.MaxFoldTokens / TokensPerMessage, fold.Count);
    }
}
