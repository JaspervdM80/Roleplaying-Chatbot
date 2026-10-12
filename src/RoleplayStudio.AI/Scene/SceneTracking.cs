using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Prompts;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using static RoleplayStudio.AI.Prompts.PromptText;

namespace RoleplayStudio.AI.Scene;

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
        instructions.AppendLine("- newcomers: named people who speak or act and are not one of the known characters. A newcomer is one person, never a group: give each member of a group their own entry. Describe them from the story, inventing only what is needed to picture them, and always give an age, estimated from the story if it is not said. Someone unnamed who speaks can be named by what they are, like \"The stablehand\".");
        instructions.AppendLine("- for a known character marked \"introduced in this story\": when the story now gives their real name, their new name; when it tells more about how they look, their lasting looks as a whole, old details kept; and their age or gender once the story makes it clear.");
        instructions.AppendLine("A known character still called by what they are, like \"The stablehand\", is not a newcomer once the story names them: when \"Tom\" turns out to be the stablehand, report a change for \"The stablehand\" with newName \"Tom\", and no newcomer called Tom.");
        instructions.AppendLine("Lasting looks are what stays the same from picture to picture: face, hair colour, length and style, eye colour, skin, build and height, and marks like scars or tattoos. Give a newcomer's looks in that detail too.");
        instructions.AppendLine("Use the known characters' names exactly as written below, also for someone who was just renamed.");
        instructions.AppendLine();
        instructions.AppendLine("Answer with JSON only, in this shape:");
        instructions.AppendLine($$"""{"location":"...","timeOfDay":"...","mood":"...","present":["Name"],"changes":[{"name":"Name","status":"...","appearanceChanges":"...","outfit":"...","newName":"...","age":30,"gender":"...","appearance":"..."}],"newcomers":[{"name":"...","role":"...","age":30,"gender":"...","description":"...","personality":"...","speechStyle":"...","appearance":"...","outfit":"..."}]}""");
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
            var character = state.Character;
            var introduced = character.IntroducedInSessionId == session.Id
                ? Sentences(
                    "Introduced in this story",
                    Labelled("Age", character.Age?.ToString() ?? "unknown"),
                    Labelled("Gender", TextFields.Clean(character.Gender) ?? "unknown"),
                    Labelled("Looks", OneLine(character.Appearance) ?? "unknown"))
                : null;
            story.AppendLine($"- {Sentences($"{character.Name} ({where})", Labelled("Wearing", OneLine(state.CurrentOutfit) ?? "unknown"), Labelled("Status", state.Status), introduced)}");
        }

        foreach (var character in cast.Cast.Where(c => cast.Met.All(s => s.CharacterId != c.Id)))
        {
            story.AppendLine($"- {Sentences($"{character.Name} (not met yet)", character.ShortDescription)}");
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
                ? present.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.String ? StructuredOutput.Text(n.GetString()) : null).OfType<string>().ToList()
                : null,
            Objects(root, "changes").Select(ChangeOf).OfType<CharacterChange>().ToList(),
            Objects(root, "newcomers").Select(NewcomerOf).OfType<Newcomer>().ToList());
    }

    private static CharacterChange? ChangeOf(JsonElement item) =>
        Text(item, "name") is { } name
            ? new CharacterChange(name, Text(item, "status"), Text(item, "appearanceChanges"), Text(item, "outfit"), Text(item, "newName"), AgeOf(item), Text(item, "gender"), Text(item, "appearance"))
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

    private static string? Text(JsonElement element, string property) => StructuredOutput.Text(element, property, MaxTextLength);
}
