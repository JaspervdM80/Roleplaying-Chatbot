using Microsoft.EntityFrameworkCore;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class MemoryServiceTests(PostgresFixture postgres)
{
    private static FakeEmbeddingGenerator ByTopic() => new(text => FakeEmbeddingGenerator.Axis(text.Contains("coffee", StringComparison.OrdinalIgnoreCase) ? 0 : 1));

    private static FakeChatClientFactory Clients(FakeEmbeddingGenerator? embeddings = null) => new(new FakeChatClient([]), embeddings);

    private static async Task TalkAsync(StudioUser user, Guid sessionId, int exchanges, string line)
    {
        for (var i = 0; i < exchanges; i++)
        {
            Assert.True((await user.Sessions.AddUserMessageAsync(sessionId, line)).IsSuccess);
            Assert.True((await user.Sessions.AddReplyAsync(sessionId, line)).IsSuccess);
        }
    }

    private async Task<MemoryEntry> StoredAsync(StudioUser user, Guid memoryId)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        return await db.Memories.AsNoTracking().SingleAsync(m => m.Id == memoryId);
    }

    private async Task<ChatSession> SessionAsync(StudioUser user, Guid sessionId)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        return await db.ChatSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
    }

    [Fact]
    public async Task A_memory_added_by_hand_is_embedded_and_listed_with_the_pinned_first()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.EmbeddingModelAsync();
        var memories = user.Memories(Clients(ByTopic()));
        var mira = session.Scene.PresentCharacterIds.Single();

        var pinned = await memories.AddAsync(session.Id, new MemoryEntry { Text = "Sam and Mira are engaged.", IsPinned = true, RelatedCharacterIds = [mira] });
        user.Time.Advance(TimeSpan.FromMinutes(1));
        var added = await memories.AddAsync(session.Id, new MemoryEntry { Type = MemoryType.Preference, Text = "Sam takes their coffee black." });
        var listed = await memories.ListAsync(session.Id);

        Assert.True(added.IsSuccess, added.Error);
        Assert.Equal(["Sam and Mira are engaged.", "Sam takes their coffee black."], listed.Value.Select(m => m.Text));
        Assert.All(listed.Value, m => Assert.Null(m.Embedding));
        var stored = await StoredAsync(user, added.Value.Id);
        Assert.Equal(FakeEmbeddingGenerator.Axis(0), stored.Embedding!.ToArray());
        Assert.Null(stored.SourceFromSequence);
        Assert.Equal([mira], (await StoredAsync(user, pinned.Value.Id)).RelatedCharacterIds);
    }

    [Fact]
    public async Task Changing_a_memorys_text_embeds_it_again_and_changing_only_its_importance_does_not()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.EmbeddingModelAsync();
        var embedded = 0;
        var memories = user.Memories(Clients(new FakeEmbeddingGenerator(text =>
        {
            embedded++;
            return FakeEmbeddingGenerator.Axis(text.Contains("coffee") ? 0 : 1);
        })));
        var memory = (await memories.AddAsync(session.Id, new MemoryEntry { Text = "Sam takes their coffee black." })).Value;

        var renamed = await memories.UpdateAsync(session.Id, new MemoryEntry { Id = memory.Id, Text = "Sam only drinks tea now.", Importance = 5 });
        var reweighed = await memories.UpdateAsync(session.Id, new MemoryEntry { Id = memory.Id, Text = "Sam only drinks tea now.", Importance = 9 });

        Assert.True(renamed.IsSuccess && reweighed.IsSuccess);
        Assert.Equal(2, embedded);
        var stored = await StoredAsync(user, memory.Id);
        Assert.Equal(("Sam only drinks tea now.", 9), (stored.Text, stored.Importance));
        Assert.Equal(FakeEmbeddingGenerator.Axis(1), stored.Embedding!.ToArray());
    }

    [Fact]
    public async Task A_memory_cannot_be_about_someone_never_met_in_the_chat()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        var stranger = await user.CharacterAsync("Jun");

        var added = await user.Memories(Clients()).AddAsync(session.Id, new MemoryEntry { Text = "Jun owes Sam money.", RelatedCharacterIds = [stranger.Id] });

        Assert.True(added.IsFailure);
        Assert.Empty((await user.Memories(Clients()).ListAsync(session.Id)).Value);
    }

    [Fact]
    public async Task Another_users_memories_cannot_be_read_edited_pinned_or_deleted()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync();
        var memory = (await owner.Memories(Clients()).AddAsync(session.Id, new MemoryEntry { Text = "Sam takes their coffee black." })).Value;
        var theirs = other.Memories(Clients());

        Assert.True((await theirs.ListAsync(session.Id)).IsFailure);
        Assert.True((await theirs.UpdateAsync(session.Id, new MemoryEntry { Id = memory.Id, Text = "Overwritten." })).IsFailure);
        Assert.True((await theirs.SetPinnedAsync(session.Id, memory.Id, true)).IsFailure);
        Assert.True((await theirs.DeleteAsync(session.Id, memory.Id)).IsFailure);
        Assert.True((await theirs.SaveSummaryAsync(session.Id, "Overwritten.", 0)).IsFailure);

        var stored = await StoredAsync(owner, memory.Id);
        Assert.Equal(("Sam takes their coffee black.", false), (stored.Text, stored.IsPinned));
        Assert.Equal("", (await SessionAsync(owner, session.Id)).Summary.Text);
    }

    [Fact]
    public async Task A_memory_is_only_reached_through_the_chat_it_belongs_to()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        var sibling = await user.ChatAsync("Jun");
        var memories = user.Memories(Clients());
        var memory = (await memories.AddAsync(session.Id, new MemoryEntry { Text = "Sam takes their coffee black." })).Value;

        Assert.True((await memories.DeleteAsync(sibling.Id, memory.Id)).IsFailure);
        Assert.True((await memories.SetPinnedAsync(session.Id, memory.Id, true)).IsSuccess);
        Assert.True((await StoredAsync(user, memory.Id)).IsPinned);
        Assert.True((await memories.DeleteAsync(session.Id, memory.Id)).IsSuccess);
        Assert.Empty((await memories.ListAsync(session.Id)).Value);
    }

    [Fact]
    public async Task An_edited_summary_keeps_covering_the_same_messages()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        await TalkAsync(user, session.Id, 10, new string('x', 1_500));
        await user.Upkeep(new FakeChatClientFactory(new ScriptedChatClient(_ => "Sam came in for coffee."))).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);
        var covered = (await SessionAsync(user, session.Id)).Summary.CoveredUpToSequence;

        var saved = await user.Memories(Clients()).SaveSummaryAsync(session.Id, "  Sam came in for coffee and stayed.  ", covered);

        Assert.True(saved.IsSuccess, saved.Error);
        var summary = (await SessionAsync(user, session.Id)).Summary;
        Assert.Equal(("Sam came in for coffee and stayed.", covered), (summary.Text, summary.CoveredUpToSequence));
        Assert.True(covered > 0);
    }

    [Fact]
    public async Task A_rebuild_is_refused_without_a_utility_model_and_queued_with_one()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        var memories = user.Memories(Clients());

        Assert.True((await memories.RebuildSummaryAsync(session.Id)).IsFailure);
        Assert.False(user.UpkeepQueue.Reader.TryRead(out _));

        await user.UtilityModelAsync();
        Assert.True((await memories.RebuildSummaryAsync(session.Id)).IsSuccess);
        Assert.True(user.UpkeepQueue.Reader.TryRead(out var job));
        Assert.Equal(new UpkeepJob(user.User.UserId!, session.Id, RebuildSummary: true), job);
    }

    [Fact]
    public async Task A_rebuild_starts_from_nothing_and_folds_until_the_rest_fits_in_the_window()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        var line = new string('x', 1_500);
        await TalkAsync(user, session.Id, 20, line);
        await user.Memories(Clients()).SaveSummaryAsync(session.Id, "Notes the user wrote.", 0);
        var stories = new List<string>();
        var utility = new FakeChatClientFactory(new ScriptedChatClient(prompt =>
        {
            if (prompt[0].Text.Contains("long-term memory"))
            {
                return """{"memories":[]}""";
            }

            stories.Add(prompt[1].Text);
            return $"Summary {stories.Count}.";
        }));
        var notifier = new MemoryNotifier();
        var announced = new List<(Guid, bool)>();
        notifier.Changed += (id, rebuildEnded) => announced.Add((id, rebuildEnded));

        await user.Upkeep(utility, notifier).RunAsync(new UpkeepJob(user.User.UserId!, session.Id, RebuildSummary: true), default);

        var summary = (await SessionAsync(user, session.Id)).Summary;
        // 41 messages of about 375 tokens outgrow one bounded fold, so a single pass would leave most of the chat unsummarized.
        Assert.True(stories.Count >= 2, $"Folded {stories.Count} times");
        Assert.DoesNotContain("Notes the user wrote.", stories[0]);
        Assert.Contains("Summary 1.", stories[1]);
        Assert.Equal($"Summary {stories.Count}.", summary.Text);
        Assert.Empty(SessionSummarizing.ToFold(await MessagesAsync(user, session.Id), summary.CoveredUpToSequence));
        Assert.Equal([(session.Id, true)], announced);
    }

    [Fact]
    public async Task A_summary_edited_before_a_fold_cannot_be_saved_over_what_the_fold_added()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        await TalkAsync(user, session.Id, 10, new string('x', 1_500));
        var editedFrom = (await SessionAsync(user, session.Id)).Summary.CoveredUpToSequence;

        await user.Upkeep(new FakeChatClientFactory(new ScriptedChatClient(_ => "Sam came in for coffee."))).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);
        var saved = await user.Memories(Clients()).SaveSummaryAsync(session.Id, "A draft about the first few messages.", editedFrom);

        Assert.True(saved.IsFailure);
        Assert.Equal("Sam came in for coffee.", (await SessionAsync(user, session.Id)).Summary.Text);
    }

    [Fact]
    public async Task A_fold_does_not_write_over_a_summary_the_user_saved_while_it_ran()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        await TalkAsync(user, session.Id, 10, new string('x', 1_500));
        var memories = user.Memories(Clients());
        var utility = new FakeChatClientFactory(new ScriptedChatClient(prompt =>
        {
            if (prompt[0].Text.Contains("long-term memory"))
            {
                return """{"memories":[]}""";
            }

            Assert.True(memories.SaveSummaryAsync(session.Id, "The user's own words.", 0).GetAwaiter().GetResult().IsSuccess);
            return "The model's summary.";
        }));

        await user.Upkeep(utility).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        var summary = (await SessionAsync(user, session.Id)).Summary;
        Assert.Equal(("The user's own words.", 0L), (summary.Text, summary.CoveredUpToSequence));
    }

    [Fact]
    public async Task A_rebuild_without_a_usable_utility_model_still_tells_the_page_it_ended()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        var notifier = new MemoryNotifier();
        var announced = new List<(Guid, bool)>();
        notifier.Changed += (id, rebuildEnded) => announced.Add((id, rebuildEnded));

        await user.Upkeep(Clients(), notifier).RunAsync(new UpkeepJob(user.User.UserId!, session.Id, RebuildSummary: true), default);

        Assert.Equal([(session.Id, true)], announced);
    }

    [Fact]
    public async Task A_rebuild_that_does_not_fit_in_the_queue_is_reported_as_failed()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        while (user.UpkeepQueue.Enqueue(new UpkeepJob(user.User.UserId!, session.Id)))
        {
        }

        Assert.True((await user.Memories(Clients()).RebuildSummaryAsync(session.Id)).IsFailure);
    }

    [Fact]
    public async Task Rewording_a_memory_changes_what_recall_brings_back()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.EmbeddingModelAsync();
        var clients = Clients(ByTopic());
        var memories = user.Memories(clients);
        var memory = (await memories.AddAsync(session.Id, new MemoryEntry { Text = "Sam takes their coffee black." })).Value;
        await memories.AddAsync(session.Id, new MemoryEntry { Text = "Mira's cat is called Pebble." });
        var asked = new Message { Sequence = 1, SpeakerName = "Sam", Content = "Coffee, please." };

        var before = await user.Recall(clients).RecallAsync(session.Id, [asked]);
        await memories.UpdateAsync(session.Id, new MemoryEntry { Id = memory.Id, Text = "Sam has given up on caffeine." });
        var after = await user.Recall(clients).RecallAsync(session.Id, [asked]);

        Assert.Equal("Sam takes their coffee black.", before.Value[0].Text);
        Assert.Equal(["Mira's cat is called Pebble.", "Sam has given up on caffeine."], after.Value.Select(m => m.Text).Order());
        Assert.DoesNotContain(after.Value, m => m.Text.Contains("coffee"));
    }

    [Fact]
    public async Task Recall_stamps_when_each_memory_it_brought_back_was_recalled()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        var memory = (await user.Memories(Clients()).AddAsync(session.Id, new MemoryEntry { Text = "Sam takes their coffee black." })).Value;
        user.Time.Advance(TimeSpan.FromHours(1));

        Assert.True((await user.Recall(Clients()).RecallAsync(session.Id, [])).IsSuccess);

        Assert.Equal(user.Time.GetUtcNow(), (await StoredAsync(user, memory.Id)).LastRecalledAt);
    }

    private async Task<List<Message>> MessagesAsync(StudioUser user, Guid sessionId)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        return await db.Messages.AsNoTracking().Where(m => m.SessionId == sessionId).OrderBy(m => m.Sequence).ToListAsync();
    }
}
