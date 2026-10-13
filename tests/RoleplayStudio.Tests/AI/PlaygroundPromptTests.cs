using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.AI.Playground;

namespace RoleplayStudio.Tests.AI;

public class PlaygroundPromptTests
{
    private static readonly PromptBudget Budget = new(PromptBudget.DefaultContextWindow, PromptBudget.DefaultReplyTokens);

    [Fact]
    public void The_system_prompt_is_the_chat_prompt_for_one_adult_character()
    {
        var messages = PlaygroundPrompt.Build(new PlaygroundCharacter("  Mira ", "A cheerful innkeeper."), [], Budget);

        var system = messages[0];
        Assert.Equal(ChatRole.System, system.Role);
        Assert.StartsWith("You play Mira", system.Text);
        Assert.Contains("A cheerful innkeeper.", system.Text);
        Assert.Contains("Write as Mira, staying in character", messages[^1].Text);
        Assert.Contains("Everyone in the story is an adult.", system.Text);
    }

    [Fact]
    public void History_maps_to_user_and_assistant_turns_and_skips_empty_ones()
    {
        var messages = PlaygroundPrompt.Build(
            new PlaygroundCharacter("Mira", null),
            [new(true, "Hi"), new(false, "Hello there"), new(false, "  "), new(true, "How are you?")],
            Budget);

        Assert.Equal(
            [ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User],
            messages.Select(m => m.Role));
        Assert.Equal("Hello there", messages[2].Text);
        Assert.StartsWith("How are you?", messages[3].Text);
    }
}
