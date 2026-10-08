using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Scene;

/// <summary>A field the model left out is null and keeps its value.</summary>
public sealed record CharacterChange(string Name, string? Status, string? AppearanceChanges, string? Outfit);

public sealed record Newcomer(string Name, string? Role, int? Age, string? Gender, string? Description, string? Personality, string? SpeechStyle, string? Appearance, string? Outfit);

/// <summary>What the utility model read from the latest messages; <see cref="Present"/> is null when it did not say who is there.</summary>
public sealed record SceneUpdate(
    string? Location,
    string? TimeOfDay,
    string? Mood,
    IReadOnlyList<string>? Present,
    IReadOnlyList<CharacterChange> Changes,
    IReadOnlyList<Newcomer> Newcomers);

/// <summary>The characters a tracked scene can name: those met in this chat, and the rest of the chatbot's cast.</summary>
public sealed record SceneCast(IReadOnlyList<CharacterState> Met, IReadOnlyList<Character> Cast);

public static class SceneTracking
{
    public const int MaxMessages = 12;
    public const int MaxTextLength = 400;

    public static IReadOnlyList<ChatMessage> Prompt(ChatSession session, string personaName, SceneCast cast, IReadOnlyList<Message> messages)
    {
        var instructions = new StringBuilder();
        instructions.AppendLine($"You keep track of the scene in a roleplay. The user plays {personaName}; never list {personaName} as a character.");
        instructions.AppendLine("Read the latest part of the story and report the scene as it stands at the end of it:");
        instructions.AppendLine("- where it is, the time of day, and the mood, each in a few words;");
        instructions.AppendLine($"- who is in the scene with {personaName} now: everyone who arrived stays until the story says they left;");
        instructions.AppendLine("- for anyone whose clothes, looks or feelings changed: their outfit as it is now, lasting changes to how they look, and a one- or two-word status (how they feel, like \"Warm\" or \"Wary\");");
        instructions.AppendLine("- newcomers: named people who speak or act and are not one of the known characters. Describe them from the story, inventing only what is needed to picture them, and always give an age, estimated from the story if it is not said. Someone unnamed who speaks can be named by what they are, like \"The stablehand\".");
        instructions.AppendLine("Use the known characters' names exactly as written below.");
        instructions.AppendLine();
        instructions.AppendLine("Answer with JSON only, in this shape:");
        instructions.AppendLine($$"""{"location":"...","timeOfDay":"...","mood":"...","present":["Name"],"changes":[{"name":"Name","status":"...","appearanceChanges":"...","outfit":"..."}],"newcomers":[{"name":"...","role":"...","age":30,"gender":"...","description":"...","personality":"...","speechStyle":"...","appearance":"...","outfit":"..."}]}""");
        instructions.AppendLine("Leave out a field that did not change. An outfit is everything they wear now, in a sentence or two, not only what changed.");

        var story = new StringBuilder();
        story.AppendLine("The scene before this part:");
        story.AppendLine($"Location: {session.Scene.Location ?? "unknown"}");
        story.AppendLine($"Time of day: {session.Scene.TimeOfDay ?? "unknown"}");
        story.AppendLine($"Mood: {session.Scene.Mood ?? "unknown"}");
        story.AppendLine();
        story.AppendLine("Known characters:");
        foreach (var state in cast.Met)
        {
            var where = session.Scene.PresentCharacterIds.Contains(state.CharacterId) ? "in the scene" : "met earlier, not in the scene";
            story.AppendLine($"- {state.Character.Name} ({where}). Wearing: {OneLine(state.CurrentOutfit) ?? "unknown"}.{Suffix(" Status: ", state.Status)}");
        }

        foreach (var character in cast.Cast.Where(c => cast.Met.All(s => s.CharacterId != c.Id)))
        {
            story.AppendLine($"- {character.Name} (not met yet).{Suffix(" ", character.ShortDescription)}");
        }

        story.AppendLine();
        story.AppendLine("Latest part of the story:");
        story.Append(Transcript.Of(messages));

        return [new ChatMessage(ChatRole.System, instructions.ToString()), new ChatMessage(ChatRole.User, story.ToString())];
    }

    /// <summary>The scene in a model reply, or null when the reply is not the JSON asked for.</summary>
    public static SceneUpdate? Parse(string reply)
    {
        using var document = StructuredOutput.ParseObject(reply);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        return new SceneUpdate(
            Text(root, "location"),
            Text(root, "timeOfDay"),
            Text(root, "mood"),
            root.TryGetProperty("present", out var present) && present.ValueKind == JsonValueKind.Array
                ? present.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.String ? TextFields.Clean(n.GetString()) : null).OfType<string>().ToList()
                : null,
            Objects(root, "changes").Select(ChangeOf).OfType<CharacterChange>().ToList(),
            Objects(root, "newcomers").Select(NewcomerOf).OfType<Newcomer>().ToList());
    }

    /// <summary>Applies the update to a tracked session; returns the characters made for newcomers, already present but not yet saved or cast.</summary>
    public static IReadOnlyList<(Character Character, string? Role)> Apply(ChatSession session, SceneCast cast, SceneUpdate update, string personaName, DateTimeOffset now)
    {
        session.Scene.Location = update.Location ?? session.Scene.Location;
        session.Scene.TimeOfDay = update.TimeOfDay ?? session.Scene.TimeOfDay;
        session.Scene.Mood = update.Mood ?? session.Scene.Mood;

        var known = cast.Met.Select(s => s.Character).Concat(cast.Cast).DistinctBy(c => c.Id).ToList();
        var created = new List<(Character, string?)>();
        foreach (var newcomer in update.Newcomers.Where(n => !IsPersona(n.Name, personaName) && Find(known, n.Name) is null))
        {
            var character = new Character
            {
                Name = newcomer.Name.Length > Character.MaxNameLength ? newcomer.Name[..Character.MaxNameLength].TrimEnd() : newcomer.Name,
                Gender = newcomer.Gender,
                ShortDescription = newcomer.Description,
                Personality = newcomer.Personality,
                SpeechStyle = newcomer.SpeechStyle,
                Appearance = newcomer.Appearance,
                DefaultOutfit = newcomer.Outfit,
            };
            if (newcomer.Age is { } age)
            {
                character.Age = age;
            }

            known.Add(character);
            created.Add((character, newcomer.Role));
        }

        if (update.Present is { } names)
        {
            var present = names
                .Where(n => !IsPersona(n, personaName))
                .Select(n => Find(known, n))
                .OfType<Character>()
                .Concat(created.Select(c => c.Item1))
                .DistinctBy(c => c.Id)
                .ToList();
            session.SetPresent(present, now);
        }
        else if (created.Count > 0)
        {
            session.SetPresent([.. session.Scene.PresentCharacterIds.Select(id => known.First(c => c.Id == id)), .. created.Select(c => c.Item1)], now);
        }

        foreach (var change in update.Changes)
        {
            if (Find(known, change.Name) is not { } character || session.CharacterStates.FirstOrDefault(s => s.CharacterId == character.Id) is not { } state)
            {
                continue;
            }

            state.Status = change.Status ?? state.Status;
            state.AppearanceChanges = change.AppearanceChanges ?? state.AppearanceChanges;
            state.CurrentOutfit = change.Outfit ?? state.CurrentOutfit;
            state.UpdatedAt = now;
        }

        return created;
    }

    // A model writes "Thorne" for "Thorne Ashby"; a first name counts only when it picks out one character.
    private static Character? Find(IReadOnlyList<Character> characters, string name)
    {
        var exact = characters.FirstOrDefault(c => string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var byFirstName = characters.Where(c => string.Equals(c.Name.Trim().Split(' ')[0], name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return byFirstName.Count == 1 ? byFirstName[0] : null;
    }

    private static bool IsPersona(string name, string personaName) =>
        string.Equals(name, personaName.Trim(), StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, personaName.Trim().Split(' ')[0], StringComparison.OrdinalIgnoreCase);

    private static CharacterChange? ChangeOf(JsonElement item) =>
        Text(item, "name") is { } name
            ? new CharacterChange(name, Text(item, "status"), Text(item, "appearanceChanges"), Text(item, "outfit"))
            : null;

    private static Newcomer? NewcomerOf(JsonElement item)
    {
        if (Text(item, "name") is not { } name)
        {
            return null;
        }

        return new Newcomer(
            name,
            Text(item, "role"),
            AgeOf(item),
            Text(item, "gender"),
            Text(item, "description"),
            Text(item, "personality"),
            Text(item, "speechStyle"),
            Text(item, "appearance"),
            Text(item, "outfit"));
    }

    private static int? AgeOf(JsonElement item)
    {
        if (!item.TryGetProperty("age", out var age))
        {
            return null;
        }

        var years = age.ValueKind switch
        {
            JsonValueKind.Number when age.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(age.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var text) => text,
            _ => -1,
        };
        return years >= 0 ? (int)Math.Round(years) : null;
    }

    private static IEnumerable<JsonElement> Objects(JsonElement root, string property) =>
        root.TryGetProperty(property, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? Cut(TextFields.Clean(value.GetString()))
            : null;

    private static string? Cut(string? text) => text is { Length: > MaxTextLength } ? text[..MaxTextLength] : text;

    private static string? OneLine(string? text) => text?.ReplaceLineEndings("; ");

    private static string Suffix(string prefix, string? text) => string.IsNullOrWhiteSpace(text) ? "" : $"{prefix}{text.Trim()}";
}
