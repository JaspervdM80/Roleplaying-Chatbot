using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Playground;

namespace RoleplayStudio.Tests.AI;

public class PlaygroundPromptTests
{
    [Fact]
    public void The_system_prompt_names_the_character_as_an_adult()
    {
        var messages = PlaygroundPrompt.Build(new PlaygroundCharacter("  Mira ", "A cheerful innkeeper."), []);

        var system = Assert.Single(messages);
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Contains("You are Mira, an adult character", system.Text);
        Assert.Contains("A cheerful innkeeper.", system.Text);
    }

    [Fact]
    public void History_maps_to_user_and_assistant_turns_and_skips_empty_ones()
    {
        var messages = PlaygroundPrompt.Build(
            new PlaygroundCharacter("Mira", null),
            [new(true, "Hi"), new(false, "Hello there"), new(false, "  "), new(true, "How are you?")]);

        Assert.Equal(
            [ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User],
            messages.Select(m => m.Role));
    }
}
