using RoleplayStudio.AI.Scene;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.AI;

public class SceneTrackingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly Character Mira = new() { Name = "Mira", DefaultOutfit = "An apron and clogs" };
    private static readonly Character Thorne = new() { Name = "Thorne Ashby", DefaultOutfit = "An oilskin coat" };

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
            {"present":["Mira","Old Bess"],"newcomers":[{"name":"Old Bess","role":"cook","age":61,"description":"The inn's cook.","appearance":"Stout, with a grey bun","outfit":"Kitchen whites"}]}
            ```
            """), "Sam", Now);

        var (bess, role) = Assert.Single(created);
        Assert.Equal(("Old Bess", "cook", 61, "Stout, with a grey bun", "Kitchen whites"), (bess.Name, role, bess.Age, bess.Appearance, bess.DefaultOutfit));
        Assert.Equal([Mira.Id, bess.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("Kitchen whites", session.CharacterStates.Single(s => s.CharacterId == bess.Id).CurrentOutfit);
    }

    [Fact]
    public void A_cast_member_who_walks_in_joins_in_their_default_outfit_and_is_not_created_again()
    {
        var (session, cast) = MiraAlone();

        var created = SceneTracking.Apply(session, cast, Parsed("""{"present":["Mira","Thorne"],"newcomers":[{"name":"Thorne Ashby"}]}"""), "Sam", Now);

        Assert.Empty(created);
        Assert.Equal([Mira.Id, Thorne.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("An oilskin coat", session.CharacterStates.Single(s => s.CharacterId == Thorne.Id).CurrentOutfit);
    }

    [Fact]
    public void Someone_who_leaves_keeps_their_state_for_when_they_come_back()
    {
        var (session, cast) = MiraAlone();
        SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"Mira","status":"Tired","outfit":"A wool cardigan over her apron"}]}"""), "Sam", Now);

        SceneTracking.Apply(session, cast, Parsed("""{"present":[]}"""), "Sam", Now);

        Assert.Empty(session.Scene.PresentCharacterIds);
        var mira = Assert.Single(session.AbsentStates(session.CharacterStates));
        Assert.Equal(("Tired", "A wool cardigan over her apron"), (mira.Status, mira.CurrentOutfit));
    }

    [Fact]
    public void A_change_without_an_outfit_or_with_an_empty_one_keeps_what_they_wear()
    {
        var (session, cast) = MiraAlone();

        SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"mira","status":"Warm"},{"name":"Mira","outfit":" "}]}"""), "Sam", Now);

        Assert.Equal(("Warm", "An apron and clogs"), (session.CharacterStates.Single().Status, session.CharacterStates.Single().CurrentOutfit));
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
    public void The_persona_named_by_first_name_alone_is_not_made_into_a_character()
    {
        var (session, cast) = MiraAlone();

        var created = SceneTracking.Apply(session, cast, Parsed("""{"present":["Sam","Mira"],"newcomers":[{"name":"Sam"}]}"""), "Sam Carter", Now);

        Assert.Empty(created);
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void A_newcomer_name_longer_than_a_character_name_can_be_is_cut_to_fit()
    {
        var (session, cast) = MiraAlone();
        var name = new string('n', Character.MaxNameLength + 50);

        var (character, _) = Assert.Single(SceneTracking.Apply(session, cast, Parsed($$"""{"newcomers":[{"name":"{{name}}"}]}"""), "Sam", Now));

        Assert.Equal(Character.MaxNameLength, character.Name.Length);
    }

    [Fact]
    public void An_age_written_as_text_is_still_read()
    {
        var newcomer = Assert.Single(Parsed("""{"newcomers":[{"name":"Old Bess","age":"61"}]}""").Newcomers);

        Assert.Equal(61, newcomer.Age);
    }

    [Fact]
    public void The_prompt_names_who_is_here_who_left_and_who_has_not_been_met()
    {
        var (session, cast) = MiraAlone();

        var story = SceneTracking.Prompt(session, "Sam", cast, [new Message { Sequence = 1, SpeakerName = "Narrator", Content = "The door opens." }])[1].Text;

        Assert.Contains("- Mira (in the scene). Wearing: An apron and clogs.", story);
        Assert.Contains("- Thorne Ashby (not met yet).", story);
        Assert.Contains("Narrator: The door opens.", story);
    }
}
