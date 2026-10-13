using System.Text;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Prompts;
using RoleplayStudio.Domain;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Images;

/// <summary>Someone in the picture: stable looks from the character, clothes and changes from the chat. <see cref="Reference"/> numbers the reference image that shows them, from 1.</summary>
public sealed record PicturedPerson(string Name, int? Age, string? Gender, string? Appearance, string? AppearanceChanges, string? Outfit, string? ImageTags, int? Reference = null)
{
    public static PicturedPerson Of(Character character, CharacterState? state, int? reference = null) =>
        new(character.Name, character.Age, character.Gender, character.Appearance, state?.AppearanceChanges, state is null ? character.DefaultOutfit : state.CurrentOutfit, character.ImageTags, reference);

    public static PicturedPerson Of(Persona persona, string? outfit) =>
        new(persona.Name, persona.Age, persona.Gender, persona.Appearance, null, outfit, null);
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

    /// <summary>Room for the place and one person, and more for each person after the first, so a group keeps everyone's looks.</summary>
    public static int WordsFor(PictureBrief brief) => 80 + (40 * Math.Max(brief.People.Count, 1));

    public static IReadOnlyList<ChatMessage> Prompt(PictureBrief brief, bool tags)
    {
        var instructions = new StringBuilder();
        instructions.AppendLine("You write prompts for an image generation model.");
        instructions.AppendLine(brief.IsPortrait
            ? "Write a prompt for a head-and-shoulders portrait of the person below, centred, facing the viewer, on a simple background."
            : brief.Focus is { } focus
                ? $"Write a prompt for a picture of {focus} at this moment of the story, with the place behind them."
                : "Write a prompt for a picture of this moment of the story: the place, and the people in it.");
        instructions.AppendLine("Describe what can be seen, not what anyone thinks or says: looks, clothes, pose, expression, the place, the light.");
        instructions.AppendLine("Keep what makes each person recognisable from picture to picture, in the words given below: age, hair, eyes, skin, build and marks. The clothes are what they wear now. When words run short, describe the place and the light more briefly, never a person's looks.");
        if (brief.People.Count > 1 && brief.People.Any(p => p.ImageTags is not null))
        {
            instructions.AppendLine("Put a person's image tags, word for word, with the rest of that person's description.");
        }
        if (brief.People.Any(p => p.Reference is not null))
        {
            instructions.AppendLine("The image model also gets reference images. Call a person shown in one \"the person from reference image N\", and still describe their clothes, pose and expression.");
        }

        instructions.AppendLine(tags
            ? $"Write the prompt as comma-separated tags and short phrases, the most important first, under {WordsFor(brief)} words, without names."
            : $"Write the prompt as a few plain sentences, under {WordsFor(brief)} words, without names.");
        instructions.AppendLine("Also write a caption: a few words on what the picture shows, like \"Mira on the loft stairs\".");
        instructions.AppendLine();
        instructions.AppendLine("Answer with JSON only, in this shape:");
        instructions.AppendLine("""{"prompt":"...","caption":"..."}""");

        var story = new StringBuilder();
        story.AppendLine(brief.People.Count == 1 ? "The person:" : "The people:");
        foreach (var person in brief.People)
        {
            story.AppendLine($"- {person.Name}: {Describe(person, withTags: brief.People.Count > 1)}");
        }

        if (!brief.IsPortrait)
        {
            story.AppendLine();
            story.AppendLine($"Place: {brief.Location ?? "unknown"}");
            story.AppendLine($"Time of day: {brief.TimeOfDay ?? "unknown"}");
            story.AppendLine($"Mood: {brief.Mood ?? "unknown"}");
            PromptText.Section(story, "The world:", brief.World);
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

        PromptText.Section(story, "The moment to picture:", brief.Moment);

        return [new ChatMessage(ChatRole.System, instructions.ToString()), new ChatMessage(ChatRole.User, story.ToString())];
    }

    /// <summary>The prompt in a model reply, or null when the reply is not the JSON asked for.</summary>
    public static WrittenPrompt? Parse(string reply)
    {
        using var document = StructuredOutput.ParseObject(reply);
        if (document is null || StructuredOutput.Text(document.RootElement, "prompt") is not { } prompt)
        {
            return null;
        }

        return new WrittenPrompt(prompt, StructuredOutput.Text(document.RootElement, "caption", MaxCaptionLength));
    }

    /// <summary>The style, the written prompt and any image tags it left out; in a group the written prompt should already place each person's tags.</summary>
    public static string Compose(PictureBrief brief, WrittenPrompt written)
    {
        var missing = brief.People
            .Select(p => TextFields.Clean(p.ImageTags))
            .OfType<string>()
            .Where(tags => !written.Prompt.Contains(tags, StringComparison.OrdinalIgnoreCase));
        string?[] parts = [brief.Style, written.Prompt, .. missing];
        var prompt = string.Join(", ", parts.Select(TextFields.Clean).OfType<string>().Select(p => p.Trim().TrimEnd(',', '.')));
        return Cut(prompt, MaxPromptLength);
    }

    public static string? AgeFor(int? age) => age is { } years ? years < 18 ? "18-years-old" : $"{years}-year-old" : null;

    public static string Moment(IEnumerable<Message> messages) => Transcript.Of(messages);

    private static string Describe(PicturedPerson person, bool withTags) => PromptText.Sentences(
        string.Join(' ', new[] { AgeFor(person.Age), TextFields.Clean(person.Gender) }.OfType<string>()),
        person.Reference is { } reference ? $"Shown in reference image {reference}" : null,
        PromptText.Labelled("Looks", PromptText.OneLine(person.Appearance, " ")),
        PromptText.Labelled("Changed since", PromptText.OneLine(person.AppearanceChanges, " ")),
        PromptText.Labelled("Wearing", PromptText.OneLine(person.Outfit, " ")),
        withTags ? PromptText.Labelled("Image tags", PromptText.OneLine(person.ImageTags, " ")) : null);

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
