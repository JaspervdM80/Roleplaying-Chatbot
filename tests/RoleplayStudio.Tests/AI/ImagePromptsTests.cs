using RoleplayStudio.AI.Images;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.AI;

public class ImagePromptsTests
{
    private static readonly Character Mira = new() { Name = "Mira", Age = 31, Gender = "Woman", Appearance = "Red curls, freckles", DefaultOutfit = "An apron", ImageTags = "soft light" };

    private static string Story(PictureBrief brief) => ImagePrompts.Prompt(brief, tags: true)[1].Text;

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
    [InlineData("""{"prompt":"...","caption":"..."}""")]
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
        var messages = ImagePrompts.Prompt(new PictureBrief([PicturedPerson.Of(Mira, null, 1)], null, IsPortrait: false), tags: true);

        Assert.Contains("- Mira: 31-year-old Woman. Shown in reference image 1. Looks: Red curls, freckles.", messages[1].Text);
        Assert.Contains("the person from reference image N", messages[0].Text);
    }

    [Fact]
    public void Without_reference_images_the_model_is_not_told_about_them()
    {
        Assert.DoesNotContain("reference image", ImagePrompts.Prompt(new PictureBrief([PicturedPerson.Of(Mira, null)], null, IsPortrait: false), tags: true)[0].Text);
    }

    private static readonly Character Jun = new() { Name = "Jun", Appearance = "Black buzz cut", ImageTags = "scar over left eye" };

    [Fact]
    public void A_model_of_the_stable_diffusion_family_gets_tags_and_any_other_plain_sentences()
    {
        var brief = new PictureBrief([PicturedPerson.Of(Mira, null)], "Mira", IsPortrait: true);

        Assert.Contains("comma-separated tags", ImagePrompts.Prompt(brief, ImageModelTraits.Unknown.WantsTags)[0].Text);
        Assert.Contains("plain sentences", ImagePrompts.Prompt(brief, (ImageModelTraits.Unknown with { TakesNegativePrompt = false }).WantsTags)[0].Text);
    }

    [Fact]
    public void A_group_picture_gets_more_words_so_no_one_loses_their_looks()
    {
        var one = new PictureBrief([PicturedPerson.Of(Mira, null)], null, IsPortrait: false);
        var three = new PictureBrief([PicturedPerson.Of(Mira, null), PicturedPerson.Of(Jun, null), new PicturedPerson("Sam", null, null, null, null, null, null)], null, IsPortrait: false);

        Assert.True(ImagePrompts.WordsFor(three) > ImagePrompts.WordsFor(one));
        Assert.Contains($"under {ImagePrompts.WordsFor(three)} words", ImagePrompts.Prompt(three, tags: true)[0].Text);
    }

    [Fact]
    public void One_persons_image_tags_are_added_to_the_prompt_and_a_groups_go_with_whose_they_are()
    {
        var written = new WrittenPrompt("two people at a bar", null);
        var alone = new PictureBrief([PicturedPerson.Of(Mira, null)], "Mira", IsPortrait: false);
        var group = new PictureBrief([PicturedPerson.Of(Mira, null), PicturedPerson.Of(Jun, null)], null, IsPortrait: false);

        Assert.EndsWith("soft light", ImagePrompts.Compose(alone, written));
        Assert.Equal("two people at a bar, soft light, scar over left eye", ImagePrompts.Compose(group, written));
        Assert.Equal("a man, scar over left eye, and a woman at a bar, soft light", ImagePrompts.Compose(group, written with { Prompt = "a man, scar over left eye, and a woman at a bar" }));
        Assert.Contains("- Jun: Looks: Black buzz cut. Image tags: scar over left eye.", ImagePrompts.Prompt(group, tags: true)[1].Text);
        Assert.DoesNotContain("Image tags", ImagePrompts.Prompt(alone, tags: true)[1].Text);
    }

    [Fact]
    public void Only_a_model_that_takes_a_negative_prompt_has_one()
    {
        Assert.NotNull(ImageModelTraits.Unknown.NegativePrompt);
        Assert.Null((ImageModelTraits.Unknown with { TakesNegativePrompt = false }).NegativePrompt);
    }
}
