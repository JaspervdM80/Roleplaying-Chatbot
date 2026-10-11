using Microsoft.EntityFrameworkCore;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Infrastructure.Data;
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
        Assert.Equal("An apron", Assert.Single(loaded.CharacterStates).CurrentOutfit);
        var opening = Assert.Single(loaded.Messages);
        Assert.Equal("Mira", opening.SpeakerName);
        Assert.Equal(user.Time.GetUtcNow(), loaded.LastActivityAt);
    }

    [Fact]
    public async Task The_chats_list_puts_the_latest_activity_first_with_its_chatbot_scenario_persona_and_last_line()
    {
        var user = new StudioUser(postgres);
        var older = await user.ChatAsync("Mira");
        user.Time.Advance(TimeSpan.FromHours(1));
        var newer = await user.ChatAsync("Jun");
        user.Time.Advance(TimeSpan.FromHours(1));

        await user.Sessions.AddUserMessageAsync(older.Id, "Two coffees, please.");

        var chats = (await user.Sessions.ListAsync()).Value;
        Assert.Equal([older.Id, newer.Id], chats.Select(c => c.Id));
        var top = chats[0];
        Assert.Equal(("Seaside Café", "Morning rush", "Sam"), (top.ChatbotName, top.ScenarioTitle, top.PersonaName));
        Assert.Equal(["Mira"], top.Characters.Select(c => c.Name));
        Assert.Equal("Sam: Two coffees, please.", top.LastLine);
        Assert.Equal("Jun: The bell over the door rings.", chats[1].LastLine);
    }

    [Fact]
    public async Task Deleting_a_chat_deletes_its_messages_character_states_and_memories()
    {
        var user = new StudioUser(postgres);
        var deleted = await user.ChatAsync("Mira");
        var kept = await user.ChatAsync("Jun");
        await using (var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!))
        {
            db.Memories.AddRange(
                new MemoryEntry { SessionId = deleted.Id, Text = "Sam takes their coffee black." },
                new MemoryEntry { SessionId = kept.Id, Text = "Sam is allergic to cinnamon." });
            await db.SaveChangesAsync();
        }

        Assert.True((await user.Sessions.DeleteAsync(deleted.Id)).IsSuccess);

        await using var check = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        Assert.False(await check.Memories.AnyAsync(m => m.SessionId == deleted.Id));
        Assert.False(await check.Messages.AnyAsync(m => m.SessionId == deleted.Id));
        Assert.False(await check.CharacterStates.AnyAsync(s => s.SessionId == deleted.Id));
        Assert.True(await check.Memories.AnyAsync(m => m.SessionId == kept.Id));
        Assert.Equal([kept.Id], (await user.Sessions.ListAsync()).Value.Select(c => c.Id));
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
        var reply = await user.Sessions.ReplyAsync(session.Id, "*nods* Coming up.");

        Assert.Equal((2, MessageRole.User, "Sam", "A latte, please."), (sent.Value.Sequence, sent.Value.Role, sent.Value.SpeakerName, sent.Value.Content));
        Assert.Equal((3, MessageRole.Character, "Mira"), (reply.Value[0].Sequence, reply.Value[0].Role, reply.Value[0].SpeakerName));
        Assert.Equal(user.Time.GetUtcNow(), (await user.Sessions.GetAsync(session.Id)).Value.LastActivityAt);
    }

    [Fact]
    public async Task A_group_reply_is_saved_as_one_message_per_speaker_in_order()
    {
        var user = new StudioUser(postgres);
        await user.ChatModelAsync();
        var mira = await user.CharacterAsync("Mira");
        var jun = await user.CharacterAsync("Jun");
        var chatbot = await user.ChatbotAsync(mira, jun);
        var scenario = await user.ScenarioAsync(chatbot, opening: null);
        var session = (await user.Sessions.StartAsync(chatbot.Id, scenario.Id, (await user.PersonaAsync()).Id)).Value;

        var saved = await user.Sessions.ReplyAsync(session.Id, "*The door bangs.*\n**Mira:** \"Morning!\"\n**Jun:** \"Hey.\"");

        Assert.Equal(
            [(1L, MessageRole.Narrator, (Guid?)null, "Narrator", "*The door bangs.*"), (2, MessageRole.Character, mira.Id, "Mira", "\"Morning!\""), (3, MessageRole.Character, jun.Id, "Jun", "\"Hey.\"")],
            (await user.Sessions.GetAsync(session.Id)).Value.Messages.Select(m => (m.Sequence, m.Role, m.SpeakerCharacterId, m.SpeakerName, m.Content)));
        Assert.Equal(3, saved.Value.Count);
    }

    [Fact]
    public async Task A_segment_naming_a_character_who_is_not_present_is_saved_as_narration()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        var absent = await user.CharacterAsync("Thorne");

        var saved = await user.Sessions.AddReplyAsync(session.Id, _ => [new ReplySegment(absent.Id, "\"I'm not here.\"")]);

        Assert.Equal((MessageRole.Narrator, (Guid?)null), (saved.Value[0].Role, saved.Value[0].SpeakerCharacterId));
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
        Assert.True((await intruder.Sessions.ReplyAsync(session.Id, "Hi.")).IsFailure);
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
        var turns = user.Turns(new FakeChatClientFactory(client));
        var streamed = "";

        var result = await turns.StreamReplyAsync(session.Id, text => streamed += text);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Coming up.", streamed);
        Assert.Contains("apron", client.LastMessages![0].Text);
        Assert.Equal("A latte, please.", client.LastMessages[^1].Text);
    }

    [Fact]
    public async Task Bringing_someone_in_puts_them_in_the_scene_and_in_the_next_reply()
    {
        var user = new StudioUser(postgres);
        await user.ChatModelAsync();
        var mira = await user.CharacterAsync("Mira");
        var jun = await user.CharacterAsync("Jun", outfit: "A wet raincoat");
        var chatbot = await user.ChatbotAsync(mira, jun);
        var scenario = await user.ScenarioAsync(chatbot, starting: mira);
        var session = (await user.Sessions.StartAsync(chatbot.Id, scenario.Id, (await user.PersonaAsync()).Id)).Value;
        var client = new FakeChatClient(["*The door bangs open.*\n\n**Jun:** Anyone got a towel?"]);
        var turns = user.Turns(new FakeChatClientFactory(client));
        var hint = new DirectorHint.BringIn(jun.Id);
        var streamed = "";

        var directed = await turns.DirectAsync(session.Id, hint);
        Assert.True(directed.IsSuccess, directed.Error);
        var reply = await turns.StreamReplyAsync(session.Id, text => streamed += text, hint);
        Assert.True(reply.IsSuccess, reply.Error);
        var saved = (await turns.SaveReplyAsync(session.Id, streamed)).Value;

        var scene = (await user.Sessions.GetSceneAsync(session.Id)).Value;
        Assert.Equal([mira.Id, jun.Id], scene.Scene.PresentCharacterIds);
        Assert.Equal("A wet raincoat", scene.CharacterStates.Single(s => s.CharacterId == jun.Id).CurrentOutfit);
        Assert.Contains("Jun arrives", client.LastMessages![0].Text);
        Assert.Equal(jun.Id, saved[^1].SpeakerCharacterId);
    }

    [Fact]
    public async Task The_cast_of_a_chat_is_its_chatbots_whole_cast_and_only_its_owner_can_list_it()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        await owner.ChatModelAsync();
        var mira = await owner.CharacterAsync("Mira");
        var jun = await owner.CharacterAsync("Jun");
        var chatbot = await owner.ChatbotAsync(mira, jun);
        var scenario = await owner.ScenarioAsync(chatbot, starting: mira);
        var session = (await owner.Sessions.StartAsync(chatbot.Id, scenario.Id, (await owner.PersonaAsync()).Id)).Value;

        Assert.Equal(["Jun", "Mira"], (await owner.Sessions.ListCastAsync(session.Id)).Value.Select(c => c.Name));
        Assert.True((await other.Sessions.ListCastAsync(session.Id)).IsFailure);
    }

    [Fact]
    public async Task Only_a_member_of_the_chatbots_cast_can_be_brought_in()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var session = await owner.ChatAsync();
        var outsider = await owner.CharacterAsync("Jun");
        var theirs = await other.CharacterAsync("Thorne");

        Assert.True((await owner.Sessions.BringInAsync(session.Id, outsider.Id)).IsFailure);
        Assert.True((await owner.Sessions.BringInAsync(session.Id, theirs.Id)).IsFailure);
        Assert.True((await other.Sessions.BringInAsync(session.Id, theirs.Id)).IsFailure);
        Assert.Single((await owner.Sessions.GetSceneAsync(session.Id)).Value.Scene.PresentCharacterIds);
    }

    [Fact]
    public async Task Moving_the_scene_sets_the_place_the_next_reply_is_written_in()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        var client = new FakeChatClient(["*They step out onto the pier.*"]);
        var turns = user.Turns(new FakeChatClientFactory(client));
        var hint = new DirectorHint.MoveTo("  The harbour  ");

        Assert.True((await turns.DirectAsync(session.Id, hint)).IsSuccess);
        Assert.True((await turns.StreamReplyAsync(session.Id, _ => { }, hint)).IsSuccess);

        Assert.Equal("The harbour", (await user.Sessions.GetSceneAsync(session.Id)).Value.Scene.Location);
        Assert.Contains("Location: The harbour", client.LastMessages![0].Text);
    }

    [Fact]
    public async Task A_chat_without_a_model_asks_for_one_instead_of_streaming()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        await user.Sessions.SetModelAsync(session.Id, null);
        var turns = user.Turns(new FakeChatClientFactory(new FakeChatClient(["Hi"])));

        var result = await turns.StreamReplyAsync(session.Id, _ => { });

        Assert.True(result.IsFailure);
        Assert.False(result.IsCancelled);
    }
}
