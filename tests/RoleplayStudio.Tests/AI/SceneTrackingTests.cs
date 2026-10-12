using RoleplayStudio.AI.Scene;
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

        var story = SceneTracking.Prompt(session, "Sam", cast, [new Message { Sequence = 1, SpeakerName = "Narrator", Content = "The door opens." }])[1].Text;

        Assert.Contains("- Mira (in the scene). Wearing: An apron and clogs.", story);
        Assert.Contains("- Thorne Ashby (not met yet).", story);
        Assert.Contains("Narrator: The door opens.", story);
    }

    [Fact]
    public void The_prompt_shows_what_is_known_of_an_introduced_character_so_the_model_can_build_on_it()
    {
        var (session, cast, _) = WithStablehand();

        var story = SceneTracking.Prompt(session, "Sam", cast, [new Message { Sequence = 1, SpeakerName = "Narrator", Content = "He grins." }])[1].Text;

        Assert.Contains("Introduced in this story. Age: 20. Gender: unknown. Looks: Lanky.", story);
    }
}
