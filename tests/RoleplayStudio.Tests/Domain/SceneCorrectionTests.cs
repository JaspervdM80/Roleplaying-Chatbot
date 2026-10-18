using RoleplayStudio.AI.Scene;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using static RoleplayStudio.Tests.Support.Scenes;

namespace RoleplayStudio.Tests.Domain;

public class SceneCorrectionTests
{
    private static readonly SceneFacts NoFacts = new(null, null, null);

    [Fact]
    public void A_corrected_character_takes_the_trimmed_fields_and_one_emptied_is_cleared()
    {
        var (session, _) = MiraAlone();
        var state = session.CharacterStates.Single();
        state.Status = "Tired";
        var opened = CharacterSnapshot.Of(state);
        var later = Now.AddHours(1);

        var corrected = session.CorrectCharacter(opened, new CharacterSnapshot
        {
            CharacterId = Mira.Id,
            CurrentOutfit = "  A green wool dress  ",
            Status = " ",
            Relationship = "Old friends",
            AppearanceChanges = "Flour on her cheek",
        }, later);

        Assert.True(corrected);
        Assert.Equal(("A green wool dress", null, "Old friends", "Flour on her cheek", later), (state.CurrentOutfit, state.Status, state.Relationship, state.AppearanceChanges, state.UpdatedAt));
    }

    [Fact]
    public void A_correction_keeps_what_tracking_changed_while_the_dialog_was_open_in_the_fields_the_user_left_alone()
    {
        var (session, cast) = MiraAlone();
        var opened = CharacterSnapshot.Of(session.CharacterStates.Single());
        var sceneOpened = SceneFacts.Of(session.Scene);
        Parsed("""{"location":"The yard","mood":"Tense","changes":[{"name":"Mira","status":"Wary","relationship":"Rivals"}]}""").ApplyTo(session, cast, "Sam", Now);

        session.CorrectCharacter(opened, new CharacterSnapshot { CharacterId = Mira.Id, CurrentOutfit = "A shawl" }, Now);
        session.CorrectScene(sceneOpened, sceneOpened with { TimeOfDay = "Dawn" });

        var mira = session.CharacterStates.Single();
        Assert.Equal(("A shawl", "Wary", "Rivals"), (mira.CurrentOutfit, mira.Status, mira.Relationship));
        Assert.Equal(("The yard", "Dawn", "Tense"), (session.Scene.Location, session.Scene.TimeOfDay, session.Scene.Mood));
    }

    [Fact]
    public void Someone_not_met_in_the_chat_cannot_be_corrected()
    {
        var (session, _) = MiraAlone();

        Assert.False(session.CorrectCharacter(new CharacterSnapshot(), new CharacterSnapshot { CharacterId = Thorne.Id, CurrentOutfit = "A towel" }, Now));
        Assert.Single(session.CharacterStates);
    }

    [Fact]
    public void Every_correction_moves_the_history_version_on_so_upkeep_in_flight_cannot_save_over_it()
    {
        var (session, _) = MiraAlone();
        var sam = new Persona { Name = "Sam", DefaultOutfit = "A grey hoodie" };

        session.CorrectScene(NoFacts, NoFacts with { Location = "The cellar" });
        session.CorrectPersonaOutfit(sam, "A grey hoodie", "A raincoat");
        session.CorrectCharacter(new CharacterSnapshot(), new CharacterSnapshot { CharacterId = Mira.Id, CurrentOutfit = "A shawl" }, Now);

        Assert.Equal(3, session.HistoryVersion);
    }

    [Fact]
    public void A_corrected_scene_takes_the_trimmed_facts_and_keeps_who_is_present()
    {
        var (session, _) = MiraAlone();
        session.Scene.Mood = "Busy";

        session.CorrectScene(SceneFacts.Of(session.Scene), new SceneFacts(" The cellar ", "Midnight", ""));

        Assert.Equal(("The cellar", "Midnight", null), (session.Scene.Location, session.Scene.TimeOfDay, session.Scene.Mood));
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
    }

    [Fact]
    public void A_blank_or_default_outfit_puts_the_persona_back_in_their_default_one()
    {
        var (session, _) = MiraAlone();
        var sam = new Persona { Name = "Sam", DefaultOutfit = "A grey hoodie" };

        session.CorrectPersonaOutfit(sam, "A grey hoodie", "A raincoat");
        var changed = session.Scene.PersonaWears(sam);
        session.CorrectPersonaOutfit(sam, "A raincoat", " ");
        var blank = session.Scene.PersonaOutfit;
        session.CorrectPersonaOutfit(sam, "A grey hoodie", "A towel");
        session.CorrectPersonaOutfit(sam, "A towel", "A grey hoodie");

        Assert.Equal(("A raincoat", null, null), (changed, blank, session.Scene.PersonaOutfit));
    }

    [Fact]
    public void Scene_tracking_reports_how_someone_stands_with_the_persona_and_keeps_it_when_it_says_nothing()
    {
        var (session, cast) = MiraAlone();

        Parsed("""{"changes":[{"name":"Mira","relationship":"Old friends, a little jealous"}]}""").ApplyTo(session, cast, "Sam", Now);
        Parsed("""{"changes":[{"name":"Mira","status":"Warm"}]}""").ApplyTo(session, cast, "Sam", Now);

        Assert.Equal("Old friends, a little jealous", session.CharacterStates.Single().Relationship);
    }

    [Fact]
    public void Scene_tracking_is_shown_how_a_known_character_stands_with_the_persona()
    {
        var (session, cast) = MiraAlone();
        session.CharacterStates[0].Relationship = "Old friends";
        var sam = new Persona { Name = "Sam" };

        var prompt = SceneTracking.Prompt(session, sam, cast, [new Message { Sequence = 1, SpeakerName = "Sam", Content = "Hello." }])[1].Text;

        Assert.Contains("With Sam: Old friends", prompt);
    }

    [Fact]
    public void Rewinding_puts_back_how_someone_stood_with_the_persona()
    {
        var (session, _) = MiraAlone();
        session.CharacterStates[0].Relationship = "Strangers";
        var first = new Message { Sequence = 1, SceneBefore = session.Snapshot() };
        session.Scene.TrackedUpToSequence = 1;
        session.CharacterStates[0].Relationship = "Old friends";

        session.Rewind(1, [first], []);

        Assert.Equal("Strangers", session.CharacterStates.Single().Relationship);
    }
}
