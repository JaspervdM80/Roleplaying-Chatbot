using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.AI.Chat;

public sealed record PresentCharacter(Character Character, CharacterState State);

/// <summary>Everything a turn's prompt is built from; <see cref="Memories"/> come best first.</summary>
public sealed record PromptInput(
    Chatbot Chatbot,
    Scenario Scenario,
    Persona Persona,
    SceneState Scene,
    IReadOnlyList<PresentCharacter> Present,
    IReadOnlyList<PresentCharacter> MetEarlier,
    SessionSummary Summary,
    IReadOnlyList<MemoryEntry> Memories,
    IReadOnlyList<Message> Messages);

public sealed record PromptBudget(int ContextWindow, int ReplyTokens)
{
    public const int DefaultContextWindow = 8192;
    public const int DefaultReplyTokens = 1024;

    public static PromptBudget For(ModelProfile profile) =>
        new(profile.ContextWindow ?? DefaultContextWindow, profile.MaxOutputTokens ?? DefaultReplyTokens);

    public int PromptTokens => Math.Max(ContextWindow - ReplyTokens, 0);
    public int SummaryTokens => PromptTokens * 15 / 100;
    public int MemoryTokens => PromptTokens * 15 / 100;
}

public static partial class PromptBuilder
{
    public static IReadOnlyList<ChatMessage> Build(PromptInput input, PromptBudget budget)
    {
        var system = System(input, budget);
        var remaining = budget.PromptTokens - TokenEstimate.Of(system);
        return [new ChatMessage(ChatRole.System, system), .. Recent(input, remaining)];
    }

    /// <summary>The messages after the summary, newest kept first; the latest is always sent, whatever it costs.</summary>
    private static IEnumerable<ChatMessage> Recent(PromptInput input, int tokens)
    {
        var kept = new List<Message>();
        foreach (var message in input.Messages.Where(m => m.Sequence > input.Summary.CoveredUpToSequence).OrderByDescending(m => m.Sequence))
        {
            tokens -= TokenEstimate.Of(message.Content);
            if (tokens < 0 && kept.Count > 0)
            {
                break;
            }

            kept.Add(message);
        }

        return kept
            .AsEnumerable()
            .Reverse()
            .Select(m => new ChatMessage(m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant, m.Content));
    }

    private static string System(PromptInput input, PromptBudget budget)
    {
        var persona = input.Persona;
        var text = new StringBuilder();
        text.AppendLine($"You are the narrator of a roleplay set in {input.Chatbot.Name}. The user plays {persona.Name}.");
        Section(text, "World", input.Chatbot.WorldDescription);
        Section(text, "Tone and rules", input.Chatbot.ToneAndRules);

        Section(text, $"{persona.Name} (played by the user)", Join(
            Labelled("Age", persona.Age),
            Labelled("Gender", persona.Gender),
            persona.Description,
            Labelled("Personality", persona.Personality),
            Labelled("Looks", persona.Appearance),
            Labelled("Wearing", persona.DefaultOutfit)));

        foreach (var (character, state) in input.Present)
        {
            Section(text, character.Name, Join(
                Labelled("Age", character.Age),
                Labelled("Gender", character.Gender),
                character.ShortDescription,
                Labelled("Personality", character.Personality),
                Labelled("Speech style", character.SpeechStyle),
                Labelled("Backstory", character.Backstory),
                Labelled("Boundaries", character.Boundaries),
                Labelled("Looks", character.Appearance),
                Labelled("Wearing now", state.CurrentOutfit),
                Labelled("Changes since the start", state.AppearanceChanges)));
        }

        Section(text, "Met earlier, not here now", MetEarlier(input.MetEarlier));

        Section(text, "Scenario", Join(
            input.Scenario.Title,
            input.Scenario.Premise,
            Labelled("Location", input.Scene.Location),
            Labelled("Time of day", input.Scene.TimeOfDay),
            Labelled("Mood", input.Scene.Mood),
            Labelled("Goals", input.Scenario.Goals)));

        Section(text, "The story so far", LastSentences(input.Summary.Text, budget.SummaryTokens));
        Section(text, "Remembered from earlier", Memories(input.Memories, budget.MemoryTokens));

        text.AppendLine();
        text.Append(Instruction(input));
        return text.ToString();
    }

    private static string? MetEarlier(IReadOnlyList<PresentCharacter> characters) =>
        characters.Count == 0
            ? null
            : string.Join("\n", characters.Select(c => $"- {c.Character.Name}{(string.IsNullOrWhiteSpace(c.Character.ShortDescription) ? "" : $": {c.Character.ShortDescription.Trim()}")}"));

    private static string? Memories(IReadOnlyList<MemoryEntry> memories, int tokens)
    {
        var lines = new List<string>();
        foreach (var memory in memories)
        {
            var line = $"- {memory.Text.Trim()}";
            tokens -= TokenEstimate.Of(line);
            if (tokens < 0)
            {
                break;
            }

            lines.Add(line);
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>The end of <paramref name="text"/> in whole sentences, since the latest events matter most.</summary>
    private static string? LastSentences(string text, int tokens)
    {
        if (TokenEstimate.Of(text) <= tokens)
        {
            return text;
        }

        var kept = new List<string>();
        foreach (var sentence in SentenceBreak().Split(text.Trim()).Reverse())
        {
            tokens -= TokenEstimate.Of(sentence) + 1;
            if (tokens < 0)
            {
                break;
            }

            kept.Add(sentence);
        }

        kept.Reverse();
        return kept.Count == 0 ? null : string.Join(" ", kept);
    }

    private static string Instruction(PromptInput input)
    {
        var persona = input.Persona.Name;
        return input.Present.Count switch
        {
            0 => $"Narrate the scene around {persona}. Never write {persona}'s words or actions; the user does that.",
            1 => $"Write as {input.Present[0].Character.Name}, staying in character. Put actions in *asterisks*. Never write {persona}'s words or actions; the user does that.",
            _ => $"Voice only the characters present ({string.Join(", ", input.Present.Select(p => p.Character.Name))}). Start each character's lines with their name in bold, like **{input.Present[0].Character.Name}:**, and put narration in *asterisks*. Never write {persona}'s words or actions; the user does that.",
        };
    }

    private static void Section(StringBuilder text, string heading, string? body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            text.AppendLine().AppendLine($"## {heading}").AppendLine(body.Trim());
        }
    }

    private static string? Labelled(string label, object? value, string separator = " ") =>
        value is null ? null : $"{label}:{separator}{value}";

    private static string? Join(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join("\n", present);
    }

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceBreak();
}
