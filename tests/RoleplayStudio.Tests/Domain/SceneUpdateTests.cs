using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using static RoleplayStudio.Tests.Support.Scenes;

namespace RoleplayStudio.Tests.Domain;

public class SceneUpdateTests
{
    [Fact]
    public void A_newcomer_becomes_a_character_in_the_scene_wearing_what_the_story_said()
    {
        var (session, cast) = MiraAlone();

        var created = Parsed("""
            ```json
            {"present":["Mira","Old Bess"],"newcomers":[{"name":"Old Bess","role":"cook","age":61,"description":"The inn's cook.","appearance":"Stout, with a grey bun","outfit":"Kitchen whites"}]}
            ```
            """).ApplyTo(session, cast, "Sam", Now).Newcomers;

        var (bess, role) = Assert.Single(created);
        Assert.Equal(("Old Bess", "cook", 61, "Stout, with a grey bun", "Kitchen whites"), (bess.Name, role, bess.Age, bess.Appearance, bess.DefaultOutfit));
        Assert.Equal([Mira.Id, bess.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("Kitchen whites", session.CharacterStates.Single(s => s.CharacterId == bess.Id).CurrentOutfit);
    }

    [Fact]
    public void A_cast_member_who_walks_in_joins_in_their_default_outfit_and_is_not_created_again()
    {
        var (session, cast) = MiraAlone();

        var created = Parsed("""{"present":["Mira","Thorne"],"newcomers":[{"name":"Thorne Ashby"}]}""").ApplyTo(session, cast, "Sam", Now).Newcomers;

        Assert.Empty(created);
        Assert.Equal([Mira.Id, Thorne.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("An oilskin coat", session.CharacterStates.Single(s => s.CharacterId == Thorne.Id).CurrentOutfit);
    }

    [Fact]
    public void Someone_who_leaves_keeps_their_state_for_when_they_come_back()
    {
        var (session, cast) = MiraAlone();
        Parsed("""{"changes":[{"name":"Mira","status":"Tired","outfit":"A wool cardigan over her apron"}]}""").ApplyTo(session, cast, "Sam", Now);

        Parsed("""{"present":[]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Empty(session.Scene.PresentCharacterIds);
        var mira = Assert.Single(session.AbsentStates());
        Assert.Equal(("Tired", "A wool cardigan over her apron"), (mira.Status, mira.CurrentOutfit));
    }

    [Fact]
    public void A_change_without_an_outfit_or_with_an_empty_one_keeps_what_they_wear()
    {
        var (session, cast) = MiraAlone();

        Parsed("""{"changes":[{"name":"mira","status":"Warm"},{"name":"Mira","outfit":" "}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal(("Warm", "An apron and clogs"), (session.CharacterStates.Single().Status, session.CharacterStates.Single().CurrentOutfit));
    }

    [Fact]
    public void Without_a_present_list_who_is_there_stays_as_it_was()
    {
        var (session, cast) = MiraAlone();

        Parsed("""{"location":"The cellar","mood":"Hushed"}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
        Assert.Equal(("The cellar", "Hushed"), (session.Scene.Location, session.Scene.Mood));
    }

    [Fact]
    public void The_persona_and_names_nobody_describes_are_never_made_into_characters()
    {
        var (session, cast) = MiraAlone();

        var created = Parsed("""{"present":["Sam","Mira","The crowd"],"newcomers":[{"name":"Sam"}]}""").ApplyTo(session, cast, "Sam", Now).Newcomers;

        Assert.Empty(created);
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void The_persona_named_by_first_name_alone_is_not_made_into_a_character()
    {
        var (session, cast) = MiraAlone();

        var created = Parsed("""{"present":["Sam","Mira"],"newcomers":[{"name":"Sam"}]}""").ApplyTo(session, cast, "Sam Carter", Now).Newcomers;

        Assert.Empty(created);
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void A_newcomer_name_longer_than_a_character_name_can_be_is_cut_to_fit()
    {
        var (session, cast) = MiraAlone();
        var name = new string('n', Character.MaxNameLength + 50);

        var (character, _) = Assert.Single(Parsed($$"""{"newcomers":[{"name":"{{name}}"}]}""").ApplyTo(session, cast, "Sam", Now).Newcomers);

        Assert.Equal(Character.MaxNameLength, character.Name.Length);
    }

    [Fact]
    public void A_newcomer_the_story_gives_no_age_has_none()
    {
        var (session, cast) = MiraAlone();

        var (bess, _) = Assert.Single(Parsed("""{"newcomers":[{"name":"Old Bess"}]}""").ApplyTo(session, cast, "Sam", Now).Newcomers);

        Assert.Null(bess.Age);
    }

    [Fact]
    public void A_newcomer_is_marked_as_introduced_by_the_chat_that_met_them()
    {
        var (session, cast) = MiraAlone();

        var (bess, _) = Assert.Single(Parsed("""{"newcomers":[{"name":"Old Bess"}]}""").ApplyTo(session, cast, "Sam", Now).Newcomers);

        Assert.Equal(session.Id, bess.IntroducedInSessionId);
    }

    [Fact]
    public void Someone_the_chat_introduced_takes_the_name_the_story_gives_them_and_stays_in_the_scene()
    {
        var (session, cast, stablehand) = WithStablehand();

        var outcome = Parsed("""{"present":["Tom"],"changes":[{"name":"The stablehand","newName":"Tom"}],"newcomers":[{"name":"Tom"}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal("Tom", stablehand.Name);
        Assert.Empty(outcome.Newcomers);
        Assert.Equal([stablehand], outcome.Renamed);
        Assert.Equal([stablehand.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void Someone_renamed_is_still_found_by_their_former_name_in_the_same_update()
    {
        var (session, cast, stablehand) = WithStablehand();

        Parsed("""{"present":["The stablehand"],"changes":[{"name":"The stablehand","newName":"Tom","status":"Shy"}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal([stablehand.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("Shy", session.CharacterStates.Single().Status);
    }

    [Fact]
    public void Looks_the_story_reveals_fill_in_an_introduced_character_and_mark_them_for_a_new_portrait()
    {
        var (session, cast, stablehand) = WithStablehand();

        var outcome = Parsed("""{"changes":[{"name":"The stablehand","appearance":"Lanky, freckled, with red hair","age":"17","gender":"male"}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal(("Lanky, freckled, with red hair", 17, "male"), (stablehand.Appearance, stablehand.Age, stablehand.Gender));
        Assert.Equal([stablehand], outcome.Restyled);
    }

    [Fact]
    public void Looks_given_again_unchanged_ask_for_no_new_portrait()
    {
        var (session, cast, _) = WithStablehand();

        var outcome = Parsed("""{"changes":[{"name":"The stablehand","appearance":"lanky"}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Empty(outcome.Restyled);
    }

    [Fact]
    public void Looks_only_reworded_are_kept_but_ask_for_no_new_portrait()
    {
        var (session, cast, stablehand) = WithStablehand();
        stablehand.Appearance = "Lanky, with red hair and freckles";

        var outcome = Parsed("""{"changes":[{"name":"The stablehand","appearance":"Lanky; red hair, freckled"}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal("Lanky; red hair, freckled", stablehand.Appearance);
        Assert.Empty(outcome.Restyled);
    }

    [Fact]
    public void A_character_the_user_wrote_is_never_renamed_or_restyled_by_the_story()
    {
        var mira = new Character { Name = "Mira", Age = 25, Appearance = "Short and round" };
        var session = new ChatSession();
        session.SetPresent([mira], Now);
        session.CharacterStates[0].Character = mira;

        var outcome = Parsed("""{"changes":[{"name":"Mira","newName":"Mirabel","appearance":"Tall","age":40}]}""").ApplyTo(session, new SceneCast(session.CharacterStates, []), "Sam", Now);

        Assert.Equal(("Mira", "Short and round", (int?)25), (mira.Name, mira.Appearance, mira.Age));
        Assert.Empty(outcome.Restyled);
    }

    [Fact]
    public void A_new_name_another_character_or_the_persona_already_has_is_refused()
    {
        var (session, cast, stablehand) = WithStablehand();

        Parsed("""{"changes":[{"name":"The stablehand","newName":"Mira"}]}""").ApplyTo(session, cast, "Sam", Now);
        Parsed("""{"changes":[{"name":"The stablehand","newName":"Sam"}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal("The stablehand", stablehand.Name);
    }
}
