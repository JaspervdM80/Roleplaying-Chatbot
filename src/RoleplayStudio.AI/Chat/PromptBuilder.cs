using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Prompts;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;
using static RoleplayStudio.AI.Prompts.PromptText;

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
    IReadOnlyList<Message> Messages,
    DirectorHint? Hint = null);

public sealed record PromptBudget(int ContextWindow, int ReplyTokens)
{
    public const int DefaultContextWindow = 8192;
    public const int DefaultReplyTokens = 1024;

    public static PromptBudget For(ModelProfile profile) =>
        new(profile.ContextWindow ?? DefaultContextWindow, profile.MaxOutputTokens ?? DefaultReplyTokens);

    public int PromptTokens => Math.Max(ContextWindow - ReplyTokens, 0);
    public int SummaryTokens => PromptTokens * 15 / 100;
    public int MemoryTokens => PromptTokens * 15 / 100;

    /// <summary>Shared by the persona's and the present characters' sheets, so long backstories cannot crowd out the history.</summary>
    public int SheetTokens => PromptTokens * 30 / 100;
}

public static partial class PromptBuilder
{
    public static IReadOnlyList<ChatMessage> Build(PromptInput input, PromptBudget budget)
    {
        var system = System(input, budget);
        var note = Steer(input) is { } steer ? $"(Out of character: {Instruction(input)} For this reply only: {steer})" : $"(Out of character: {Instruction(input)})";
        var remaining = budget.PromptTokens - TokenEstimate.Of(system) - TokenEstimate.Of(note);
        List<ChatMessage> messages = [new ChatMessage(ChatRole.System, system), .. Recent(input, remaining)];
        return WithNote(messages, note);
    }

    // How to write the reply goes after the history, where a model heeds it, and in a user turn: some chat templates refuse a later system message or two user turns in a row.
    private static List<ChatMessage> WithNote(List<ChatMessage> messages, string note)
    {
        if (messages[^1].Role == ChatRole.User)
        {
            messages[^1] = new ChatMessage(ChatRole.User, $"{messages[^1].Text}\n\n{note}");
        }
        else
        {
            messages.Add(new ChatMessage(ChatRole.User, note));
        }

        return messages;
    }

    /// <summary>
    /// The turns after the summary, newest kept first; the latest is always sent, whatever it costs. A reply saved as
    /// several speakers goes back as one turn in the tagged form the model wrote it in.
    /// </summary>
    private static IEnumerable<ChatMessage> Recent(PromptInput input, int tokens)
    {
        var kept = new List<ChatMessage>();
        var messages = input.Messages.Where(m => m.Sequence > input.Summary.CoveredUpToSequence).OrderBy(m => m.Sequence);
        foreach (var turn in Turns(messages, soloCharacter: input.Present.Count == 1).Reverse())
        {
            tokens -= TokenEstimate.Of(turn.Text);
            if (tokens < 0 && kept.Count > 0)
            {
                break;
            }

            kept.Add(turn);
        }

        kept.Reverse();
        return kept;
    }

    /// <summary>Character lines are tagged unless the turn is one line in a chat with one character, which writes untagged.</summary>
    private static IEnumerable<ChatMessage> Turns(IEnumerable<Message> messages, bool soloCharacter)
    {
        var turns = new List<List<Message>>();
        foreach (var message in messages)
        {
            if (turns.Count == 0 || message.Role == MessageRole.User || turns[^1][^1].Role == MessageRole.User)
            {
                turns.Add([message]);
            }
            else
            {
                turns[^1].Add(message);
            }
        }

        return turns
            .Select(t => t[0].Role == MessageRole.User
                ? new ChatMessage(ChatRole.User, t[0].Content)
                : new ChatMessage(ChatRole.Assistant, soloCharacter && t.Count == 1 ? t[0].Content : string.Join("\n\n", t.Select(Tagged))))
            .ToList();
    }

    private static string Tagged(Message message) =>
        message.Role == MessageRole.Character ? $"**{message.SpeakerName}:** {message.Content}" : message.Content;

    private static string System(PromptInput input, PromptBudget budget)
    {
        var persona = input.Persona;
        var sheetTokens = budget.SheetTokens / (input.Present.Count + 1);
        var text = new StringBuilder();
        text.AppendLine(Role(input));
        Section(text, "World", input.Chatbot.WorldDescription);
        Section(text, "Tone and rules", input.Chatbot.ToneAndRules);

        Section(text, $"{persona.Name} (played by the user)", Sheet(
            sheetTokens,
            new Field("Age", persona.Age?.ToString()),
            new Field("Gender", persona.Gender),
            new Field(null, persona.Description, Trimmable: true),
            new Field("Personality", persona.Personality, Trimmable: true),
            new Field("Looks", persona.Appearance),
            new Field("Wearing now", input.Scene.PersonaWears(persona))));

        foreach (var (character, state) in input.Present)
        {
            Section(text, character.Name, Sheet(
                sheetTokens,
                new Field("Age", character.Age?.ToString()),
                new Field("Gender", character.Gender),
                new Field(null, character.ShortDescription, Trimmable: true),
                new Field("Personality", character.Personality, Trimmable: true),
                new Field("Speech style", character.SpeechStyle),
                new Field("Backstory", character.Backstory, Trimmable: true),
                new Field("Boundaries", character.Boundaries),
                new Field("Looks", character.Appearance),
                new Field("Wearing now", state.CurrentOutfit),
                new Field("Changes since the start", state.AppearanceChanges),
                new Field("Feeling", state.Status)));
        }

        Section(text, "Met earlier, not here now", MetEarlier(input.MetEarlier));
        Section(text, "Scenario", Lines(input.Scenario.Title, input.Scenario.Premise, Labelled("Goals", input.Scenario.Goals)));
        Section(text, "Now", Lines(
            Labelled("Location", input.Scene.Location),
            Labelled("Time of day", input.Scene.TimeOfDay),
            Labelled("Mood", input.Scene.Mood)));

        Section(text, "The story so far", LastSentences(input.Summary.Text, budget.SummaryTokens));
        Section(text, "Remembered from earlier", Memories(input.Memories, budget.MemoryTokens));

        text.AppendLine();
        text.Append(People(persona.Name));
        return text.ToString();
    }

    private static string Role(PromptInput input)
    {
        var setting = $"a roleplay set in {input.Chatbot.Name}. The user plays {input.Persona.Name}.";
        return input.Present.Count switch
        {
            0 => $"You are the narrator of {setting}",
            1 => $"You play {input.Present[0].Character.Name} in {setting}",
            _ => $"You direct {setting} You narrate and voice the characters in the scene.",
        };
    }

    private sealed record Field(string? Label, string? Text, bool Trimmable = false);

    /// <summary>The fields on lines of their own; over <paramref name="tokens"/>, the trimmable ones keep only their first sentences, in order.</summary>
    private static string? Sheet(int tokens, params Field[] fields)
    {
        static string? Line(string? label, string? text) => label is null ? TextFields.Clean(text) : Labelled(label, text);

        tokens -= fields.Where(f => !f.Trimmable).Sum(f => TokenEstimate.Of(Line(f.Label, f.Text) ?? ""));
        var lines = new List<string?>();
        foreach (var field in fields)
        {
            if (!field.Trimmable)
            {
                lines.Add(Line(field.Label, field.Text));
                continue;
            }

            var line = Line(field.Label, FirstSentences(TextFields.Clean(field.Text), tokens - TokenEstimate.Of($"{field.Label}: ")));
            tokens -= TokenEstimate.Of(line ?? "");
            lines.Add(line);
        }

        return Lines([.. lines]);
    }

    /// <summary>A hint naming someone not in the scene adds nothing; the scene is changed before the prompt is built.</summary>
    private static string? Steer(PromptInput input)
    {
        string? Present(Guid id) => input.Present.FirstOrDefault(p => p.Character.Id == id)?.Character.Name;

        return input.Hint switch
        {
            DirectorHint.LetRespond(var id) when Present(id) is { } name =>
                $"only {name} speaks and acts; everyone else stays quiet.",
            DirectorHint.BringIn(var id) when Present(id) is { } name =>
                $"{name} arrives in the scene now. Show them coming in and let them take part.",
            DirectorHint.SkipAhead =>
                "skip ahead in time to the next moment worth playing. Say in a line of narration how much time has passed and what changed, then carry on from there.",
            DirectorHint.NarrationOnly =>
                "write narration only, describing the scene and what happens; no one speaks.",
            DirectorHint.MoveTo(var location) =>
                $"the scene moves to {location}. Narrate getting there and carry on in the new place.",
            _ => null,
        };
    }

    private static string? MetEarlier(IReadOnlyList<PresentCharacter> characters) =>
        Lines([.. characters.Select(c => TextFields.Clean(c.Character.ShortDescription) is { } description ? $"- {c.Character.Name}: {description}" : $"- {c.Character.Name}")]);

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

        var kept = WholeSentences(SentenceBreak().Split(text.Trim()).Reverse(), tokens);
        kept.Reverse();
        return kept.Count == 0 ? null : string.Join(" ", kept);
    }

    /// <summary>
    /// The start of <paramref name="text"/> in whole lines, then whole sentences of the line that runs over, since a sheet
    /// leads with what matters most; a list without full stops still keeps its first items.
    /// </summary>
    private static string? FirstSentences(string? text, int tokens)
    {
        if (text is null || TokenEstimate.Of(text) <= tokens)
        {
            return text;
        }

        var kept = new List<string>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cost = TokenEstimate.Of(line) + 1;
            if (cost > tokens)
            {
                if (WholeSentences(SentenceBreak().Split(line), tokens) is { Count: > 0 } sentences)
                {
                    kept.Add(string.Join(" ", sentences));
                }

                break;
            }

            kept.Add(line);
            tokens -= cost;
        }

        return kept.Count == 0 ? null : string.Join("\n", kept);
    }

    private static List<string> WholeSentences(IEnumerable<string> sentences, int tokens)
    {
        var kept = new List<string>();
        foreach (var sentence in sentences)
        {
            tokens -= TokenEstimate.Of(sentence) + 1;
            if (tokens < 0)
            {
                break;
            }

            kept.Add(sentence);
        }

        return kept;
    }

    private static string Instruction(PromptInput input)
    {
        var persona = input.Persona.Name;
        var turn = input.Present.Count switch
        {
            0 => $"Narrate the scene around {persona}.",
            1 => $"Write as {input.Present[0].Character.Name}, staying in character. Put actions in *asterisks*.",
            _ => $"Narrate, and voice only the characters present ({string.Join(", ", input.Present.Select(p => p.Character.Name))}); no one else speaks. "
                + $"Start each character's lines on a new line with their name in bold, like **{input.Present[0].Character.Name}:**, "
                + "and write narration on lines of its own in *italics*.",
        };
        return $"{turn} Never write {persona}'s words or actions; the user does that. "
            + $"Keep the reply to a few paragraphs: move the story on one step, then stop where {persona} can respond.";
    }

    private static string People(string persona) =>
        "When someone new appears, describe in a sentence or two how they look the first time they are seen. " +
        $"Call anyone {persona} already knows, like a friend, relative, classmate or colleague, by their name from the start, giving them one if they have none yet; " +
        "a stranger stays nameless until they are introduced.";

    private static void Section(StringBuilder text, string heading, string? body) => PromptText.Section(text, $"## {heading}", body);

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceBreak();
}
