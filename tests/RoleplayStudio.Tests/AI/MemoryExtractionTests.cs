using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.Tests.AI;

public class MemoryExtractionTests
{
    private static readonly Character Mira = new() { Name = "Mira" };

    [Fact]
    public void A_reply_wrapped_in_a_code_fence_and_chatter_is_still_read()
    {
        var reply = """
            Sure! Here are the memories:
            ```json
            {"memories":[{"type":"preference","text":"Sam takes their coffee black.","importance":6,"characters":["mira"]}]}
            ```
            """;

        var memory = Assert.Single(MemoryExtraction.Parse(reply, [Mira])!);

        Assert.Equal((MemoryType.Preference, "Sam takes their coffee black.", 6), (memory.Type, memory.Text, memory.Importance));
        Assert.Equal([Mira.Id], memory.CharacterIds);
    }

    [Fact]
    public void Someone_not_in_the_chat_an_unknown_type_and_an_extreme_importance_are_tamed()
    {
        var reply = """{"memories":[{"type":"Gossip","text":"Mira owes Jun money.","importance":42,"characters":["Mira","Jun"]},{"text":"  "},{"type":"Fact"}]}""";

        var memory = Assert.Single(MemoryExtraction.Parse(reply, [Mira])!);

        Assert.Equal(MemoryType.Fact, memory.Type);
        Assert.Equal(MemoryEntry.MaxImportance, memory.Importance);
        Assert.Equal([Mira.Id], memory.CharacterIds);
    }

    [Fact]
    public void An_empty_list_means_nothing_to_remember_rather_than_a_bad_reply()
    {
        Assert.Empty(MemoryExtraction.Parse("""{"memories":[]}""", [Mira])!);
    }

    [Theory]
    [InlineData("Nothing much happened.")]
    [InlineData("""{"memory":"Sam likes tea."}""")]
    [InlineData("""{"memories":[{"text":"Sam likes tea."}""")]
    public void A_reply_that_is_not_the_json_asked_for_reads_as_null(string reply)
    {
        Assert.Null(MemoryExtraction.Parse(reply, [Mira]));
    }
}
