using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.Tests.AI;

public class PromptBuilderTests
{
    private static readonly PromptBudget Roomy = new(32_000, 1_000);

    private static PresentCharacter Present(string name, string? outfit = null, string? status = null, string? backstory = null) =>
        new(new Character { Name = name, Backstory = backstory }, new CharacterState { CurrentOutfit = outfit, Status = status });

    private static PromptInput Input(
        IReadOnlyList<PresentCharacter> present,
        IReadOnlyList<Message>? messages = null,
        SessionSummary? summary = null,
        IReadOnlyList<MemoryEntry>? memories = null,
        IReadOnlyList<PresentCharacter>? metEarlier = null,
        SceneState? scene = null) => new(
        new Chatbot { Name = "Seaside Café", WorldDescription = "A quiet harbour town." },
        new Scenario { Title = "Morning rush" },
        new Persona { Name = "Sam", DefaultOutfit = "A grey hoodie" },
        scene ?? new SceneState { Location = "Behind the counter" },
        present,
        metEarlier ?? [],
        summary ?? new SessionSummary(),
        memories ?? [],
        messages ?? []);

    private static Message Said(long sequence, string content, MessageRole role = MessageRole.Character) =>
        new() { Sequence = sequence, Role = role, Content = content };

    private static string Section(string system, string heading) =>
        system.ReplaceLineEndings("\n").Split($"## {heading}\n")[1].Split("\n\n")[0].Trim();

    [Fact]
    public void The_system_prompt_describes_the_world_and_what_each_present_character_wears_and_feels_now()
    {
        var system = PromptBuilder.Build(Input([Present("Mira", outfit: "A yellow raincoat", status: "Wary")]), Roomy)[0];

        Assert.Equal(ChatRole.System, system.Role);
        Assert.Contains("A quiet harbour town.", system.Text);
        Assert.Contains("Wearing now: A yellow raincoat", system.Text);
        Assert.Contains("Feeling: Wary", Section(system.Text, "Mira"));
    }

    [Fact]
    public void Where_and_when_the_scene_is_now_has_a_section_of_its_own()
    {
        var system = PromptBuilder.Build(Input([Present("Mira")], scene: new SceneState { Location = "Behind the counter", TimeOfDay = "Dawn", Mood = "Hurried" }), Roomy)[0].Text;

        Assert.Equal("Location: Behind the counter\nTime of day: Dawn\nMood: Hurried", Section(system, "Now"));
        Assert.DoesNotContain("Behind the counter", Section(system, "Scenario"));
    }

    [Fact]
    public void The_persona_wears_what_the_chat_tracked_and_their_default_outfit_until_then()
    {
        var tracked = PromptBuilder.Build(Input([Present("Mira")], scene: new SceneState { PersonaOutfit = "A wet swimsuit" }), Roomy)[0].Text;
        var untracked = PromptBuilder.Build(Input([Present("Mira")]), Roomy)[0].Text;

        Assert.Contains("Wearing now: A wet swimsuit", tracked);
        Assert.DoesNotContain("hoodie", tracked);
        Assert.Contains("Wearing now: A grey hoodie", untracked);
    }

    [Theory]
    [InlineData(0, "You are the narrator of a roleplay set in Seaside Café.")]
    [InlineData(1, "You play Mira in a roleplay set in Seaside Café.")]
    [InlineData(2, "You direct a roleplay set in Seaside Café.")]
    public void The_prompt_opens_with_the_role_for_who_is_present(int present, string opening)
    {
        var system = PromptBuilder.Build(Input([.. new[] { Present("Mira"), Present("Jun") }.Take(present)]), Roomy)[0].Text;

        Assert.StartsWith(opening, system);
    }

    [Fact]
    public void How_to_write_the_reply_comes_after_the_history_on_the_users_turn()
    {
        var messages = PromptBuilder.Build(Input([Present("Mira")], [Said(1, "Morning!"), Said(2, "A latte, please.", MessageRole.User)]), Roomy);

        Assert.Equal([ChatRole.System, ChatRole.Assistant, ChatRole.User], messages.Select(m => m.Role));
        Assert.StartsWith("A latte, please.\n\n(Out of character: Write as Mira", messages[^1].Text);
        Assert.Contains("stop where Sam can respond", messages[^1].Text);
        Assert.DoesNotContain("Write as Mira", messages[0].Text);
    }

    [Fact]
    public void Someone_met_earlier_is_named_for_the_story_but_not_voiced()
    {
        var messages = PromptBuilder.Build(Input([Present("Mira")], metEarlier: [Present("Thorne")]), Roomy);

        Assert.Contains("- Thorne", messages[0].Text);
        Assert.Contains("Write as Mira", messages[^1].Text);
    }

    [Fact]
    public void A_group_scene_asks_for_speaker_tags_naming_everyone_present()
    {
        var instruction = PromptBuilder.Build(Input([Present("Mira"), Present("Jun")]), Roomy)[^1].Text;

        Assert.Contains("**Mira:**", instruction);
        Assert.Contains("Mira, Jun", instruction);
    }

    [Fact]
    public void A_long_backstory_keeps_its_first_whole_sentences_within_the_sheets_share()
    {
        var budget = new PromptBudget(2_000, 0);
        var backstory = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"Mira did thing {i} long ago."));

        var sheet = Section(PromptBuilder.Build(Input([Present("Mira", outfit: "A yellow raincoat", backstory: backstory)]), budget)[0].Text, "Mira");

        Assert.Contains("Backstory: Mira did thing 1 long ago.", sheet);
        Assert.DoesNotContain("thing 200 ", sheet);
        Assert.EndsWith("long ago.", sheet.Split('\n').Single(l => l.StartsWith("Backstory")));
        Assert.Contains("Wearing now: A yellow raincoat", sheet);
        Assert.True(sheet.Length / 4 <= budget.SheetTokens / 2);
    }

    [Fact]
    public void A_long_backstory_written_as_a_list_keeps_its_first_items()
    {
        var budget = new PromptBudget(2_000, 0);
        var backstory = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"- grew up in harbour town {i}"));

        var sheet = Section(PromptBuilder.Build(Input([Present("Mira", backstory: backstory)]), budget)[0].Text, "Mira");

        Assert.Contains("Backstory: - grew up in harbour town 1\n- grew up in harbour town 2\n", sheet);
        Assert.DoesNotContain("town 200", sheet);
    }

    [Fact]
    public void A_hint_to_let_someone_answer_names_them_for_this_reply_only()
    {
        var jun = Present("Jun");
        var messages = PromptBuilder.Build(Input([Present("Mira"), jun]) with { Hint = new DirectorHint.LetRespond(jun.Character.Id) }, Roomy);

        Assert.Contains("For this reply only: only Jun speaks", messages[^1].Text);
        Assert.DoesNotContain(PromptBuilder.Build(Input([Present("Mira"), jun]), Roomy), m => m.Text.Contains("For this reply only"));
    }

    [Fact]
    public void A_hint_joins_the_users_last_message_so_turns_still_alternate()
    {
        var messages = PromptBuilder.Build(
            Input([Present("Mira")], [Said(1, "Morning!"), Said(2, "A latte, please.", MessageRole.User)]) with { Hint = new DirectorHint.NarrationOnly() },
            Roomy);

        Assert.Equal([ChatRole.System, ChatRole.Assistant, ChatRole.User], messages.Select(m => m.Role));
        Assert.StartsWith("A latte, please.", messages[^1].Text);
        Assert.Contains("narration only", messages[^1].Text);
        Assert.DoesNotContain("narration only", messages[0].Text);
    }

    [Fact]
    public void A_hint_after_a_reply_comes_as_a_user_turn_of_its_own()
    {
        var messages = PromptBuilder.Build(Input([Present("Mira")], [Said(1, "Morning!")]) with { Hint = new DirectorHint.SkipAhead() }, Roomy);

        Assert.Equal([ChatRole.System, ChatRole.Assistant, ChatRole.User], messages.Select(m => m.Role));
        Assert.Contains("skip ahead in time", messages[^1].Text);
    }

    [Fact]
    public void A_hint_naming_someone_not_in_the_scene_adds_nothing()
    {
        var messages = PromptBuilder.Build(Input([Present("Mira")], [Said(1, "Hello.", MessageRole.User)]) with { Hint = new DirectorHint.LetRespond(Guid.NewGuid()) }, Roomy);

        Assert.DoesNotContain("For this reply only", messages[^1].Text);
    }

    [Fact]
    public void Messages_follow_in_sequence_with_the_user_as_user_and_everyone_else_as_assistant()
    {
        var messages = PromptBuilder.Build(Input([Present("Mira")], [Said(2, "A latte, please.", MessageRole.User), Said(1, "Morning!")]), Roomy);

        Assert.Equal([ChatRole.System, ChatRole.Assistant, ChatRole.User], messages.Select(m => m.Role));
        Assert.Equal("Morning!", messages[1].Text);
        Assert.StartsWith("A latte, please.", messages[2].Text);
    }

    [Fact]
    public void A_reply_saved_as_several_speakers_goes_back_as_one_turn_in_the_tagged_form()
    {
        var messages = PromptBuilder.Build(
            Input(
                [Present("Mira"), Present("Jun")],
                [
                    Said(1, "Two lattes.", MessageRole.User),
                    Said(2, "*The machine hisses.*", MessageRole.Narrator),
                    new Message { Sequence = 3, Role = MessageRole.Character, SpeakerName = "Mira", Content = "\"Coming up.\"" },
                    new Message { Sequence = 4, Role = MessageRole.Character, SpeakerName = "Jun", Content = "\"Make mine a double.\"" },
                ]),
            Roomy);

        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], messages.Select(m => m.Role));
        Assert.Equal("*The machine hisses.*\n\n**Mira:** \"Coming up.\"\n\n**Jun:** \"Make mine a double.\"", messages[2].Text);
    }

    [Fact]
    public void In_a_group_a_reply_from_one_speaker_keeps_its_tag_so_the_model_knows_who_said_it()
    {
        var messages = PromptBuilder.Build(
            Input([Present("Mira"), Present("Jun")], [new Message { Sequence = 1, Role = MessageRole.Character, SpeakerName = "Mira", Content = "\"Morning!\"" }]),
            Roomy);

        Assert.Equal("**Mira:** \"Morning!\"", messages[1].Text);
    }

    [Fact]
    public void Messages_the_summary_covers_give_way_to_the_summary()
    {
        var messages = PromptBuilder.Build(
            Input([Present("Mira")], [Said(1, "Morning!"), Said(2, "A latte, please.", MessageRole.User), Said(3, "Coming up.")], new SessionSummary { Text = "Sam ordered a latte.", CoveredUpToSequence = 2 }),
            Roomy);

        Assert.Contains("Sam ordered a latte.", messages[0].Text);
        Assert.Equal("Coming up.", Assert.Single(messages, m => m.Role == ChatRole.Assistant).Text);
        Assert.DoesNotContain(messages, m => m.Text.Contains("Morning!") || m.Text.Contains("A latte, please."));
    }

    [Fact]
    public void Over_budget_the_oldest_messages_go_first_but_the_latest_is_always_sent()
    {
        var budget = new PromptBudget(1_000, 200);
        var empty = PromptBuilder.Build(Input([Present("Mira")]), budget);
        var room = budget.PromptTokens - empty.Sum(m => (m.Text.Length + 3) / 4);
        var older = new string('a', room * 4);
        var latest = new string('b', room * 8);

        var fits = PromptBuilder.Build(Input([Present("Mira")], [Said(1, older, MessageRole.User), Said(2, "short")]), budget);
        var overflows = PromptBuilder.Build(Input([Present("Mira")], [Said(1, "short", MessageRole.User), Said(2, latest)]), budget);

        Assert.Equal(["short"], fits.Where(m => m.Role == ChatRole.Assistant).Select(m => m.Text));
        Assert.DoesNotContain(fits, m => m.Text.Contains(older));
        Assert.Equal([latest], overflows.Where(m => m.Role == ChatRole.Assistant).Select(m => m.Text));
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

        var section = Section(system, "The story so far");
        Assert.EndsWith("Sam finally kissed Mira.", section);
        Assert.StartsWith("Early event ", section);
        Assert.DoesNotContain("Early event 1 happened.", section);
    }
}
