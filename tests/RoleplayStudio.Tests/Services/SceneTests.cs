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

    private static FakeModelClientFactory Utility(string reply) => new(new FakeChatClient([reply]));

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
        Assert.Equal("Mira", Assert.Single(mine.Value.PresentCharacters()).Name);
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

    [Fact]
    public async Task A_newcomer_gets_a_portrait_queued_when_there_is_an_image_model()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        await user.ImageModelAsync();

        await user.SceneUpkeep(Utility(BessWalksIn)).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        var bess = (await user.Characters.ListAsync()).Value.Single(c => c.Name == "Old Bess");
        Assert.Equal(session.Id, bess.IntroducedInSessionId);
        Assert.True(user.PictureQueue.Reader.TryRead(out var job));
        Assert.Equal((bess.Id, (Guid?)null, false), (job.CharacterId!.Value, job.SessionId, job.RenewsReference));
    }

    [Fact]
    public async Task Without_an_image_model_no_portrait_is_queued()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();

        await user.SceneUpkeep(Utility(BessWalksIn)).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        Assert.False(user.PictureQueue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task A_renamed_and_described_newcomer_is_saved_and_gets_a_portrait_that_renews_their_reference()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        await user.ImageModelAsync();
        await user.SceneUpkeep(Utility(BessWalksIn)).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);
        Assert.True(user.PictureQueue.Reader.TryRead(out var first));
        user.PictureQueue.Finish(first.Id);
        Assert.True((await user.Sessions.AddUserMessageAsync(session.Id, "What's your name, cook?")).IsSuccess);
        const string named = """{"changes":[{"name":"Old Bess","newName":"Bess Harrow","appearance":"Stout, grey bun, a burn scar on one forearm"}]}""";

        await user.SceneUpkeep(Utility(named)).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        var bess = (await user.Characters.ListAsync()).Value.Single(c => c.Id == first.CharacterId);
        Assert.Equal(("Bess Harrow", "Stout, grey bun, a burn scar on one forearm"), (bess.Name, bess.Appearance));
        Assert.True(user.PictureQueue.Reader.TryRead(out var renewed));
        Assert.Equal((bess.Id, true), (renewed.CharacterId!.Value, renewed.RenewsReference));
    }

    [Fact]
    public async Task Someone_renamed_takes_the_new_name_on_the_lines_they_already_spoke_in_the_chat()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        await user.SceneUpkeep(Utility("""{"present":["Old Bess"],"newcomers":[{"name":"Old Bess","age":61}]}""")).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);
        var spoken = await user.Sessions.ReplyAsync(session.Id, "Evening, love.");
        Assert.Equal("Old Bess", spoken.Value.Single().SpeakerName);

        await user.SceneUpkeep(Utility("""{"changes":[{"name":"Old Bess","newName":"Bess Harrow"}]}""")).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        var speakers = await db.Messages.Where(m => m.SessionId == session.Id && m.Role != MessageRole.User).Select(m => m.SpeakerName).ToListAsync();
        Assert.Contains("Bess Harrow", speakers);
        Assert.DoesNotContain("Old Bess", speakers);
    }

    [Fact]
    public async Task Editing_an_introduced_character_hands_them_to_the_user_for_good()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        await user.SceneUpkeep(Utility(BessWalksIn)).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);
        var bess = (await user.Characters.ListAsync()).Value.Single(c => c.Name == "Old Bess");

        bess.Appearance = "Tall and thin";
        Assert.True((await user.Characters.UpdateAsync(bess)).IsSuccess);

        Assert.Null((await user.Characters.ListAsync()).Value.Single(c => c.Id == bess.Id).IntroducedInSessionId);
    }

    [Fact]
    public async Task What_the_user_corrects_in_the_scene_is_in_the_next_reply_prompt()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        var opened = CharacterSnapshot.Of(session.CharacterStates.Single());

        Assert.True((await user.Sessions.CorrectSceneAsync(session.Id, SceneFacts.Of(session.Scene), new SceneFacts("The cellar", "Midnight", "Hushed"))).IsSuccess);
        Assert.True((await user.Sessions.CorrectPersonaOutfitAsync(session.Id, null, "A borrowed raincoat")).IsSuccess);
        var corrected = await user.Sessions.CorrectCharacterAsync(session.Id, opened, new CharacterSnapshot { CharacterId = opened.CharacterId, CurrentOutfit = "A green wool dress", Relationship = "Old friends" });
        Assert.True(corrected.IsSuccess, corrected.Error);
        await user.Sessions.AddUserMessageAsync(session.Id, "Down here?");
        var client = new FakeChatClient(["Down here."]);

        Assert.True((await user.Turns(new FakeModelClientFactory(client)).StreamReplyAsync(session.Id, _ => { })).IsSuccess);

        var system = client.LastMessages![0].Text;
        Assert.Contains("Wearing now: A green wool dress", system);
        Assert.Contains("With Sam: Old friends", system);
        Assert.Contains("Wearing now: A borrowed raincoat", system);
        Assert.Contains("Location: The cellar", system);
        Assert.DoesNotContain("An apron", system);
    }

    [Fact]
    public async Task Only_the_owner_can_correct_a_chat_and_only_someone_met_in_it()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync("Mira");
        var mira = session.CharacterStates.Single().CharacterId;
        var stranger = await owner.CharacterAsync("Thorne");

        var opened = SceneFacts.Of(session.Scene);

        Assert.True((await other.Sessions.CorrectSceneAsync(session.Id, opened, opened with { Location = "Their place" })).IsFailure);
        Assert.True((await other.Sessions.CorrectPersonaOutfitAsync(session.Id, null, "Nothing")).IsFailure);
        Assert.True((await other.Sessions.CorrectCharacterAsync(session.Id, new CharacterSnapshot(), new CharacterSnapshot { CharacterId = mira, CurrentOutfit = "Nothing" })).IsFailure);
        Assert.True((await owner.Sessions.CorrectCharacterAsync(session.Id, new CharacterSnapshot(), new CharacterSnapshot { CharacterId = stranger.Id, CurrentOutfit = "A coat" })).IsFailure);

        var reloaded = await ReloadAsync(owner, session.Id);
        Assert.Equal(("Behind the counter", null, "An apron"), (reloaded.Scene.Location, reloaded.Scene.PersonaOutfit, reloaded.CharacterStates.Single().CurrentOutfit));
    }

    [Fact]
    public async Task Scene_tracking_that_read_the_chat_before_a_correction_cannot_save_over_it()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        var sessions = user.Sessions;
        // The user saves a correction while the utility model is still reading the chat as it was.
        var utility = new FakeModelClientFactory(new ScriptedChatClient(_ =>
        {
            var opened = SceneFacts.Of(session.Scene);
            Assert.True(sessions.CorrectSceneAsync(session.Id, opened, opened with { Location = "The cellar" }).GetAwaiter().GetResult().IsSuccess);
            return BessWalksIn;
        }));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => user.SceneUpkeep(utility).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default));

        var reloaded = await ReloadAsync(user, session.Id);
        Assert.Equal(("The cellar", 0L), (reloaded.Scene.Location, reloaded.Scene.TrackedUpToSequence));
    }

    [Fact]
    public async Task A_correction_saved_after_tracking_keeps_what_tracking_changed_in_the_fields_the_user_left_alone()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        var opened = CharacterSnapshot.Of(session.CharacterStates.Single());
        var sceneOpened = SceneFacts.Of(session.Scene);
        await user.SceneUpkeep(Utility(BessWalksIn)).RunAsync(new UpkeepJob(user.User.UserId!, session.Id), default);

        Assert.True((await user.Sessions.CorrectCharacterAsync(session.Id, opened, new CharacterSnapshot { CharacterId = opened.CharacterId, CurrentOutfit = "A shawl" })).IsSuccess);
        Assert.True((await user.Sessions.CorrectSceneAsync(session.Id, sceneOpened, sceneOpened with { Mood = "Hushed" })).IsSuccess);

        var reloaded = await ReloadAsync(user, session.Id);
        var mira = reloaded.CharacterStates.Single(s => s.CharacterId == opened.CharacterId);
        Assert.Equal(("A shawl", "Warm"), (mira.CurrentOutfit, mira.Status));
        Assert.Equal(("The common room", "Late evening", "Hushed"), (reloaded.Scene.Location, reloaded.Scene.TimeOfDay, reloaded.Scene.Mood));
    }
}
