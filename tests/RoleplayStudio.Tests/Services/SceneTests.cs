using Microsoft.EntityFrameworkCore;
using RoleplayStudio.AI.Scene;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class SceneTests(PostgresFixture postgres)
{
    private const string BessWalksIn = """
        {"location":"The common room","timeOfDay":"Late evening","present":["Mira","Old Bess"],
         "changes":[{"name":"Mira","status":"Warm"}],
         "newcomers":[{"name":"Old Bess","role":"cook","age":61,"outfit":"Kitchen whites"}]}
        """;

    private static FakeChatClientFactory Utility(string reply) => new(new FakeChatClient([reply]));

    private async Task<ChatSession> ReloadAsync(StudioUser user, Guid sessionId)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        return await db.ChatSessions
            .Include(s => s.Scenario)
            .Include(s => s.CharacterStates).ThenInclude(s => s.Character)
            .AsNoTracking()
            .AsSplitQuery()
            .SingleAsync(s => s.Id == sessionId);
    }

    [Fact]
    public async Task A_newcomer_is_saved_as_the_owners_character_and_joins_the_cast_and_the_scene()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        Assert.True((await user.Sessions.AddUserMessageAsync(session.Id, "Is the cook still up?")).IsSuccess);
        var notified = new List<Guid>();
        var notifier = new SceneNotifier();
        notifier.Changed += notified.Add;

        await user.SceneUpkeep(Utility(BessWalksIn), notifier).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        var reloaded = await ReloadAsync(user, session.Id);
        var bess = reloaded.CharacterStates.Single(s => s.Character.Name == "Old Bess");
        var mira = reloaded.CharacterStates.Single(s => s.Character.Name == "Mira");
        Assert.Equal(("The common room", "Late evening", 2L), (reloaded.Scene.Location, reloaded.Scene.TimeOfDay, reloaded.Scene.TrackedUpToSequence));
        Assert.Equal([mira.CharacterId, bess.CharacterId], reloaded.Scene.PresentCharacterIds);
        Assert.Equal(("Kitchen whites", "Warm"), (bess.CurrentOutfit, mira.Status));
        Assert.Contains((await user.Characters.ListAsync()).Value, c => c.Id == bess.CharacterId);
        Assert.Contains((await user.Chatbots.GetAsync(reloaded.Scenario.ChatbotId)).Value.Cast, m => m.CharacterId == bess.CharacterId && m.Role == "cook");
        Assert.Equal([session.Id], notified);
    }

    [Fact]
    public async Task A_reply_that_is_not_json_moves_past_those_messages_and_leaves_the_scene_alone()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.UtilityModelAsync();

        await user.SceneUpkeep(Utility("Nothing changed, I think.")).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        var reloaded = await ReloadAsync(user, session.Id);
        Assert.Equal((1L, "Behind the counter"), (reloaded.Scene.TrackedUpToSequence, reloaded.Scene.Location));
        Assert.Single(reloaded.Scene.PresentCharacterIds);
    }

    [Fact]
    public async Task The_scene_reloads_without_messages_for_its_owner_and_is_not_found_for_anyone_else()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync("Mira");

        var mine = await owner.Sessions.GetSceneAsync(session.Id);
        var theirs = await other.Sessions.GetSceneAsync(session.Id);

        Assert.True(mine.IsSuccess, mine.Error);
        Assert.Equal("Mira", Assert.Single(mine.Value.PresentCharacters(mine.Value.CharacterStates)).Name);
        Assert.Empty(mine.Value.Messages);
        Assert.True(theirs.IsFailure);
        Assert.False(theirs.IsCancelled);
    }

    [Fact]
    public async Task A_job_queued_for_another_owner_leaves_the_chat_untouched_and_creates_nobody()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync();
        await other.UtilityModelAsync();

        await other.SceneUpkeep(Utility(BessWalksIn)).RunAsync(new UpkeepJob(other.User.UserId!, session.Id), default);

        var reloaded = await ReloadAsync(owner, session.Id);
        Assert.Equal(0L, reloaded.Scene.TrackedUpToSequence);
        Assert.Single(reloaded.CharacterStates);
        Assert.Empty((await other.Characters.ListAsync()).Value);
    }
}
