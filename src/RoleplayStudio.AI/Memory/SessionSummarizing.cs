using System.Text;
using Microsoft.Extensions.AI;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Memory;

public static class SessionSummarizing
{
    public const int SummarizeAboveTokens = 3000;
    public const int KeepRecentTokens = 1500;
    public const int MaxFoldTokens = 6000;
    public const int SummaryWords = 300;

    /// <summary>
    /// The oldest unsummarized messages to fold into the summary once the unsummarized part outgrows <see cref="SummarizeAboveTokens"/>:
    /// enough that about <see cref="KeepRecentTokens"/> stay verbatim, at most <see cref="MaxFoldTokens"/> in one go.
    /// </summary>
    public static IReadOnlyList<Message> ToFold(IReadOnlyList<Message> messages, long coveredUpToSequence)
    {
        var open = messages.Where(m => m.Sequence > coveredUpToSequence).OrderBy(m => m.Sequence).ToList();
        var tokens = open.Sum(m => TokenEstimate.Of(m.Content));
        if (tokens <= SummarizeAboveTokens)
        {
            return [];
        }

        var fold = new List<Message>();
        var folded = 0;
        foreach (var message in open.Take(open.Count - 1))
        {
            var size = TokenEstimate.Of(message.Content);
            if (tokens <= KeepRecentTokens || (fold.Count > 0 && folded + size > MaxFoldTokens))
            {
                break;
            }

            fold.Add(message);
            folded += size;
            tokens -= size;
        }

        return fold;
    }

    public static IReadOnlyList<ChatMessage> Prompt(string previousSummary, IReadOnlyList<Message> fold, string personaName)
    {
        var instructions = $"""
            You keep the running summary of a roleplay in which the user plays {personaName}.
            Rewrite the summary so it also covers the new part of the story. Keep what still matters: who is who, what happened, how relationships stand, promises and open threads.
            Write plain past-tense prose in the third person, at most {SummaryWords} words, and answer with the summary only.
            """;

        var story = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(previousSummary))
        {
            story.AppendLine("Summary so far:").AppendLine(previousSummary.Trim()).AppendLine();
        }

        story.AppendLine("New part of the story:");
        story.Append(Transcript.Of(fold));

        return [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, story.ToString())];
    }
}
