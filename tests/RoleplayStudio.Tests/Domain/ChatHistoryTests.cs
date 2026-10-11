using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;

namespace RoleplayStudio.Tests.Domain;

public class ChatHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly Character Mira = new() { Name = "Mira", DefaultOutfit = "An apron" };
    private static readonly Character Jun = new() { Name = "Jun", DefaultOutfit = "A raincoat" };

    private static ChatSession Session(int messageCount)
    {
        var session = new ChatSession { Title = "Morning rush", Scene = new SceneState { Location = "The counter" } };
        session.SetPresent([Mira], Now);
        session.Messages = Enumerable.Range(1, messageCount)
            .Select(i => new Message { Sequence = i, Role = i % 2 == 0 ? MessageRole.User : MessageRole.Character, Content = $"Line {i}" })
            .ToList();
        return session;
    }

    private static MemoryEntry Memory(string text, long? from, long? to) => new() { Text = text, SourceFromSequence = from, SourceToSequence = to };

    [Fact]
    public void Rewinding_drops_every_memory_of_a_pass_that_read_a_removed_message_and_reads_that_pass_again()
    {
        var session = Session(9);
        session.MemoriesExtractedUpToSequence = 9;
        var early = Memory("Sam takes their coffee black.", 1, 5);
        var late = Memory("Sam broke a cup.", 6, 9);
        var written = Memory("Sam hates cinnamon.", null, null);

        var rewound = session.Rewind(8, session.Messages, [early, late, written]);

        Assert.Equal([late], rewound.DroppedMemories);
        Assert.Equal(5, session.MemoriesExtractedUpToSequence);
    }

    [Fact]
    public void A_memory_the_user_pinned_survives_a_rewind_of_the_messages_it_came_from()
    {
        var session = Session(9);
        session.MemoriesExtractedUpToSequence = 9;
        var pinned = Memory("Sam is allergic to cinnamon.", 6, 9);
        pinned.IsPinned = true;
        var mined = Memory("Sam broke a cup.", 6, 9);

        var rewound = session.Rewind(8, session.Messages, [pinned, mined]);

        Assert.Equal([mined], rewound.DroppedMemories);
        Assert.Equal(5, session.MemoriesExtractedUpToSequence);
    }

    [Fact]
    public void A_branch_takes_the_scene_from_the_snapshot_on_the_message_after_its_point()
    {
        var session = Session(5);
        session.Messages[3].SceneBefore = session.Snapshot();
        session.Scene.Location = "The harbour";
        session.Scene.TrackedUpToSequence = 5;

        var (branch, _) = session.Branch(3, session.Messages, [], Now);

        Assert.Equal(("The counter", 3L), (branch.Scene.Location, branch.Scene.TrackedUpToSequence));
        Assert.Equal(3, branch.Messages.Count);
        Assert.Equal("The harbour", session.Scene.Location);
    }

    [Fact]
    public void Rewinding_clears_a_summary_that_covers_a_removed_message_and_keeps_one_that_does_not()
    {
        var covering = Session(9);
        covering.Summary = new SessionSummary { Text = "Sam came in.", CoveredUpToSequence = 6 };
        var older = Session(9);
        older.Summary = new SessionSummary { Text = "Sam came in.", CoveredUpToSequence = 4 };

        covering.Rewind(6, covering.Messages, []);
        older.Rewind(6, older.Messages, []);

        Assert.Equal(("", 0L), (covering.Summary.Text, covering.Summary.CoveredUpToSequence));
        Assert.Equal(("Sam came in.", 4L), (older.Summary.Text, older.Summary.CoveredUpToSequence));
    }

    [Fact]
    public void Rewinding_puts_back_the_scene_from_the_last_snapshot_before_the_removed_messages()
    {
        var session = Session(5);
        session.Messages[3].SceneBefore = session.Snapshot();
        session.Scene.Location = "The harbour";
        session.SetPresent([Mira, Jun], Now);
        session.CharacterStates[0].CurrentOutfit = "A wet apron";
        session.Scene.TrackedUpToSequence = 5;

        var rewound = session.Rewind(5, session.Messages, []);

        Assert.Equal(("The counter", 3L), (session.Scene.Location, session.Scene.TrackedUpToSequence));
        Assert.Equal([Mira.Id], session.Scene.PresentCharacterIds);
        Assert.Equal("An apron", Assert.Single(session.CharacterStates).CurrentOutfit);
        Assert.Equal(Jun.Id, Assert.Single(rewound.DroppedStates).CharacterId);
    }

    [Fact]
    public void Rewinding_past_what_scene_tracking_read_leaves_the_scene_alone()
    {
        var session = Session(5);
        session.Messages[0].SceneBefore = new SceneSnapshot { Location = "Somewhere else" };
        session.Scene.TrackedUpToSequence = 3;

        session.Rewind(4, session.Messages, []);

        Assert.Equal(("The counter", 3L), (session.Scene.Location, session.Scene.TrackedUpToSequence));
    }

    [Fact]
    public void Rewinding_moves_the_history_on_so_upkeep_that_read_the_chat_before_cannot_save_over_it()
    {
        var session = Session(3);

        session.Rewind(3, session.Messages, []);

        Assert.Equal(1, session.HistoryVersion);
    }

    [Fact]
    public void A_branch_holds_the_messages_up_to_its_point_and_only_the_memories_mined_before_it()
    {
        var session = Session(9);
        session.MemoriesExtractedUpToSequence = 9;
        var early = Memory("Sam takes their coffee black.", 1, 5);

        var (branch, memories) = session.Branch(6, session.Messages, [early, Memory("Sam broke a cup.", 6, 9)], Now);

        Assert.NotEqual(session.Id, branch.Id);
        Assert.Equal([1L, 2, 3, 4, 5, 6], branch.Messages.Select(m => m.Sequence));
        Assert.All(branch.Messages, m => Assert.Equal(branch.Id, m.SessionId));
        Assert.Equal([early.Text], memories.Select(m => m.Text));
        Assert.Equal(branch.Id, memories[0].SessionId);
        Assert.Equal(5, branch.MemoriesExtractedUpToSequence);
        Assert.Equal(9, session.MemoriesExtractedUpToSequence);
        Assert.Equal([Mira.Id], branch.CharacterStates.Select(s => s.CharacterId));
    }
}
