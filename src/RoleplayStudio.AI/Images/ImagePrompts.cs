using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Images;

/// <summary>Someone in the picture: stable looks from the character, clothes and changes from the chat. <see cref="Reference"/> numbers the reference image that shows them, from 1.</summary>
public sealed record PicturedPerson(string Name, int? Age, string? Gender, string? Appearance, string? AppearanceChanges, string? Outfit, string? ImageTags, int? Reference = null)
{
    public static PicturedPerson Of(Character character, CharacterState? state, int? reference = null) =>
        new(character.Name, character.Age, character.Gender, character.Appearance, state?.AppearanceChanges, state is null ? character.DefaultOutfit : state.CurrentOutfit, character.ImageTags, reference);

    public static PicturedPerson Of(Persona persona) =>
        new(persona.Name, persona.Age, persona.Gender, persona.Appearance, null, persona.DefaultOutfit, null);
}

/// <summary>What a picture is drawn from. <see cref="Focus"/> names the one person it is of; null pictures the whole scene.</summary>
public sealed record PictureBrief(
    IReadOnlyList<PicturedPerson> People,
    string? Focus,
    bool IsPortrait,
    string? Location = null,
    string? TimeOfDay = null,
    string? Mood = null,
    string? World = null,
    string? Style = null,
    string? Moment = null,
    IReadOnlyList<string>? Memories = null);

public sealed record WrittenPrompt(string Prompt, string? Caption);

public static class ImagePrompts
{
    public const int MaxMemories = 4;
    public const int MaxMessages = 6;
    private const int MaxPromptLength = 2000;
    private const int MaxCaptionLength = 80;

    /// <summary>Portraits and pictures of one person stand at 3:4, scenes lie at 16:9; both are multiples of 64, as diffusion models want.</summary>
    public static (int Width, int Height) SizeFor(PictureBrief brief) => brief.IsPortrait || brief.Focus is not null ? (768, 1024) : (1344, 768);

    public static IReadOnlyList<ChatMessage> Prompt(PictureBrief brief)
    {
        var instructions = new StringBuilder();
        instructions.AppendLine("You write prompts for an image generation model.");
        instructions.AppendLine(brief.IsPortrait
            ? "Write a prompt for a head-and-shoulders portrait of the person below, centred, facing the viewer, on a simple background."
            : brief.Focus is { } focus
                ? $"Write a prompt for a picture of {focus} at this moment of the story, with the place behind them."
                : "Write a prompt for a picture of this moment of the story: the place, and the people in it.");
        instructions.AppendLine("Describe what can be seen, not what anyone thinks or says: looks, clothes, pose, expression, the place, the light.");
        instructions.AppendLine("Use each person's looks and clothes exactly as given below, in the same words, leaving nothing out: age, hair, eyes, skin, build and marks keep them recognisable from picture to picture. The clothes are what they wear now.");
        if (brief.People.Any(p => p.Reference is not null))
        {
            instructions.AppendLine("The image model also gets reference images. Call a person shown in one \"the person from reference image N\", and still describe their clothes, pose and expression.");
        }

        instructions.AppendLine("Write the prompt as one paragraph of comma-separated phrases, under 120 words, without names.");
        instructions.AppendLine("Also write a caption: a few words on what the picture shows, like \"Mira on the loft stairs\".");
        instructions.AppendLine();
        instructions.AppendLine("Answer with JSON only, in this shape:");
        instructions.AppendLine("""{"prompt":"...","caption":"..."}""");

        var story = new StringBuilder();
        story.AppendLine(brief.People.Count == 1 ? "The person:" : "The people:");
        foreach (var person in brief.People)
        {
            story.AppendLine($"- {person.Name}: {Describe(person)}");
        }

        if (!brief.IsPortrait)
        {
            story.AppendLine();
            story.AppendLine($"Place: {brief.Location ?? "unknown"}");
            story.AppendLine($"Time of day: {brief.TimeOfDay ?? "unknown"}");
            story.AppendLine($"Mood: {brief.Mood ?? "unknown"}");
            AppendSection(story, "The world", brief.World);
        }

        if (brief.Memories is { Count: > 0 } memories)
        {
            story.AppendLine();
            story.AppendLine("Worth remembering:");
            foreach (var memory in memories)
            {
                story.AppendLine($"- {memory}");
            }
        }

        AppendSection(story, "The moment to picture", brief.Moment);

        return [new ChatMessage(ChatRole.System, instructions.ToString()), new ChatMessage(ChatRole.User, story.ToString())];
    }

    /// <summary>The prompt in a model reply, or null when the reply is not the JSON asked for.</summary>
    public static WrittenPrompt? Parse(string reply)
    {
        using var document = StructuredOutput.ParseObject(reply);
        if (document is null || Text(document.RootElement, "prompt") is not { } prompt)
        {
            return null;
        }

        return new WrittenPrompt(prompt, Text(document.RootElement, "caption") is { } caption ? Cut(caption, MaxCaptionLength) : null);
    }

    public static (string Prompt, string Negative) Compose(PictureBrief brief, WrittenPrompt written)
    {
        var parts = new List<string?> { brief.Style, written.Prompt };
        parts.AddRange(brief.People.Select(p => p.ImageTags));

        var prompt = string.Join(", ", parts.Select(TextFields.Clean).OfType<string>().Select(p => p.Trim().TrimEnd(',', '.')));
        return (Cut(prompt, MaxPromptLength), "blurry, low quality, deformed, distorted, bad anatomy, disfigured, mutated, extra limbs, ugly, poorly drawn, bad proportions, cloned face, gross proportions, malformed limbs, missing arms, missing legs, extra arms, extra legs, fused fingers, too many fingers");
    }

    public static string? AgeFor(int? age) => age is { } years ? $"{years}-year-old" : null;

    public static string Moment(IEnumerable<Message> messages) => Transcript.Of(messages);

    private static string Describe(PicturedPerson person)
    {
        var parts = new List<string>();
        if (string.Join(' ', new[] { AgeFor(person.Age), TextFields.Clean(person.Gender) }.OfType<string>()) is { Length: > 0 } who)
        {
            parts.Add(who);
        }

        if (person.Reference is { } reference)
        {
            parts.Add($"Shown in reference image {reference}");
        }

        AddPart(parts, "Looks", person.Appearance);
        AddPart(parts, "Changed since", person.AppearanceChanges);
        AddPart(parts, "Wearing", person.Outfit);
        return string.Join(". ", parts) + ".";
    }

    private static void AddPart(List<string> parts, string label, string? text)
    {
        if (TextFields.Clean(text) is { } clean)
        {
            parts.Add($"{label}: {clean.ReplaceLineEndings(" ").TrimEnd('.')}");
        }
    }

    private static void AppendSection(StringBuilder story, string title, string? text)
    {
        if (TextFields.Clean(text) is { } clean)
        {
            story.AppendLine();
            story.AppendLine($"{title}:");
            story.AppendLine(clean);
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? TextFields.Clean(value.GetString()) : null;

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
