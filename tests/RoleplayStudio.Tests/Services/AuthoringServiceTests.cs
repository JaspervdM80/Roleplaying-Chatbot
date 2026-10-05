using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class AuthoringServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_user_only_lists_their_own_personas_characters_and_chatbots()
    {
        var alice = new StudioUser(postgres);
        var bob = new StudioUser(postgres);
        await alice.PersonaAsync("Alice");
        await alice.ChatbotAsync(await alice.CharacterAsync("Mira"));
        await bob.PersonaAsync("Bob");
        await bob.ChatbotAsync(await bob.CharacterAsync("Jun"));

        Assert.Equal(["Alice"], (await alice.Personas.ListAsync()).Value.Select(p => p.Name));
        Assert.Equal(["Mira"], (await alice.Characters.ListAsync()).Value.Select(c => c.Name));
        var chatbot = Assert.Single((await alice.Chatbots.ListAsync()).Value);
        Assert.Equal(["Mira"], chatbot.Cast.Select(m => m.Character.Name));
    }

    [Fact]
    public async Task Another_users_character_cannot_join_a_cast()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var chatbot = await owner.ChatbotAsync();
        var theirs = await other.CharacterAsync("Not yours");

        var result = await owner.Chatbots.SetCastAsync(chatbot.Id, [new CastMember(theirs.Id, null)]);

        Assert.True(result.IsFailure);
        Assert.Empty((await owner.Chatbots.GetAsync(chatbot.Id)).Value.Cast);
    }

    [Fact]
    public async Task Another_users_chatbot_and_scenarios_read_as_not_found()
    {
        var owner = new StudioUser(postgres);
        var intruder = new StudioUser(postgres);
        var chatbot = await owner.ChatbotAsync();
        var scenario = await owner.ScenarioAsync(chatbot);

        Assert.True((await intruder.Chatbots.GetAsync(chatbot.Id)).IsFailure);
        Assert.True((await intruder.Chatbots.UpdateScenarioAsync(chatbot.Id, new Scenario { Id = scenario.Id, Title = "Hijacked" })).IsFailure);
        Assert.True((await intruder.Chatbots.DeleteScenarioAsync(chatbot.Id, scenario.Id)).IsFailure);
        Assert.True((await intruder.Chatbots.DeleteAsync(chatbot.Id)).IsFailure);
        Assert.True((await intruder.Chatbots.UpdateAsync(new Chatbot { Id = chatbot.Id, Name = "Hijacked" })).IsFailure);
        Assert.True((await intruder.Chatbots.AddScenarioAsync(chatbot.Id, new Scenario { Title = "Planted" })).IsFailure);
        Assert.True((await intruder.Chatbots.SetCastAsync(chatbot.Id, [new CastMember((await intruder.CharacterAsync("Planted")).Id, null)])).IsFailure);

        var unchanged = (await owner.Chatbots.GetAsync(chatbot.Id)).Value;
        Assert.Equal("Seaside Café", unchanged.Name);
        Assert.Empty(unchanged.Cast);
        Assert.Equal(["Morning rush"], unchanged.Scenarios.Select(s => s.Title));
    }

    [Fact]
    public async Task Another_users_persona_and_character_cannot_be_edited_or_deleted()
    {
        var owner = new StudioUser(postgres);
        var intruder = new StudioUser(postgres);
        var persona = await owner.PersonaAsync("Private");
        var character = await owner.CharacterAsync("Private");

        Assert.True((await intruder.Personas.UpdateAsync(new Persona { Id = persona.Id, Name = "Hijacked" })).IsFailure);
        Assert.True((await intruder.Personas.DeleteAsync(persona.Id)).IsFailure);
        Assert.True((await intruder.Characters.UpdateAsync(new Character { Id = character.Id, Name = "Hijacked" })).IsFailure);
        Assert.True((await intruder.Characters.DeleteAsync(character.Id)).IsFailure);

        Assert.Equal(["Private"], (await owner.Personas.ListAsync()).Value.Select(p => p.Name));
        Assert.Equal(["Private"], (await owner.Characters.ListAsync()).Value.Select(c => c.Name));
    }

    [Fact]
    public async Task A_scenario_of_one_chatbot_cannot_be_edited_through_another()
    {
        var user = new StudioUser(postgres);
        var first = await user.ChatbotAsync();
        var second = await user.ChatbotAsync();
        var scenario = await user.ScenarioAsync(first);

        var result = await user.Chatbots.UpdateScenarioAsync(second.Id, new Scenario { Id = scenario.Id, Title = "Moved" });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_scenario_cannot_start_with_a_character_outside_the_cast()
    {
        var user = new StudioUser(postgres);
        var outsider = await user.CharacterAsync("Outsider");
        var chatbot = await user.ChatbotAsync(await user.CharacterAsync("Mira"));

        var result = await user.Chatbots.AddScenarioAsync(chatbot.Id, new Scenario { Title = "Gatecrash", StartingCharacterIds = [outsider.Id] });

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Leaving_the_cast_also_leaves_every_starting_line_up()
    {
        var user = new StudioUser(postgres);
        var mira = await user.CharacterAsync("Mira");
        var jun = await user.CharacterAsync("Jun");
        var chatbot = await user.ChatbotAsync(mira, jun);
        await user.ScenarioAsync(chatbot, starting: [mira, jun]);

        await user.Chatbots.SetCastAsync(chatbot.Id, [new CastMember(mira.Id, "Owner")]);

        var saved = (await user.Chatbots.GetAsync(chatbot.Id)).Value;
        Assert.Equal("Owner", Assert.Single(saved.Cast).Role);
        Assert.Equal([mira.Id], Assert.Single(saved.Scenarios).StartingCharacterIds);
    }

    [Fact]
    public async Task Deleting_a_character_removes_them_from_casts_and_starting_line_ups()
    {
        var user = new StudioUser(postgres);
        var mira = await user.CharacterAsync("Mira");
        var jun = await user.CharacterAsync("Jun");
        var chatbot = await user.ChatbotAsync(mira, jun);
        await user.ScenarioAsync(chatbot, starting: [mira, jun]);

        Assert.True((await user.Characters.DeleteAsync(jun.Id)).IsSuccess);

        var saved = (await user.Chatbots.GetAsync(chatbot.Id)).Value;
        Assert.Equal([mira.Id], saved.Cast.Select(m => m.CharacterId));
        Assert.Equal([mira.Id], Assert.Single(saved.Scenarios).StartingCharacterIds);
    }

    [Fact]
    public async Task What_a_chat_still_uses_cannot_be_deleted()
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync();
        var loaded = (await user.Sessions.GetAsync(session.Id)).Value;
        var character = Assert.Single(loaded.CharacterStates).Character;

        Assert.True((await user.Personas.DeleteAsync(loaded.PersonaId)).IsFailure);
        Assert.True((await user.Characters.DeleteAsync(character.Id)).IsFailure);
        Assert.True((await user.Chatbots.DeleteScenarioAsync(loaded.Scenario.ChatbotId, loaded.ScenarioId)).IsFailure);
        Assert.True((await user.Chatbots.DeleteAsync(loaded.Scenario.ChatbotId)).IsFailure);

        Assert.True((await user.Sessions.DeleteAsync(session.Id)).IsSuccess);
        Assert.True((await user.Chatbots.DeleteAsync(loaded.Scenario.ChatbotId)).IsSuccess);
        Assert.True((await user.Characters.DeleteAsync(character.Id)).IsSuccess);
        Assert.True((await user.Personas.DeleteAsync(loaded.PersonaId)).IsSuccess);
    }

    [Fact]
    public async Task A_chatbot_cannot_default_to_another_users_model()
    {
        var owner = new StudioUser(postgres);
        var other = new StudioUser(postgres);
        var theirs = await other.ChatModelAsync();

        var result = await owner.Chatbots.CreateAsync(new Chatbot { Name = "Borrowed", DefaultChatModelProfileId = theirs.Id });

        Assert.True(result.IsFailure);
    }
}
