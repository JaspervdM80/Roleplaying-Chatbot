using Microsoft.EntityFrameworkCore;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class ChatHistoryServiceTests(PostgresFixture postgres)
{
    private const string MemoriesJson = """{"memories":[{"type":"Event","text":"Sam ordered a coffee.","importance":6,"characters":["Mira"]}]}""";
    private const string ToTheHarbour = """{"location":"The harbour","present":["Mira"],"changes":[{"name":"Mira","outfit":"A raincoat"}]}""";

    private static FakeChatClientFactory Utility(string reply) => new(new FakeChatClient([reply]));

    private static async Task TalkAsync(StudioUser user, Guid sessionId, int exchanges)
    {
        for (var i = 0; i < exchanges; i++)
        {
            Assert.True((await user.Sessions.AddUserMessageAsync(sessionId, "Coffee, black.")).IsSuccess);
            Assert.True((await user.Sessions.ReplyAsync(sessionId, "Here you go.")).IsSuccess);
        }
    }

    private async Task<ApplicationDbContext> ContextAsync(StudioUser user) => await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);

    [Fact]
    public async Task After_taking_back_a_reply_the_memories_mined_from_it_are_gone_and_earlier_ones_stay()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        var job = new UpkeepJob(user.User.UserId!, session.Id);
        await TalkAsync(user, session.Id, 2);
        await user.Upkeep(Utility(MemoriesJson)).RunAsync(job, default);
        await TalkAsync(user, session.Id, 2);
        await user.Upkeep(Utility(MemoriesJson)).RunAsync(job, default);

        Assert.True((await user.History.TakeBackReplyAsync(session.Id)).IsSuccess);

        await using var db = await ContextAsync(user);
        var memory = Assert.Single(await db.Memories.Where(m => m.SessionId == session.Id).ToListAsync());
        Assert.Equal((1L, 5L), (memory.SourceFromSequence, memory.SourceToSequence));
        Assert.Equal([1L, 2, 3, 4, 5, 6, 7, 8], await db.Messages.Where(m => m.SessionId == session.Id).OrderBy(m => m.Sequence).Select(m => m.Sequence).ToListAsync());
        Assert.Equal(5, (await db.ChatSessions.SingleAsync(s => s.Id == session.Id)).MemoriesExtractedUpToSequence);
    }

    [Fact]
    public async Task Taking_back_a_reply_puts_the_scene_back_as_it_was_before_the_reply()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        var job = new UpkeepJob(user.User.UserId!, session.Id);
        await user.SceneUpkeep(Utility("""{"present":["Mira"]}""")).RunAsync(job, default);
        await TalkAsync(user, session.Id, 1);
        await user.SceneUpkeep(Utility(ToTheHarbour)).RunAsync(job, default);

        Assert.True((await user.History.TakeBackReplyAsync(session.Id)).IsSuccess);

        var reloaded = (await user.Sessions.GetAsync(session.Id)).Value;
        Assert.Equal(("Behind the counter", 1L), (reloaded.Scene.Location, reloaded.Scene.TrackedUpToSequence));
        Assert.Equal("An apron", Assert.Single(reloaded.CharacterStates).CurrentOutfit);
    }

    [Fact]
    public async Task Taking_back_a_reply_before_the_user_has_said_anything_keeps_the_scenarios_opening()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();

        Assert.True((await user.History.TakeBackReplyAsync(session.Id)).IsSuccess);

        Assert.Equal("The bell over the door rings.", Assert.Single((await user.Sessions.GetAsync(session.Id)).Value.Messages).Content);
    }

    [Fact]
    public async Task Editing_a_message_rewords_it_and_removes_everything_after_it()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await TalkAsync(user, session.Id, 2);
        var second = (await user.Sessions.GetAsync(session.Id)).Value.Messages.Single(m => m.Sequence == 2);

        Assert.True((await user.History.EditUserMessageAsync(session.Id, second.Id, "  Tea, actually. ")).IsSuccess);

        var messages = (await user.Sessions.GetAsync(session.Id)).Value.Messages;
        Assert.Equal([1L, 2], messages.Select(m => m.Sequence));
        Assert.Equal("Tea, actually.", messages[1].Content);
    }

    [Fact]
    public async Task Only_the_users_own_messages_can_be_edited()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await TalkAsync(user, session.Id, 1);
        var reply = (await user.Sessions.GetAsync(session.Id)).Value.Messages.Single(m => m.Sequence == 3);

        var result = await user.History.EditUserMessageAsync(session.Id, reply.Id, "Something else");

        Assert.True(result.IsFailure);
        Assert.Equal("Here you go.", (await user.Sessions.GetAsync(session.Id)).Value.Messages.Single(m => m.Sequence == 3).Content);
    }

    [Fact]
    public async Task Another_users_chat_can_be_neither_edited_rewound_nor_branched()
    {
        var owner = new StudioUser(postgres);
        var intruder = new StudioUser(postgres);
        var session = await owner.ChatAsync();
        await TalkAsync(owner, session.Id, 1);
        var line = (await owner.Sessions.GetAsync(session.Id)).Value.Messages.Single(m => m.Sequence == 2);

        Assert.True((await intruder.History.EditUserMessageAsync(session.Id, line.Id, "Mine now")).IsFailure);
        Assert.True((await intruder.History.TakeBackReplyAsync(session.Id)).IsFailure);
        Assert.True((await intruder.History.BranchAsync(session.Id, line.Id)).IsFailure);

        Assert.Equal(3, (await owner.Sessions.GetAsync(session.Id)).Value.Messages.Count);
        Assert.Empty((await intruder.Sessions.ListAsync()).Value);
    }

    [Fact]
    public async Task A_picture_of_a_removed_turn_stays_in_the_chat_under_no_message()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await TalkAsync(user, session.Id, 1);
        var reply = (await user.Sessions.GetAsync(session.Id)).Value.Messages[^1];
        await using (var db = await ContextAsync(user))
        {
            db.Images.Add(new GeneratedImage { SessionId = session.Id, MessageId = reply.Id, Prompt = "A café", StoragePath = "x.png" });
            await db.SaveChangesAsync();
        }

        Assert.True((await user.History.TakeBackReplyAsync(session.Id)).IsSuccess);

        await using var check = await ContextAsync(user);
        var picture = Assert.Single(await check.Images.Where(i => i.SessionId == session.Id).ToListAsync());
        Assert.Null(picture.MessageId);
    }

    [Fact]
    public async Task A_branch_is_a_new_chat_up_to_the_message_and_leaves_the_original_alone()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        await TalkAsync(user, session.Id, 2);
        await user.Upkeep(Utility(MemoriesJson)).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);
        var third = (await user.Sessions.GetAsync(session.Id)).Value.Messages.Single(m => m.Sequence == 3);

        var branched = await user.History.BranchAsync(session.Id, third.Id);

        Assert.True(branched.IsSuccess, branched.Error);
        var branch = (await user.Sessions.GetAsync(branched.Value.Id)).Value;
        Assert.Equal([1L, 2, 3], branch.Messages.Select(m => m.Sequence));
        Assert.Equal(["Mira"], branch.CharacterStates.Select(s => s.Character.Name));
        Assert.Equal(5, (await user.Sessions.GetAsync(session.Id)).Value.Messages.Count);
        await using var db = await ContextAsync(user);
        Assert.False(await db.Memories.AnyAsync(m => m.SessionId == branch.Id));
        Assert.True(await db.Memories.AnyAsync(m => m.SessionId == session.Id));
        Assert.Equal(2, (await user.Sessions.ListAsync()).Value.Count);
    }

    [Fact]
    public async Task Scene_tracking_that_read_the_chat_before_a_rewind_cannot_save_over_it()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();
        await TalkAsync(user, session.Id, 1);
        var history = user.History;
        // The rewind lands while the utility model is still answering, as a user can press "write again" mid-upkeep.
        var utility = new FakeChatClientFactory(new ScriptedChatClient(_ =>
        {
            Assert.True(history.TakeBackReplyAsync(session.Id).GetAwaiter().GetResult().IsSuccess);
            return ToTheHarbour;
        }));

        var refused = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => user.SceneUpkeep(utility).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default));

        // The worker drops only a conflict on the chat itself; any other is still an error.
        Assert.Contains(refused.Entries, e => e.Entity is ChatSession);

        var reloaded = (await user.Sessions.GetAsync(session.Id)).Value;
        Assert.Equal(("Behind the counter", 0L), (reloaded.Scene.Location, reloaded.Scene.TrackedUpToSequence));
    }
}
