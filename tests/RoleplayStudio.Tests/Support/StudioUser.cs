using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.AI.Images;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Scene;
using RoleplayStudio.AI.Upkeep;
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

    public UpkeepQueue UpkeepQueue { get; } = new(NullLogger<UpkeepQueue>.Instance);
    public PictureQueue PictureQueue { get; } = new(NullLogger<PictureQueue>.Instance);

    public MemoryRecall Recall(IChatClientFactory clients) =>
        new(postgres.DbFactory, User, new MemoryEmbeddings(clients, NullLogger<MemoryEmbeddings>.Instance), Time, NullLogger<MemoryRecall>.Instance);

    public MemoryUpkeep Upkeep(IChatClientFactory clients, MemoryNotifier? notifier = null) =>
        new(postgres.DbFactory, clients, new MemoryEmbeddings(clients, NullLogger<MemoryEmbeddings>.Instance), notifier ?? new MemoryNotifier(), Time, NullLogger<MemoryUpkeep>.Instance);

    public MemoryService Memories(IChatClientFactory clients) =>
        new(postgres.DbFactory, User, new MemoryEmbeddings(clients, NullLogger<MemoryEmbeddings>.Instance), UpkeepQueue, Time, NullLogger<MemoryService>.Instance);

    public SceneUpkeep SceneUpkeep(IChatClientFactory clients, SceneNotifier? notifier = null) =>
        new(postgres.DbFactory, clients, notifier ?? new SceneNotifier(), PictureQueue, Time, NullLogger<SceneUpkeep>.Instance);

    public ChatTurnService Turns(IChatClientFactory clients) =>
        new(Sessions, Profiles, clients, Recall(clients), UpkeepQueue, User, NullLogger<ChatTurnService>.Instance);

    public async Task<Persona> PersonaAsync(string name = "Sam") => (await Personas.CreateAsync(new Persona { Name = name })).Value;

    public async Task<Character> CharacterAsync(string name, string? outfit = null) =>
        (await Characters.CreateAsync(new Character { Name = name, Age = 25, DefaultOutfit = outfit })).Value;

    public async Task<ModelProfile> ChatModelAsync(bool isDefault = true) =>
        (await Profiles.CreateAsync(new ModelProfile { Name = "Local", Role = ModelRole.Chat, Provider = ProviderKind.Ollama, ModelId = "mistral-nemo", IsDefault = isDefault })).Value;

    public async Task<ModelProfile> UtilityModelAsync() =>
        (await Profiles.CreateAsync(new ModelProfile { Name = "Utility", Role = ModelRole.Utility, Provider = ProviderKind.Ollama, ModelId = "qwen3", IsDefault = true })).Value;

    public async Task<ModelProfile> ImageModelAsync() =>
        (await Profiles.CreateAsync(new ModelProfile { Name = "Runware", Role = ModelRole.Image, Provider = ProviderKind.Runware, ModelId = "runware:101@1", IsDefault = true })).Value;

    public async Task<ModelProfile> EmbeddingModelAsync() =>
        (await Profiles.CreateAsync(new ModelProfile { Name = "Embeddings", Role = ModelRole.Embedding, Provider = ProviderKind.Ollama, ModelId = "nomic-embed-text", IsDefault = true })).Value;

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
        var character = await CharacterAsync(characterName, outfit: "An apron");
        var chatbot = await ChatbotAsync(character);
        var scenario = await ScenarioAsync(chatbot);
        var persona = await PersonaAsync();
        var started = await Sessions.StartAsync(chatbot.Id, scenario.Id, persona.Id);
        Assert.True(started.IsSuccess, started.Error);
        return started.Value;
    }
}
