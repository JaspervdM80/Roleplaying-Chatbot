using RoleplayStudio.AI.Images;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.AI;

public class ImagePromptsTests
{
    private static readonly Character Mira = new() { Name = "Mira", Age = 31, Gender = "Woman", Appearance = "Red curls, freckles", DefaultOutfit = "An apron", ImageTags = "soft light" };

    private static string Story(PictureBrief brief) => ImagePrompts.Prompt(brief)[1].Text;

    [Fact]
    public void A_person_wears_what_the_chat_says_they_wear_now_not_their_default_outfit()
    {
        var state = new CharacterState { CharacterId = Mira.Id, CurrentOutfit = "A green wool dress" };

        var story = Story(new PictureBrief([PicturedPerson.Of(Mira, state)], "Mira", IsPortrait: false));

        Assert.Contains("Wearing: A green wool dress", story);
        Assert.DoesNotContain("apron", story);
        Assert.Contains("Looks: Red curls, freckles", story);
    }

    [Fact]
    public void A_portrait_wears_the_default_outfit()
    {
        var story = Story(new PictureBrief([PicturedPerson.Of(Mira, null)], "Mira", IsPortrait: true));

        Assert.Contains("Wearing: An apron", story);
    }

    [Fact]
    public void The_written_prompt_is_read_from_fenced_json()
    {
        var written = ImagePrompts.Parse("""
            Here you go:
            ```json
            {"prompt":"a woman by a hearth","caption":"Mira by the fire"}
            ```
            """);

        Assert.Equal(new WrittenPrompt("a woman by a hearth", "Mira by the fire"), written);
    }

    [Theory]
    [InlineData("A woman by a hearth.")]
    [InlineData("""{"caption":"Mira"}""")]
    [InlineData("""{"prompt":"   "}""")]
    public void A_reply_without_a_prompt_is_no_prompt(string reply)
    {
        Assert.Null(ImagePrompts.Parse(reply));
    }

    [Fact]
    public void Portraits_and_pictures_of_one_person_stand_and_scenes_lie()
    {
        Assert.Equal((768, 1024), ImagePrompts.SizeFor(new PictureBrief([], "Mira", IsPortrait: true)));
        Assert.Equal((768, 1024), ImagePrompts.SizeFor(new PictureBrief([], "Mira", IsPortrait: false)));
        Assert.Equal((1344, 768), ImagePrompts.SizeFor(new PictureBrief([], null, IsPortrait: false)));
    }

    [Fact]
    public void A_person_without_an_age_is_described_without_one()
    {
        var story = Story(new PictureBrief([new PicturedPerson("Sam", null, "Man", "Tall", null, "A coat", null)], null, IsPortrait: false));

        Assert.Contains("- Sam: Man. Looks: Tall. Wearing: A coat.", story);
        Assert.DoesNotContain("year-old", story);
    }

    [Fact]
    public void A_person_shown_in_a_reference_image_is_numbered_and_the_model_told_how_to_name_them()
    {
        var messages = ImagePrompts.Prompt(new PictureBrief([PicturedPerson.Of(Mira, null, 1)], null, IsPortrait: false));

        Assert.Contains("- Mira: 31-year-old Woman. Shown in reference image 1. Looks: Red curls, freckles.", messages[1].Text);
        Assert.Contains("the person from reference image N", messages[0].Text);
    }

    [Fact]
    public void Without_reference_images_the_model_is_not_told_about_them()
    {
        Assert.DoesNotContain("reference image", ImagePrompts.Prompt(new PictureBrief([PicturedPerson.Of(Mira, null)], null, IsPortrait: false))[0].Text);
    }
}
