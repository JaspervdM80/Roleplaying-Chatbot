using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Tests.AI;

public class SpeakerTagParserTests
{
    private static readonly Character Mira = new() { Name = "Mira Holt" };
    private static readonly Character Jun = new() { Name = "Jun" };

    private static readonly string GroupReply = """
        *The espresso machine hisses.*

        **Mira Holt:** "Morning, Sam."
        *She slides a cup across.*

        **Jun:** "You're late again."
        He grins.

        **Mira:** "Leave him be."
        """.ReplaceLineEndings("\n");

    private static (Guid?, string) Line(Character? speaker, string text) => (speaker?.Id, text);

    private static List<(Guid?, string)> Lines(IEnumerable<ReplySegment> segments) => segments.Select(s => (s.CharacterId, s.Text)).ToList();

    [Fact]
    public void A_group_reply_splits_into_tagged_speakers_with_italic_lines_as_narration()
    {
        var segments = SpeakerTagParser.Parse(GroupReply, [Mira, Jun]);

        Assert.Equal(
            [
                Line(null, "*The espresso machine hisses.*"),
                Line(Mira, "\"Morning, Sam.\""),
                Line(null, "*She slides a cup across.*"),
                Line(Jun, "\"You're late again.\"\nHe grins."),
                Line(Mira, "\"Leave him be.\""),
            ],
            Lines(segments));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void A_tag_split_across_stream_chunks_is_read_as_if_it_came_whole(int chunkLength)
    {
        var parser = new SpeakerTagParser([Mira, Jun]);
        foreach (var chunk in GroupReply.Chunk(chunkLength))
        {
            parser.Append(new string(chunk));
        }

        Assert.Equal(Lines(SpeakerTagParser.Parse(GroupReply, [Mira, Jun])), Lines(parser.Complete()));
    }

    [Fact]
    public void The_start_of_a_line_waits_only_while_it_could_still_be_a_tag()
    {
        var parser = new SpeakerTagParser([Mira, Jun]);

        parser.Append("**Ju");
        Assert.Empty(parser.Segments);

        parser.Append("n:** \"Hey");
        Assert.Equal([Line(Jun, "\"Hey")], Lines(parser.Segments));

        parser.Append("\"\nShe laughs");
        Assert.Equal([Line(Jun, "\"Hey\"\nShe laughs")], Lines(parser.Segments));
    }

    [Fact]
    public void A_tag_naming_someone_not_present_stays_in_the_text_as_narration()
    {
        var segments = SpeakerTagParser.Parse("**Jun:** \"Hi.\"\n**Bartender:** \"What'll it be?\"", [Mira, Jun]);

        Assert.Equal([Line(Jun, "\"Hi.\""), Line(null, "**Bartender:** \"What'll it be?\"")], Lines(segments));
    }

    [Fact]
    public void With_one_character_present_untagged_text_and_their_actions_are_theirs()
    {
        var segments = SpeakerTagParser.Parse("*She smiles.*\n\"Coming up.\"", [Jun]);

        Assert.Equal([Line(Jun, "*She smiles.*\n\"Coming up.\"")], Lines(segments));
    }

    [Theory]
    [InlineData("**Never** again, she thinks.")]
    [InlineData("**Bold claim: coffee is life.")]
    public void Bold_text_at_the_start_of_a_line_is_not_taken_for_a_tag(string reply)
    {
        var segments = SpeakerTagParser.Parse(reply, [Mira, Jun]);

        Assert.Equal([Line(null, reply)], Lines(segments));
    }

    [Fact]
    public void A_narrator_tag_and_the_other_bold_form_are_both_tags()
    {
        var segments = SpeakerTagParser.Parse("**Narrator:** Rain starts.\n**Jun**: \"Great.\"", [Mira, Jun]);

        Assert.Equal([Line(null, "Rain starts."), Line(Jun, "\"Great.\"")], Lines(segments));
    }
}
