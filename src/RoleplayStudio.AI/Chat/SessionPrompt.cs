using System.Text;
using Microsoft.Extensions.AI;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Chat;

public sealed record PresentCharacter(Character Character, CharacterState State);

public sealed record SessionPromptInput(
    Chatbot Chatbot,
    Scenario Scenario,
    Persona Persona,
    SceneState Scene,
    IReadOnlyList<PresentCharacter> Present,
    IReadOnlyList<Message> Messages);

/// <summary>The whole chat as one prompt, unbudgeted, until the token-budgeted PromptBuilder replaces it.</summary>
public static class SessionPrompt
{
    public static IReadOnlyList<ChatMessage> Build(SessionPromptInput input)
    {
        return
        [
            new ChatMessage(ChatRole.System, System(input)),
            .. input.Messages
                .OrderBy(m => m.Sequence)
                .Select(m => new ChatMessage(m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)),
        ];
    }

    private static string System(SessionPromptInput input)
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
            persona.Appearance.Describe(),
            Labelled("Wearing", persona.DefaultOutfit.Describe(), "\n")));

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
                character.Appearance.Describe(),
                Labelled("Wearing now", state.CurrentOutfit.Describe(), "\n"),
                Labelled("Changes since the start", state.AppearanceChanges)));
        }

        Section(text, "Scenario", Join(
            input.Scenario.Title,
            input.Scenario.Premise,
            Labelled("Location", input.Scene.Location),
            Labelled("Time of day", input.Scene.TimeOfDay),
            Labelled("Mood", input.Scene.Mood),
            Labelled("Goals", input.Scenario.Goals)));

        text.AppendLine();
        text.Append(Instruction(input));
        return text.ToString();
    }

    private static string Instruction(SessionPromptInput input)
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
}
