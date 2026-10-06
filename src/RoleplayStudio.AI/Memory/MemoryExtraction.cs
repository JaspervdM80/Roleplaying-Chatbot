using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.AI.Memory;

public sealed record ExtractedMemory(MemoryType Type, string Text, int Importance, IReadOnlyList<Guid> CharacterIds);

public static class MemoryExtraction
{
    public const int MinNewMessages = 4;
    public const int MaxMessages = 40;
    public const int MaxTextLength = 400;
    public const int DefaultImportance = 5;

    public static IReadOnlyList<ChatMessage> Prompt(IReadOnlyList<Message> messages, IReadOnlyList<string> known, string personaName, IReadOnlyList<Character> characters)
    {
        var instructions = new StringBuilder();
        instructions.AppendLine($"You keep the long-term memory of a roleplay between {personaName} (the user) and {Names(characters)}.");
        instructions.AppendLine("Read the new part of the story and list what is worth remembering later: events that happened, facts learned, how relationships changed, lasting changes in appearance, likes and dislikes, and facts about the world.");
        instructions.AppendLine("Each memory is one short, self-contained sentence in the third person that names who it is about. Skip small talk, anything already remembered, and anything only true for this moment.");
        instructions.AppendLine($"Rate importance from {MemoryEntry.MinImportance} (trivia) to {MemoryEntry.MaxImportance} (changes the story).");
        instructions.AppendLine();
        instructions.AppendLine("Answer with JSON only, in this shape:");
        instructions.AppendLine($$"""{"memories":[{"type":"{{string.Join("|", Enum.GetNames<MemoryType>())}}","text":"...","importance":5,"characters":["Name"]}]}""");
        instructions.AppendLine("""Answer {"memories":[]} when nothing is worth remembering.""");

        var story = new StringBuilder();
        if (known.Count > 0)
        {
            story.AppendLine("Already remembered:");
            foreach (var memory in known)
            {
                story.AppendLine($"- {memory}");
            }

            story.AppendLine();
        }

        story.AppendLine("New part of the story:");
        story.Append(Transcript.Of(messages));

        return [new ChatMessage(ChatRole.System, instructions.ToString()), new ChatMessage(ChatRole.User, story.ToString())];
    }

    /// <summary>The memories in a model reply, or null when the reply is not the JSON asked for.</summary>
    public static IReadOnlyList<ExtractedMemory>? Parse(string reply, IReadOnlyList<Character> characters)
    {
        using var document = StructuredOutput.ParseObject(reply);
        if (document is null || !document.RootElement.TryGetProperty("memories", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var memories = new List<ExtractedMemory>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("text", out var textElement)
                || textElement.ValueKind != JsonValueKind.String
                || textElement.GetString()?.Trim() is not { Length: > 0 } text)
            {
                continue;
            }

            memories.Add(new ExtractedMemory(
                TypeOf(item),
                text.Length > MaxTextLength ? text[..MaxTextLength] : text,
                ImportanceOf(item),
                CharacterIdsOf(item, characters)));
        }

        return memories;
    }

    private static MemoryType TypeOf(JsonElement item) =>
        item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && Enum.TryParse<MemoryType>(type.GetString(), ignoreCase: true, out var parsed)
            ? parsed
            : MemoryType.Fact;

    private static int ImportanceOf(JsonElement item) =>
        item.TryGetProperty("importance", out var importance) && importance.ValueKind == JsonValueKind.Number && importance.TryGetDouble(out var value)
            ? Math.Clamp((int)Math.Round(value), MemoryEntry.MinImportance, MemoryEntry.MaxImportance)
            : DefaultImportance;

    // A model can name someone who is not in this chat; only the session's characters become ids.
    private static List<Guid> CharacterIdsOf(JsonElement item, IReadOnlyList<Character> characters)
    {
        if (!item.TryGetProperty("characters", out var names) || names.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return names.EnumerateArray()
            .Where(n => n.ValueKind == JsonValueKind.String)
            .Select(n => characters.FirstOrDefault(c => string.Equals(c.Name, n.GetString()?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OfType<Character>()
            .Select(c => c.Id)
            .Distinct()
            .ToList();
    }

    private static string Names(IReadOnlyList<Character> characters) =>
        characters.Count == 0 ? "the narrator" : string.Join(", ", characters.Select(c => c.Name));
}
