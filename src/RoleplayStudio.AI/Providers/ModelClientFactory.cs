using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using RoleplayStudio.AI.Images;
using IImageGenerator = RoleplayStudio.AI.Images.IImageGenerator;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

public interface IModelClientFactory
{
    Result<IChatClient> CreateChatClient(ModelProfile profile);

    Result<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGenerator(ModelProfile profile);

    Result<IImageGenerator> CreateImageGenerator(ModelProfile profile);
}

/// <summary>Builds model clients; with <paramref name="recordPromptText"/> every call's prompt and reply go into its trace, so only Development turns it on.</summary>
public sealed class ModelClientFactory(IConfiguration configuration, RunwareModelCatalog? runwareModels = null, bool recordPromptText = false) : IModelClientFactory
{
    public const string TelemetrySource = "RoleplayStudio.AI";

    private readonly Dictionary<ProviderKind, IModelProvider> _providers = Providers(new ProviderKeys(configuration), runwareModels ?? RunwareModelCatalog.Default)
        .ToDictionary(provider => provider.Kind);

    private static IEnumerable<IModelProvider> Providers(ProviderKeys keys, RunwareModelCatalog runwareModels) =>
    [
        new OpenAICompatibleProvider(keys),
        new OllamaProvider(),
        new RunwareProvider(keys, runwareModels),
        new ComfyUIProvider(),
    ];

    public Result<IChatClient> CreateChatClient(ModelProfile profile)
    {
        var client = Create(profile, provider => provider.CreateChatClient(profile));
        return client.IsSuccess ? Result.Success(Traced(client.Value)) : client;
    }

    public Result<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGenerator(ModelProfile profile) =>
        Create(profile, provider => provider.CreateEmbeddingGenerator(profile));

    public Result<IImageGenerator> CreateImageGenerator(ModelProfile profile) =>
        Create(profile, provider => provider.CreateImageGenerator(profile));

    private Result<T> Create<T>(ModelProfile profile, Func<IModelProvider, Result<T>> create)
    {
        if (profile.FindProblem() is { } problem)
        {
            return Result.Failure<T>(problem);
        }

        return create(_providers.GetValueOrDefault(profile.Provider) ?? new UnsupportedProvider(profile.Provider));
    }

    private IChatClient Traced(IChatClient client) =>
        client.AsBuilder().UseOpenTelemetry(sourceName: TelemetrySource, configure: c => c.EnableSensitiveData = recordPromptText).Build();

    private sealed record UnsupportedProvider(ProviderKind Kind) : IModelProvider;
}
