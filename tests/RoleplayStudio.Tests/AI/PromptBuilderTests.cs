using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.Tests.AI;

public class PromptBuilderTests
{
    private static readonly PromptBudget Roomy = new(32_000, 1_000);

    private static PresentCharacter Present(string name, string? outfit = null) =>
        new(new Character { Name = name }, new CharacterState { CurrentOutfit = outfit });

    private static PromptInput Input(
        IReadOnlyList<PresentCharacter> present,
        IReadOnlyList<Message>? messages = null,
        SessionSummary? summary = null,
        IReadOnlyList<MemoryEntry>? memories = null,
        IReadOnlyList<PresentCharacter>? metEarlier = null) => new(
        new Chatbot { Name = "Seaside Café", WorldDescription = "A quiet harbour town." },
        new Scenario { Title = "Morning rush" },
        new Persona { Name = "Sam" },
        new SceneState { Location = "Behind the counter" },
        present,
        metEarlier ?? [],
        summary ?? new SessionSummary(),
        memories ?? [],
        messages ?? []);

    private static Message Said(long sequence, string content, MessageRole role = MessageRole.Character) =>
        new() { Sequence = sequence, Role = role, Content = content };

    [Fact]
    public void The_system_prompt_describes_the_world_and_what_each_present_character_wears_now()
    {
        var system = PromptBuilder.Build(Input([Present("Mira", outfit: "A yellow raincoat")]), Roomy)[0];

        Assert.Equal(ChatRole.System, system.Role);
        Assert.Contains("A quiet harbour town.", system.Text);
        Assert.Contains("Wearing now: A yellow raincoat", system.Text);
        Assert.Contains("Behind the counter", system.Text);
    }

    [Fact]
    public void Someone_met_earlier_is_named_for_the_story_but_not_voiced()
    {
        var system = PromptBuilder.Build(Input([Present("Mira")], metEarlier: [Present("Thorne")]), Roomy)[0].Text;

        Assert.Contains("- Thorne", system);
        Assert.Contains("Write as Mira", system);
    }

    [Fact]
    public void A_group_scene_asks_for_speaker_tags_naming_everyone_present()
    {
        var system = PromptBuilder.Build(Input([Present("Mira"), Present("Jun")]), Roomy)[0].Text;

        Assert.Contains("**Mira:**", system);
        Assert.Contains("Mira, Jun", system);
    }

    [Fact]
    public void Messages_follow_in_sequence_with_the_user_as_user_and_everyone_else_as_assistant()
    {
        var messages = PromptBuilder.Build(Input([Present("Mira")], [Said(2, "A latte, please.", MessageRole.User), Said(1, "Morning!")]), Roomy);

        Assert.Equal([ChatRole.System, ChatRole.Assistant, ChatRole.User], messages.Select(m => m.Role));
        Assert.Equal("Morning!", messages[1].Text);
    }

    [Fact]
    public void Messages_the_summary_covers_give_way_to_the_summary()
    {
        var messages = PromptBuilder.Build(
            Input([Present("Mira")], [Said(1, "Morning!"), Said(2, "A latte, please.", MessageRole.User), Said(3, "Coming up.")], new SessionSummary { Text = "Sam ordered a latte.", CoveredUpToSequence = 2 }),
            Roomy);

        Assert.Contains("Sam ordered a latte.", messages[0].Text);
        Assert.Equal(["Coming up."], messages.Skip(1).Select(m => m.Text));
    }

    [Fact]
    public void Over_budget_the_oldest_messages_go_first_but_the_latest_is_always_sent()
    {
        var budget = new PromptBudget(1_000, 200);
        var system = PromptBuilder.Build(Input([Present("Mira")]), budget)[0].Text;
        var room = budget.PromptTokens - (system.Length + 3) / 4;
        var older = new string('a', room * 4);
        var latest = new string('b', room * 8);

        var fits = PromptBuilder.Build(Input([Present("Mira")], [Said(1, older), Said(2, "short")]), budget);
        var overflows = PromptBuilder.Build(Input([Present("Mira")], [Said(1, "short"), Said(2, latest)]), budget);

        Assert.Equal(["short"], fits.Skip(1).Select(m => m.Text));
        Assert.Equal([latest], overflows.Skip(1).Select(m => m.Text));
    }

    [Fact]
    public void Memories_are_sent_best_first_until_their_share_of_the_budget_runs_out()
    {
        var budget = new PromptBudget(2_000, 0);
        var lines = Enumerable.Range(1, 40).Select(i => new MemoryEntry { Text = $"Memory number {i} is about the harbour and the café." }).ToList();

        var system = PromptBuilder.Build(Input([Present("Mira")], memories: lines), budget)[0].Text;

        Assert.Contains("Memory number 1 ", system);
        Assert.DoesNotContain("Memory number 40 ", system);
    }

    [Fact]
    public void A_summary_over_its_budget_keeps_its_latest_sentences_whole()
    {
        var budget = new PromptBudget(1_000, 0);
        var early = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"Early event {i} happened."));
        var summary = new SessionSummary { Text = $"{early} Sam finally kissed Mira.", CoveredUpToSequence = 0 };

        var system = PromptBuilder.Build(Input([Present("Mira")], summary: summary), budget)[0].Text;

        var section = system.ReplaceLineEndings("\n").Split("## The story so far")[1].Split("\n\n")[0].Trim();
        Assert.EndsWith("Sam finally kissed Mira.", section);
        Assert.StartsWith("Early event ", section);
        Assert.DoesNotContain("Early event 1 happened.", section);
    }
}
