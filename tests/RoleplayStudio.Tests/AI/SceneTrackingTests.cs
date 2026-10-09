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
            """), "Sam", Now).Newcomers;

        var (bess, role) = Assert.Single(created);
        Assert.Equal(("Old Bess", "cook", 61, "Stout, with a grey bun", "Kitchen whites"), (bess.Name, role, bess.Age, bess.Appearance, bess.DefaultOutfit));
        Assert.Equal([Mira.Id, bess.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("Kitchen whites", session.CharacterStates.Single(s => s.CharacterId == bess.Id).CurrentOutfit);
    }

    [Fact]
    public void A_cast_member_who_walks_in_joins_in_their_default_outfit_and_is_not_created_again()
    {
        var (session, cast) = MiraAlone();

        var created = SceneTracking.Apply(session, cast, Parsed("""{"present":["Mira","Thorne"],"newcomers":[{"name":"Thorne Ashby"}]}"""), "Sam", Now).Newcomers;

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

        var created = SceneTracking.Apply(session, cast, Parsed("""{"present":["Sam","Mira","The crowd"],"newcomers":[{"name":"Sam"}]}"""), "Sam", Now).Newcomers;

        Assert.Empty(created);
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void The_persona_named_by_first_name_alone_is_not_made_into_a_character()
    {
        var (session, cast) = MiraAlone();

        var created = SceneTracking.Apply(session, cast, Parsed("""{"present":["Sam","Mira"],"newcomers":[{"name":"Sam"}]}"""), "Sam Carter", Now).Newcomers;

        Assert.Empty(created);
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void A_newcomer_name_longer_than_a_character_name_can_be_is_cut_to_fit()
    {
        var (session, cast) = MiraAlone();
        var name = new string('n', Character.MaxNameLength + 50);

        var (character, _) = Assert.Single(SceneTracking.Apply(session, cast, Parsed($$"""{"newcomers":[{"name":"{{name}}"}]}"""), "Sam", Now).Newcomers);

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

    /// <summary>A chat whose scene tracking met someone it could only call "The stablehand", with the looks it guessed.</summary>
    private static (ChatSession Session, SceneCast Cast, Character Stablehand) WithStablehand()
    {
        var session = new ChatSession { Scene = new SceneState { Location = "The yard" } };
        var stablehand = new Character { Name = "The stablehand", Age = 20, Appearance = "Lanky", IntroducedInSessionId = session.Id };
        session.SetPresent([stablehand], Now);
        session.CharacterStates[0].Character = stablehand;
        return (session, new SceneCast(session.CharacterStates, [Mira]), stablehand);
    }

    [Fact]
    public void A_newcomer_is_marked_as_introduced_by_the_chat_that_met_them()
    {
        var (session, cast) = MiraAlone();

        var (bess, _) = Assert.Single(SceneTracking.Apply(session, cast, Parsed("""{"newcomers":[{"name":"Old Bess"}]}"""), "Sam", Now).Newcomers);

        Assert.Equal(session.Id, bess.IntroducedInSessionId);
    }

    [Fact]
    public void Someone_the_chat_introduced_takes_the_name_the_story_gives_them_and_stays_in_the_scene()
    {
        var (session, cast, stablehand) = WithStablehand();

        var outcome = SceneTracking.Apply(session, cast, Parsed("""{"present":["Tom"],"changes":[{"name":"The stablehand","newName":"Tom"}],"newcomers":[{"name":"Tom"}]}"""), "Sam", Now);

        Assert.Equal("Tom", stablehand.Name);
        Assert.Empty(outcome.Newcomers);
        Assert.Equal([stablehand.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void Someone_renamed_is_still_found_by_their_former_name_in_the_same_update()
    {
        var (session, cast, stablehand) = WithStablehand();

        SceneTracking.Apply(session, cast, Parsed("""{"present":["The stablehand"],"changes":[{"name":"The stablehand","newName":"Tom","status":"Shy"}]}"""), "Sam", Now);

        Assert.Equal([stablehand.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("Shy", session.CharacterStates.Single().Status);
    }

    [Fact]
    public void Looks_the_story_reveals_fill_in_an_introduced_character_and_mark_them_for_a_new_portrait()
    {
        var (session, cast, stablehand) = WithStablehand();

        var outcome = SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"The stablehand","appearance":"Lanky, freckled, with red hair","age":"17","gender":"male"}]}"""), "Sam", Now);

        Assert.Equal(("Lanky, freckled, with red hair", 17, "male"), (stablehand.Appearance, stablehand.Age, stablehand.Gender));
        Assert.Equal([stablehand], outcome.Restyled);
    }

    [Fact]
    public void Looks_given_again_unchanged_ask_for_no_new_portrait()
    {
        var (session, cast, _) = WithStablehand();

        var outcome = SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"The stablehand","appearance":"lanky"}]}"""), "Sam", Now);

        Assert.Empty(outcome.Restyled);
    }

    [Fact]
    public void Looks_only_reworded_are_kept_but_ask_for_no_new_portrait()
    {
        var (session, cast, stablehand) = WithStablehand();
        stablehand.Appearance = "Lanky, with red hair and freckles";

        var outcome = SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"The stablehand","appearance":"Lanky; red hair, freckled"}]}"""), "Sam", Now);

        Assert.Equal("Lanky; red hair, freckled", stablehand.Appearance);
        Assert.Empty(outcome.Restyled);
    }

    [Fact]
    public void A_character_the_user_wrote_is_never_renamed_or_restyled_by_the_story()
    {
        var mira = new Character { Name = "Mira", Appearance = "Short and round" };
        var session = new ChatSession();
        session.SetPresent([mira], Now);
        session.CharacterStates[0].Character = mira;

        var outcome = SceneTracking.Apply(session, new SceneCast(session.CharacterStates, []), Parsed("""{"changes":[{"name":"Mira","newName":"Mirabel","appearance":"Tall","age":40}]}"""), "Sam", Now);

        Assert.Equal(("Mira", "Short and round", Character.MinimumAge), (mira.Name, mira.Appearance, mira.Age));
        Assert.Empty(outcome.Restyled);
    }

    [Fact]
    public void A_new_name_another_character_or_the_persona_already_has_is_refused()
    {
        var (session, cast, stablehand) = WithStablehand();

        SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"The stablehand","newName":"Mira"}]}"""), "Sam", Now);
        SceneTracking.Apply(session, cast, Parsed("""{"changes":[{"name":"The stablehand","newName":"Sam"}]}"""), "Sam", Now);

        Assert.Equal("The stablehand", stablehand.Name);
    }

    [Fact]
    public void The_prompt_shows_what_is_known_of_an_introduced_character_so_the_model_can_build_on_it()
    {
        var (session, cast, _) = WithStablehand();

        var story = SceneTracking.Prompt(session, "Sam", cast, [new Message { Sequence = 1, SpeakerName = "Narrator", Content = "He grins." }])[1].Text;

        Assert.Contains("Introduced in this story. Age: 20. Gender: unknown. Looks: Lanky.", story);
    }
}
