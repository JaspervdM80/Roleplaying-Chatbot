using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Playground;

public sealed record PlaygroundCharacter(string Name, string? Description);

public sealed record PlaygroundTurn(bool FromUser, string Text);

/// <summary>A single character chatting one-on-one, kept in memory only, prompted the way a chat with one character is.</summary>
public static class PlaygroundPrompt
{
    public static IReadOnlyList<ChatMessage> Build(PlaygroundCharacter character, IReadOnlyList<PlaygroundTurn> history, PromptBudget budget)
    {
        var cast = new Character { Name = character.Name.Trim(), ShortDescription = character.Description };
        var turns = history.Where(turn => !string.IsNullOrWhiteSpace(turn.Text)).ToList();
        var messages = turns
            .Select((turn, i) => new Message
            {
                Sequence = i + 1,
                Role = turn.FromUser ? MessageRole.User : MessageRole.Character,
                SpeakerCharacterId = turn.FromUser ? null : cast.Id,
                SpeakerName = turn.FromUser ? "" : cast.Name,
                Content = turn.Text,
            })
            .ToList();

        var input = new PromptInput(
            new Chatbot { Name = "a world the story makes up as it goes", ToneAndRules = "Everyone in the story is an adult." },
            new Scenario(),
            new Persona { Name = "the user" },
            new SceneState { PresentCharacterIds = [cast.Id] },
            [new PresentCharacter(cast, new CharacterState { CharacterId = cast.Id, Character = cast })],
            [],
            new SessionSummary(),
            [],
            messages);
        return PromptBuilder.Build(input, budget);
    }
}
