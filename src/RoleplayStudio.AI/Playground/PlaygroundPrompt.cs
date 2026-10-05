using Microsoft.Extensions.AI;

namespace RoleplayStudio.AI.Playground;

public sealed record PlaygroundCharacter(string Name, string? Description);

public sealed record PlaygroundTurn(bool FromUser, string Text);

/// <summary>A single character chatting one-on-one, kept in memory only, until authored characters and sessions exist.</summary>
public static class PlaygroundPrompt
{
    public static IReadOnlyList<ChatMessage> Build(PlaygroundCharacter character, IReadOnlyList<PlaygroundTurn> history)
    {
        var name = character.Name.Trim();
        var system = $"You are {name}, an adult character in a roleplay chat with the user.";
        if (!string.IsNullOrWhiteSpace(character.Description))
        {
            system += $"\n\n{character.Description.Trim()}";
        }

        system += $"\n\nStay in character and write only {name}'s words and actions, never the user's. Put actions in *asterisks*.";

        return
        [
            new ChatMessage(ChatRole.System, system),
            .. history
                .Where(turn => !string.IsNullOrWhiteSpace(turn.Text))
                .Select(turn => new ChatMessage(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text)),
        ];
    }
}
