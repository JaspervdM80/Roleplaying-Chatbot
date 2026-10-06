using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.Domain;

public class AuthoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static Character Character(string name, string? top = null) => new() { Name = name, DefaultOutfit = new Outfit { Top = top } };

    [Fact]
    public void A_scenario_cannot_start_with_someone_outside_the_cast()
    {
        var scenario = new Scenario { Title = "Picnic", StartingCharacterIds = [Guid.NewGuid()] };

        Assert.Equal("StartingCharacterIds", scenario.FindProblem([Guid.NewGuid()])?.Field);
    }

    [Fact]
    public void A_scenario_problem_names_the_field_the_editor_points_at()
    {
        Assert.Equal("Title", new Scenario { Title = " " }.FindProblem([])?.Field);
        Assert.Null(new Scenario { Title = "Picnic" }.FindProblem([]));
    }

    [Fact]
    public void A_scenario_stores_the_same_starting_characters_in_one_order_however_they_were_picked()
    {
        var (mira, jun) = (Guid.NewGuid(), Guid.NewGuid());
        var picked = new Scenario();
        var repicked = new Scenario();

        picked.CopyEditableFieldsFrom(new Scenario { StartingCharacterIds = [mira, jun] });
        repicked.CopyEditableFieldsFrom(new Scenario { StartingCharacterIds = [jun, mira, jun] });

        Assert.Equal(picked.StartingCharacterIds, repicked.StartingCharacterIds);
    }

    [Fact]
    public void A_character_problem_names_the_field_the_editor_points_at()
    {
        Assert.Equal("Name", new Character { Name = " " }.FindProblem()?.Field);
        Assert.Equal("Age", new Character { Name = "Mira", Age = -1 }.FindProblem()?.Field);
        Assert.Null(new Character { Name = "Mira" }.FindProblem());
    }

    [Fact]
    public void A_persona_problem_names_the_field_the_editor_points_at()
    {
        Assert.Equal("Name", new Persona { Name = " " }.FindProblem()?.Field);
        Assert.Equal("Age", new Persona { Name = "Sam", Age = -1 }.FindProblem()?.Field);
        Assert.Null(new Persona { Name = "Sam" }.FindProblem());
    }

    [Fact]
    public void A_chatbot_problem_names_the_field_the_editor_points_at()
    {
        Assert.Equal("Name", new Chatbot { Name = " " }.FindProblem()?.Field);
        Assert.Null(new Chatbot { Name = "The Lantern Inn" }.FindProblem());
    }

    [Fact]
    public void A_scenario_with_no_starting_characters_starts_with_the_whole_cast()
    {
        var cast = new[] { Character("Mira"), Character("Jun") };

        Assert.Equal(cast, new Scenario().PresentAtStart(cast));
    }

    [Fact]
    public void Editing_copies_cleaned_text_and_turns_blank_fields_into_null()
    {
        var persona = new Persona();
        persona.CopyEditableFieldsFrom(new Persona { Name = "  Sam ", Description = "   ", Appearance = new Appearance { Hair = " red " } });

        Assert.Equal("Sam", persona.Name);
        Assert.Null(persona.Description);
        Assert.Equal("red", persona.Appearance.Hair);
    }

    [Fact]
    public void A_started_chat_dresses_who_is_present_in_their_default_outfit()
    {
        var mira = Character("Mira", top: "apron");
        var jun = Character("Jun", top: "hoodie");
        var scenario = new Scenario { Title = "Morning rush", StartingLocation = "Café", StartingCharacterIds = [mira.Id] };

        var session = ChatSession.Start(scenario, new Persona { Name = "Sam" }, [mira, jun], null, Now);

        Assert.Equal([mira.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("Café", session.Scene.Location);
        var state = Assert.Single(session.CharacterStates);
        Assert.Equal("apron", state.CurrentOutfit.Top);

        // The session owns its own outfit, so a change during the chat never rewrites the character.
        state.CurrentOutfit.Top = "raincoat";
        Assert.Equal("apron", mira.DefaultOutfit.Top);
    }

    [Fact]
    public void The_opening_message_comes_first_and_is_spoken_by_the_one_character_present()
    {
        var mira = Character("Mira");
        var scenario = new Scenario { Title = "Morning rush", OpeningMessage = "Morning! The usual?", StartingCharacterIds = [mira.Id] };

        var session = ChatSession.Start(scenario, new Persona { Name = "Sam" }, [mira], null, Now);

        var opening = Assert.Single(session.Messages);
        Assert.Equal(1, opening.Sequence);
        Assert.Equal(MessageRole.Character, opening.Role);
        Assert.Equal(mira.Id, opening.SpeakerCharacterId);
    }

    [Fact]
    public void A_group_scene_is_answered_by_the_narrator()
    {
        var (role, characterId, name) = ChatSession.ReplySpeaker([Character("Mira"), Character("Jun")]);

        Assert.Equal(MessageRole.Narrator, role);
        Assert.Null(characterId);
        Assert.Equal(ChatSession.NarratorName, name);
    }

    [Fact]
    public void A_scenario_without_an_opening_message_starts_an_empty_chat()
    {
        var session = ChatSession.Start(new Scenario { Title = "Quiet" }, new Persona { Name = "Sam" }, [], null, Now);

        Assert.Empty(session.Messages);
        Assert.Empty(session.CharacterStates);
    }
}
