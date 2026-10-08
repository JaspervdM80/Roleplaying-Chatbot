using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.Tests.Domain;

public class MemoryEntryTests
{
    private static readonly Guid Mira = Guid.NewGuid();
    private static readonly Guid Jun = Guid.NewGuid();

    [Fact]
    public void Copying_an_edit_trims_the_text_keeps_importance_in_range_and_drops_repeated_characters()
    {
        var memory = new MemoryEntry();

        memory.CopyEditableFieldsFrom(new MemoryEntry { Type = MemoryType.Fact, Text = "  Mira hates rain.  ", Importance = 42, RelatedCharacterIds = [Mira, Mira], IsPinned = true });

        Assert.Equal((MemoryType.Fact, "Mira hates rain.", MemoryEntry.MaxImportance, true), (memory.Type, memory.Text, memory.Importance, memory.IsPinned));
        Assert.Equal([Mira], memory.RelatedCharacterIds);
    }

    [Fact]
    public void A_memory_needs_text_and_may_only_be_about_characters_met_in_the_chat()
    {
        Assert.Equal(nameof(MemoryEntry.Text), new MemoryEntry { Text = " " }.FindProblem([Mira])?.Field);
        Assert.Equal(nameof(MemoryEntry.RelatedCharacterIds), new MemoryEntry { Text = "Jun left.", RelatedCharacterIds = [Jun] }.FindProblem([Mira])?.Field);
        Assert.Null(new MemoryEntry { Text = "Mira stayed.", RelatedCharacterIds = [Mira] }.FindProblem([Mira]));
    }

    [Fact]
    public void Filters_combine_and_search_ignores_case()
    {
        var memory = new MemoryEntry { Type = MemoryType.Preference, Text = "Sam takes their Coffee black.", RelatedCharacterIds = [Mira] };

        Assert.True(memory.Matches(null, null, null));
        Assert.True(memory.Matches(" coffee ", MemoryType.Preference, Mira));
        Assert.False(memory.Matches("tea", null, null));
        Assert.False(memory.Matches("coffee", MemoryType.Event, null));
        Assert.False(memory.Matches("coffee", null, Jun));
    }
}
