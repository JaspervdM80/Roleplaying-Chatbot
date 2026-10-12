using RoleplayStudio.AI.Scene;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using static RoleplayStudio.Tests.Support.Scenes;

namespace RoleplayStudio.Tests.AI;

public class SceneTrackingTests
{
    [Fact]
    public void A_reply_that_is_not_json_is_no_update()
    {
        Assert.Null(SceneTracking.Parse("The scene is the same as before."));
    }

    [Fact]
    public void Fields_copied_from_the_answer_template_change_nothing_and_make_no_one()
    {
        var (session, cast) = MiraAlone();

        var outcome = Parsed("""{"location":"...","mood":"…","changes":[{"name":"Mira","outfit":"..."}],"newcomers":[{"name":"...","role":"..."}]}""")
            .ApplyTo(session, cast, "Sam", Now);

        Assert.Empty(outcome.Newcomers);
        Assert.Equal("The bar", session.Scene.Location);
        Assert.Null(session.Scene.Mood);
        Assert.Equal("An apron and clogs", session.CharacterStates[0].CurrentOutfit);
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

        var story = SceneTracking.Prompt(session, new Persona { Name = "Sam" }, cast, [new Message { Sequence = 1, SpeakerName = "Narrator", Content = "The door opens." }])[1].Text;

        Assert.Contains("- Mira (in the scene). Wearing: An apron and clogs.", story);
        Assert.Contains("- Thorne Ashby (not met yet).", story);
        Assert.Contains("Narrator: The door opens.", story);
    }

    [Fact]
    public void The_prompt_shows_what_is_known_of_an_introduced_character_so_the_model_can_build_on_it()
    {
        var (session, cast, _) = WithStablehand();

        var story = SceneTracking.Prompt(session, new Persona { Name = "Sam" }, cast, [new Message { Sequence = 1, SpeakerName = "Narrator", Content = "He grins." }])[1].Text;

        Assert.Contains("Introduced in this story. Age: 20. Gender: unknown. Looks: Lanky.", story);
    }

    [Fact]
    public void The_persona_wears_their_default_outfit_until_the_story_changes_it()
    {
        var (session, cast) = MiraAlone();
        var sam = new Persona { Name = "Sam", DefaultOutfit = "A grey hoodie" };
        var said = new Message { Sequence = 1, SpeakerName = "Sam", Content = "I change into my swimsuit." };

        var before = SceneTracking.Prompt(session, sam, cast, [said])[1].Text;
        Parsed("""{"personaOutfit":"A red swimsuit"}""").ApplyTo(session, cast, sam.Name, Now);
        var after = SceneTracking.Prompt(session, sam, cast, [said])[1].Text;

        Assert.Contains("Sam is wearing: A grey hoodie", before);
        Assert.Equal("A red swimsuit", session.Scene.PersonaWears(sam));
        Assert.Contains("Sam is wearing: A red swimsuit", after);
    }

    [Fact]
    public void The_answer_template_shows_a_change_with_only_what_changed()
    {
        var (session, cast) = MiraAlone();

        var instructions = SceneTracking.Prompt(session, new Persona { Name = "Sam" }, cast, [])[0].Text;

        Assert.Contains("""changes":[{"name":"Name","outfit":"..."}]""", instructions);
        Assert.DoesNotContain("newName\":", instructions);
        Assert.DoesNotContain("personaOutfit\":", instructions);
    }
}
