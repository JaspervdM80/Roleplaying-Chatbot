using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.Tests.Support;

/// <summary>One signed-in user with every authoring service, and builders for the graph a chat needs.</summary>
public sealed class StudioUser(PostgresFixture postgres)
{
    public FakeCurrentUser User { get; } = FakeCurrentUser.NewUser();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    public PersonaService Personas => new(postgres.DbFactory, User, NullLogger<PersonaService>.Instance);
    public CharacterService Characters => new(postgres.DbFactory, User, NullLogger<CharacterService>.Instance);
    public ChatbotService Chatbots => new(postgres.DbFactory, User, NullLogger<ChatbotService>.Instance);
    public ChatSessionService Sessions => new(postgres.DbFactory, User, Time, NullLogger<ChatSessionService>.Instance);
    public ModelProfileService Profiles => new(postgres.DbFactory, User, NullLogger<ModelProfileService>.Instance);

    public async Task<Persona> PersonaAsync(string name = "Sam") => (await Personas.CreateAsync(new Persona { Name = name })).Value;

    public async Task<Character> CharacterAsync(string name, string? top = null) =>
        (await Characters.CreateAsync(new Character { Name = name, Age = 25, DefaultOutfit = new Outfit { Top = top } })).Value;

    public async Task<ModelProfile> ChatModelAsync(bool isDefault = true) =>
        (await Profiles.CreateAsync(new ModelProfile { Name = "Local", Role = ModelRole.Chat, Provider = ProviderKind.Ollama, ModelId = "mistral-nemo", IsDefault = isDefault })).Value;

    public async Task<Chatbot> ChatbotAsync(params Character[] cast)
    {
        var chatbot = (await Chatbots.CreateAsync(new Chatbot { Name = "Seaside Café", WorldDescription = "A quiet harbour town." })).Value;
        if (cast.Length > 0)
        {
            Assert.True((await Chatbots.SetCastAsync(chatbot.Id, cast.Select(c => new CastMember(c.Id, null)).ToList())).IsSuccess);
        }

        return chatbot;
    }

    public async Task<Scenario> ScenarioAsync(Chatbot chatbot, string? opening = "The bell over the door rings.", params Character[] starting) =>
        (await Chatbots.AddScenarioAsync(chatbot.Id, new Scenario
        {
            Title = "Morning rush",
            StartingLocation = "Behind the counter",
            OpeningMessage = opening,
            StartingCharacterIds = starting.Select(c => c.Id).ToList(),
        })).Value;

    /// <summary>A started chat with one character present, so replies are theirs.</summary>
    public async Task<ChatSession> ChatAsync(string characterName = "Mira")
    {
        await ChatModelAsync();
        var character = await CharacterAsync(characterName, top: "apron");
        var chatbot = await ChatbotAsync(character);
        var scenario = await ScenarioAsync(chatbot);
        var persona = await PersonaAsync();
        var started = await Sessions.StartAsync(chatbot.Id, scenario.Id, persona.Id);
        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }
}
