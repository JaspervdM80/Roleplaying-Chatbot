using RoleplayStudio.AI.Scene;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.AI;

public class SceneTrackingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly Character Mira = new() { Name = "Mira", DefaultOutfit = new Outfit { Top = "apron", Footwear = "clogs" } };
    private static readonly Character Thorne = new() { Name = "Thorne Ashby", DefaultOutfit = new Outfit { Top = "oilskin coat" } };

    /// <summary>A chat with Mira present, and Thorne in the cast but not met yet.</summary>
    private static (ChatSession Session, SceneCast Cast) MiraAlone()
    {
        var session = new ChatSession { Scene = new SceneState { Location = "The bar" } };
        session.SetPresent([Mira], Now);
        session.CharacterStates[0].Character = Mira;
        return (session, new SceneCast(session.CharacterStates, [Mira, Thorne]));
    }

    private static SceneUpdate Parsed(string json) => SceneTracking.Parse(json) ?? throw new InvalidOperationException("The test JSON did not parse");

    [Fact]
    public void A_reply_that_is_not_json_is_no_update()
    {
        Assert.Null(SceneTracking.Parse("The scene is the same as before."));
    }

    [Fact]
    public void A_newcomer_becomes_a_character_in_the_scene_wearing_what_the_story_said()
    {
        var (session, cast) = MiraAlone();

        var created = SceneTracking.Apply(session, cast, Parsed("""
            ```json
            {"present":["Mira","Old Bess"],"newcomers":[{"name":"Old Bess","role":"cook","age":61,"description":"The inn's cook.","appearance":{"hair":"grey bun"},"outfit":{"top":"kitchen whites"}}]}
            ```
            """), "Sam", Now);

        var (bess, role) = Assert.Single(created);
        Assert.Equal(("Old Bess", "cook", 61, "grey bun", "kitchen whites"), (bess.Name, role, bess.Age, bess.Appearance.Hair, bess.DefaultOutfit.Top));
        Assert.Equal([Mira.Id, bess.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("kitchen whites", session.CharacterStates.Single(s => s.CharacterId == bess.Id).CurrentOutfit.Top);
    }

    [Fact]
    public void A_cast_member_who_walks_in_joins_in_their_default_outfit_and_is_not_created_again()
    {
        var (session, cast) = MiraAlone();

        var created = SceneTracking.Apply(session, cast, Parsed("""{"present":["Mira","Thorne"],"newcomers":[{"name":"Thorne Ashby"}]}"""), "Sam", Now);

        Assert.Empty(created);
        Assert.Equal([Mira.Id, Thorne.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("oilskin coat", session.CharacterStates.Single(s => s.CharacterId == Thorne.Id).CurrentOutfit.Top);
    }

    [Fact]
    public void Someone_who_leaves_keeps_their_state_for_when_they_come_back()
    {
        var (session, cast) = MiraAlone();
        SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"Mira","status":"Tired","outfit":{"top":"wool cardigan"}}]}"""), "Sam", Now);

        SceneTracking.Apply(session, cast, Parsed("""{"present":[]}"""), "Sam", Now);

        Assert.Empty(session.Scene.PresentCharacterIds);
        var mira = Assert.Single(session.AbsentStates(session.CharacterStates));
        Assert.Equal(("Tired", "wool cardigan"), (mira.Status, mira.CurrentOutfit.Top));
    }

    [Fact]
    public void An_outfit_change_keeps_pieces_left_out_and_drops_pieces_given_as_empty()
    {
        var (session, cast) = MiraAlone();

        SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"mira","outfit":{"footwear":"","accessories":"red scarf"}}]}"""), "Sam", Now);

        var outfit = session.CharacterStates.Single().CurrentOutfit;
        Assert.Equal(("apron", null, "red scarf"), (outfit.Top, outfit.Footwear, outfit.Accessories));
    }

    [Fact]
    public void Without_a_present_list_who_is_there_stays_as_it_was()
    {
        var (session, cast) = MiraAlone();

        SceneTracking.Apply(session, cast, Parsed("""{"location":"The cellar","mood":"Hushed"}"""), "Sam", Now);

        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
        Assert.Equal(("The cellar", "Hushed"), (session.Scene.Location, session.Scene.Mood));
    }

    [Fact]
    public void The_persona_and_names_nobody_describes_are_never_made_into_characters()
    {
        var (session, cast) = MiraAlone();

        var created = SceneTracking.Apply(session, cast, Parsed("""{"present":["Sam","Mira","The crowd"],"newcomers":[{"name":"Sam"}]}"""), "Sam", Now);

        Assert.Empty(created);
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void The_prompt_names_who_is_here_who_left_and_who_has_not_been_met()
    {
        var (session, cast) = MiraAlone();

        var story = SceneTracking.Prompt(session, "Sam", cast, [new Message { Sequence = 1, SpeakerName = "Narrator", Content = "The door opens." }])[1].Text;

        Assert.Contains("- Mira (in the scene). Wearing: Top: apron; Footwear: clogs.", story);
        Assert.Contains("- Thorne Ashby (not met yet).", story);
        Assert.Contains("Narrator: The door opens.", story);
    }
}
