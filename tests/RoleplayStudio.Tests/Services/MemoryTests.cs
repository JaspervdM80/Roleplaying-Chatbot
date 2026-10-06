using Microsoft.EntityFrameworkCore;
using Pgvector;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class MemoryTests(PostgresFixture postgres)
{
    private const string MemoriesJson = """{"memories":[{"type":"Preference","text":"Sam takes their coffee black.","importance":7,"characters":["Mira","Jun"]}]}""";

    private static FakeEmbeddingGenerator ByTopic() => new(text => FakeEmbeddingGenerator.Axis(text.Contains("coffee", StringComparison.OrdinalIgnoreCase) ? 0 : 1));

    private static FakeChatClientFactory Utility(string extraction, string summary = "Sam came in for coffee.", FakeEmbeddingGenerator? embeddings = null) =>
        new(new ScriptedChatClient(prompt => prompt[0].Text.Contains("long-term memory") ? extraction : summary), embeddings);

    private static async Task TalkAsync(StudioUser user, Guid sessionId, int exchanges, string line = "Coffee, black.")
    {
        for (var i = 0; i < exchanges; i++)
        {
            Assert.True((await user.Sessions.AddUserMessageAsync(sessionId, line)).IsSuccess);
            Assert.True((await user.Sessions.AddReplyAsync(sessionId, "Here you go.")).IsSuccess);
        }
    }

    private async Task<(ChatSession Session, List<MemoryEntry> Memories)> ReloadAsync(StudioUser user, Guid sessionId)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        var session = await db.ChatSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        var memories = await db.Memories.AsNoTracking().Where(m => m.SessionId == sessionId).ToListAsync();
        return (session, memories);
    }

    private async Task SeedAsync(StudioUser user, Guid sessionId, params (string Text, float[]? Vector, bool Pinned)[] memories)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        db.Memories.AddRange(memories.Select(m => new MemoryEntry
        {
            SessionId = sessionId,
            Text = m.Text,
            Embedding = m.Vector is null ? null : new Vector(m.Vector),
            IsPinned = m.Pinned,
            CreatedAt = user.Time.GetUtcNow(),
        }));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Upkeep_stores_the_extracted_memories_with_vectors_and_only_this_chats_characters()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        await user.EmbeddingModelAsync();
        await TalkAsync(user, session.Id, 2);

        await user.Upkeep(Utility(MemoriesJson, embeddings: ByTopic())).RunAsync(new MemoryJob(user.User.UserId!, session.Id), default);

        var (reloaded, memories) = await ReloadAsync(user, session.Id);
        var memory = Assert.Single(memories);
        Assert.Equal((MemoryType.Preference, "Sam takes their coffee black.", 7), (memory.Type, memory.Text, memory.Importance));
        Assert.Equal([reloaded.Scene.PresentCharacterIds.Single()], memory.RelatedCharacterIds);
        Assert.Equal(FakeEmbeddingGenerator.Axis(0), memory.Embedding!.ToArray());
        Assert.Equal(5, reloaded.MemoriesExtractedUpToSequence);
        Assert.Equal((1L, 5L), (memory.SourceFromSequence, memory.SourceToSequence));
    }

    [Fact]
    public async Task A_reply_that_is_not_json_skips_those_messages_without_writing_a_memory()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        await TalkAsync(user, session.Id, 2);

        await user.Upkeep(Utility("I could not find anything memorable, sorry!")).RunAsync(new MemoryJob(user.User.UserId!, session.Id), default);

        var (reloaded, memories) = await ReloadAsync(user, session.Id);
        Assert.Empty(memories);
        Assert.Equal(5, reloaded.MemoriesExtractedUpToSequence);
    }

    [Fact]
    public async Task Without_an_embedding_model_memories_are_still_kept_and_recalled_by_importance()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        await TalkAsync(user, session.Id, 2);

        await user.Upkeep(Utility(MemoriesJson)).RunAsync(new MemoryJob(user.User.UserId!, session.Id), default);
        var recalled = await user.Recall(Utility(MemoriesJson)).RecallAsync(session.Id, []);

        Assert.Null(Assert.Single((await ReloadAsync(user, session.Id)).Memories).Embedding);
        Assert.Equal(["Sam takes their coffee black."], recalled.Value.Select(m => m.Text));
    }

    [Fact]
    public async Task A_job_queued_for_another_owner_leaves_the_chat_untouched()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync();
        await owner.UtilityModelAsync();
        await other.UtilityModelAsync();
        await TalkAsync(owner, session.Id, 10, new string('x', 1_500));

        await other.Upkeep(Utility(MemoriesJson)).RunAsync(new MemoryJob(other.User.UserId!, session.Id), default);

        var (reloaded, memories) = await ReloadAsync(owner, session.Id);
        Assert.Empty(memories);
        Assert.Equal((0L, 0L, ""), (reloaded.MemoriesExtractedUpToSequence, reloaded.Summary.CoveredUpToSequence, reloaded.Summary.Text));
    }

    [Fact]
    public async Task A_memory_saved_without_a_vector_is_still_recalled_beside_vector_hits()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.EmbeddingModelAsync();
        await SeedAsync(user, session.Id, ("Sam takes their coffee black.", FakeEmbeddingGenerator.Axis(0), false), ("Mira is saving for a boat.", null, false));

        var recalled = await user.Recall(new FakeChatClientFactory(new FakeChatClient([]), ByTopic()))
            .RecallAsync(session.Id, [new Message { Sequence = 1, SpeakerName = "Sam", Content = "Coffee, please." }]);

        Assert.Equal(["Sam takes their coffee black.", "Mira is saving for a boat."], recalled.Value.Select(m => m.Text));
    }

    [Fact]
    public async Task Without_a_utility_model_upkeep_leaves_the_chat_untouched()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await TalkAsync(user, session.Id, 10);

        await user.Upkeep(Utility(MemoriesJson)).RunAsync(new MemoryJob(user.User.UserId!, session.Id), default);

        var (reloaded, memories) = await ReloadAsync(user, session.Id);
        Assert.Empty(memories);
        Assert.Equal((0L, 0L, ""), (reloaded.MemoriesExtractedUpToSequence, reloaded.Summary.CoveredUpToSequence, reloaded.Summary.Text));
    }

    [Fact]
    public async Task A_long_chat_is_folded_into_the_summary_and_the_turn_stops_sending_what_it_covers()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        var line = new string('x', 1_500);
        await TalkAsync(user, session.Id, 10, line);

        await user.Upkeep(Utility(MemoriesJson)).RunAsync(new MemoryJob(user.User.UserId!, session.Id), default);

        var (reloaded, _) = await ReloadAsync(user, session.Id);
        Assert.Equal("Sam came in for coffee.", reloaded.Summary.Text);
        Assert.InRange(reloaded.Summary.CoveredUpToSequence, 2, 20);

        var chat = new FakeChatClient(["Hi."]);
        Assert.True((await user.Turns(new FakeChatClientFactory(chat)).StreamReplyAsync(session.Id, _ => { })).IsSuccess);
        Assert.Contains("Sam came in for coffee.", chat.LastMessages![0].Text);
        Assert.Equal(21 - reloaded.Summary.CoveredUpToSequence, chat.LastMessages.Count - 1);
    }

    [Fact]
    public async Task A_memory_from_another_chat_is_never_recalled_however_close_it_is()
    {
        var user = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        var sibling = await user.ChatAsync("Jun");
        var stranger = await other.ChatAsync("Mira");
        await user.EmbeddingModelAsync();
        // Closer neighbours in another chat of the same user and in another user's chat.
        var crowd = Enumerable.Range(0, 120).Select(i => ($"Jun's regular orders coffee {i}.", (float[]?)FakeEmbeddingGenerator.Axis(0), false)).ToArray();
        await SeedAsync(user, sibling.Id, crowd);
        await SeedAsync(other, stranger.Id, crowd);
        var nearAxis = FakeEmbeddingGenerator.Axis(0);
        nearAxis[1] = 0.5f;
        await SeedAsync(user, session.Id, ("Sam takes their coffee black.", nearAxis, false), ("Mira's cat is called Pebble.", FakeEmbeddingGenerator.Axis(1), false));

        var recalled = await user.Recall(new FakeChatClientFactory(new FakeChatClient([]), ByTopic()))
            .RecallAsync(session.Id, [new Message { Sequence = 1, SpeakerName = "Sam", Content = "Coffee, please." }]);

        Assert.True(recalled.IsSuccess, recalled.Error);
        Assert.Equal(["Sam takes their coffee black.", "Mira's cat is called Pebble."], recalled.Value.Select(m => m.Text));
    }

    [Fact]
    public async Task Another_users_chat_recalls_nothing()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync();
        await SeedAsync(owner, session.Id, ("Sam takes their coffee black.", null, true));

        var recalled = await other.Recall(new FakeChatClientFactory(new FakeChatClient([]))).RecallAsync(session.Id, []);

        Assert.True(recalled.IsFailure);
    }

    [Fact]
    public async Task Pinned_memories_are_recalled_first_whatever_the_conversation()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.EmbeddingModelAsync();
        await SeedAsync(user, session.Id, ("Sam takes their coffee black.", FakeEmbeddingGenerator.Axis(0), false), ("Sam and Mira are engaged.", FakeEmbeddingGenerator.Axis(1), true));

        var recalled = await user.Recall(new FakeChatClientFactory(new FakeChatClient([]), ByTopic()))
            .RecallAsync(session.Id, [new Message { Sequence = 1, SpeakerName = "Sam", Content = "Coffee, please." }]);

        Assert.Equal(["Sam and Mira are engaged.", "Sam takes their coffee black."], recalled.Value.Select(m => m.Text));
    }

    [Fact]
    public async Task Saving_a_reply_queues_upkeep_for_the_owner_and_a_turn_sends_recalled_memories()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await SeedAsync(user, session.Id, ("Sam takes their coffee black.", null, false));
        var chat = new FakeChatClient(["Black, as always."]);
        var turns = user.Turns(new FakeChatClientFactory(chat));

        Assert.True((await turns.StreamReplyAsync(session.Id, _ => { })).IsSuccess);
        Assert.True((await turns.SaveReplyAsync(session.Id, "Black, as always.")).IsSuccess);

        Assert.Contains("Sam takes their coffee black.", chat.LastMessages![0].Text);
        Assert.True(user.MemoryQueue.Reader.TryRead(out var job));
        Assert.Equal(new MemoryJob(user.User.UserId!, session.Id), job);
    }
}
