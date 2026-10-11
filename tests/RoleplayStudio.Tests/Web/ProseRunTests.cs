using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Tests.Web;

public class ProseRunTests
{
    [Fact]
    public void Asterisks_mark_actions_in_italics_and_double_asterisks_in_bold()
    {
        var runs = ProseRun.Split("*She smiles.* \"**Never** again.\"");

        Assert.Equal(
            [new("She smiles.", ProseStyle.Emphasis), new(" \"", ProseStyle.Plain), new("Never", ProseStyle.Strong), new(" again.\"", ProseStyle.Plain)],
            runs);
    }

    [Theory]
    [InlineData("Five * three is fifteen.")]
    [InlineData("*She turns away\nand leaves.*")]
    [InlineData("A stray *asterisk")]
    public void An_asterisk_that_does_not_close_on_the_same_line_stays_as_written(string text)
    {
        Assert.Equal([new ProseRun(text, ProseStyle.Plain)], ProseRun.Split(text));
    }
}
