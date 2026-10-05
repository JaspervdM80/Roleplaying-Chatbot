using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.AI;

public class SessionPromptTests
{
    private static PresentCharacter Present(string name, string? top = null) =>
        new(new Character { Name = name }, new CharacterState { CurrentOutfit = new Outfit { Top = top } });

    private static SessionPromptInput Input(IReadOnlyList<PresentCharacter> present, params Message[] messages) => new(
        new Chatbot { Name = "Seaside Café", WorldDescription = "A quiet harbour town." },
        new Scenario { Title = "Morning rush" },
        new Persona { Name = "Sam" },
        new SceneState { Location = "Behind the counter" },
        present,
        messages);

    [Fact]
    public void The_system_prompt_describes_the_world_and_what_each_present_character_wears_now()
    {
        var system = SessionPrompt.Build(Input([Present("Mira", top: "raincoat")]))[0];

        Assert.Equal(ChatRole.System, system.Role);
        Assert.Contains("A quiet harbour town.", system.Text);
        Assert.Contains("raincoat", system.Text);
        Assert.Contains("Behind the counter", system.Text);
    }

    [Fact]
    public void A_group_scene_asks_for_speaker_tags_naming_everyone_present()
    {
        var system = SessionPrompt.Build(Input([Present("Mira"), Present("Jun")]))[0].Text;

        Assert.Contains("**Mira:**", system);
        Assert.Contains("Mira, Jun", system);
    }

    [Fact]
    public void Messages_follow_in_sequence_with_the_user_as_user_and_everyone_else_as_assistant()
    {
        var messages = SessionPrompt.Build(Input(
            [Present("Mira")],
            new Message { Sequence = 2, Role = MessageRole.User, Content = "A latte, please." },
            new Message { Sequence = 1, Role = MessageRole.Character, Content = "Morning!" }));

        Assert.Equal([ChatRole.System, ChatRole.Assistant, ChatRole.User], messages.Select(m => m.Role));
        Assert.Equal("Morning!", messages[1].Text);
    }
}
