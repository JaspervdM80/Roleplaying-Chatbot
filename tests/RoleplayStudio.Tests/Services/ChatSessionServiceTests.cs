using Microsoft.Extensions.Logging.Abstractions;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class ChatSessionServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Starting_a_chat_snapshots_the_scene_outfits_opening_message_and_default_model()
    {
        var user = new StudioUser(postgres);

        var session = await user.ChatAsync();

        var loaded = (await user.Sessions.GetAsync(session.Id)).Value;
        var model = Assert.Single((await user.Profiles.ListAsync()).Value);
        Assert.Equal(model.Id, loaded.ChatModelProfileId);
        Assert.Equal("Behind the counter", loaded.Scene.Location);
        Assert.Equal("apron", Assert.Single(loaded.CharacterStates).CurrentOutfit.Top);
        var opening = Assert.Single(loaded.Messages);
        Assert.Equal("Mira", opening.SpeakerName);
        Assert.Equal(user.Time.GetUtcNow(), loaded.LastActivityAt);
    }

    [Fact]
    public async Task A_chat_cannot_be_started_as_another_users_persona()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var chatbot = await owner.ChatbotAsync(await owner.CharacterAsync("Mira"));
        var scenario = await owner.ScenarioAsync(chatbot);
        var theirs = await other.PersonaAsync();

        var result = await owner.Sessions.StartAsync(chatbot.Id, scenario.Id, theirs.Id);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_chat_cannot_be_started_from_another_users_scenario()
    {
        var owner = new StudioUser(postgres);
        var intruder = new StudioUser(postgres);
        var chatbot = await owner.ChatbotAsync(await owner.CharacterAsync("Mira"));
        var scenario = await owner.ScenarioAsync(chatbot);
        var persona = await intruder.PersonaAsync();

        var result = await intruder.Sessions.StartAsync(chatbot.Id, scenario.Id, persona.Id);

        Assert.True(result.IsFailure);
        Assert.Empty((await owner.Sessions.ListAsync()).Value);
    }

    [Fact]
    public async Task Messages_follow_in_sequence_and_a_reply_is_spoken_by_the_one_character_present()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        user.Time.Advance(TimeSpan.FromMinutes(5));

        var sent = await user.Sessions.AddUserMessageAsync(session.Id, "  A latte, please. ");
        var reply = await user.Sessions.AddReplyAsync(session.Id, "*nods* Coming up.");

        Assert.Equal((2, MessageRole.User, "Sam", "A latte, please."), (sent.Value.Sequence, sent.Value.Role, sent.Value.SpeakerName, sent.Value.Content));
        Assert.Equal((3, MessageRole.Character, "Mira"), (reply.Value.Sequence, reply.Value.Role, reply.Value.SpeakerName));
        Assert.Equal(user.Time.GetUtcNow(), (await user.Sessions.GetAsync(session.Id)).Value.LastActivityAt);
    }

    [Fact]
    public async Task Another_users_chat_reads_as_not_found_and_takes_no_messages()
    {
        var owner = new StudioUser(postgres);
        var intruder = new StudioUser(postgres);
        var session = await owner.ChatAsync();

        Assert.Empty((await intruder.Sessions.ListAsync()).Value);
        Assert.True((await intruder.Sessions.GetAsync(session.Id)).IsFailure);
        Assert.True((await intruder.Sessions.AddUserMessageAsync(session.Id, "Hello?")).IsFailure);
        Assert.True((await intruder.Sessions.AddReplyAsync(session.Id, "Hi.")).IsFailure);
        Assert.True((await intruder.Sessions.DeleteAsync(session.Id)).IsFailure);
        Assert.True((await intruder.Sessions.SetModelAsync(session.Id, null)).IsFailure);

        var loaded = (await owner.Sessions.GetAsync(session.Id)).Value;
        Assert.Single(loaded.Messages);
        Assert.NotNull(loaded.ChatModelProfileId);
    }

    [Fact]
    public async Task A_chat_cannot_switch_to_another_users_model()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync();
        var theirs = await other.ChatModelAsync();

        Assert.True((await owner.Sessions.SetModelAsync(session.Id, theirs.Id)).IsFailure);
        Assert.NotEqual(theirs.Id, (await owner.Sessions.GetAsync(session.Id)).Value.ChatModelProfileId);
    }

    [Fact]
    public async Task A_turn_streams_the_reply_from_a_prompt_built_from_the_chat()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.Sessions.AddUserMessageAsync(session.Id, "A latte, please.");
        var client = new FakeChatClient(["Coming ", "up."]);
        var turns = new ChatTurnService(user.Sessions, user.Profiles, new FakeChatClientFactory(client), NullLogger<ChatTurnService>.Instance);
        var streamed = "";

        var result = await turns.StreamReplyAsync(session.Id, text => streamed += text);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Coming up.", streamed);
        Assert.Contains("apron", client.LastMessages![0].Text);
        Assert.Equal("A latte, please.", client.LastMessages[^1].Text);
    }

    [Fact]
    public async Task A_chat_without_a_model_asks_for_one_instead_of_streaming()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.Sessions.SetModelAsync(session.Id, null);
        var turns = new ChatTurnService(user.Sessions, user.Profiles, new FakeChatClientFactory(new FakeChatClient(["Hi"])), NullLogger<ChatTurnService>.Instance);

        var result = await turns.StreamReplyAsync(session.Id, _ => { });

        Assert.True(result.IsFailure);
        Assert.False(result.IsCancelled);
    }
}
