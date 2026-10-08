using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using OpenAI;
using RoleplayStudio.AI.Images;
using IImageGenerator = RoleplayStudio.AI.Images.IImageGenerator;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

public interface IChatClientFactory
{
    Result<IChatClient> Create(ModelProfile profile);

    Result<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGenerator(ModelProfile profile);

    Result<IImageGenerator> CreateImageGenerator(ModelProfile profile);
}

public sealed class ChatClientFactory(IConfiguration configuration) : IChatClientFactory
{
    // Not IHttpClientFactory: the service defaults put a 10-second resilience timeout on every client, far shorter than a generation.
    internal static readonly SocketsHttpHandler Handler = new() { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    public Result<IChatClient> Create(ModelProfile profile)
    {
        if (profile.FindProblem() is { } problem)
        {
            return Result.Failure<IChatClient>(problem);
        }

        return profile.Provider switch
        {
            ProviderKind.OpenAICompatible => CreateOpenAICompatible(profile),
            ProviderKind.Ollama => CreateOllama(profile),
            _ => Result.Failure<IChatClient>("{0} models cannot chat", profile.Provider),
        };
    }

    public Result<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGenerator(ModelProfile profile)
    {
        if (profile.FindProblem() is { } problem)
        {
            return Result.Failure<IEmbeddingGenerator<string, Embedding<float>>>(problem);
        }

        switch (profile.Provider)
        {
            case ProviderKind.OpenAICompatible:
                var connection = OpenAIConnection(profile);
                return connection.IsSuccess
                    ? Result.Success(new OpenAI.Embeddings.EmbeddingClient(profile.ModelId, connection.Value.Key, connection.Value.Options).AsIEmbeddingGenerator())
                    : connection.To<IEmbeddingGenerator<string, Embedding<float>>>();
            case ProviderKind.Ollama:
                return Result.Success<IEmbeddingGenerator<string, Embedding<float>>>(new OllamaApiClient(OllamaHttp(profile), profile.ModelId));
            default:
                return Result.Failure<IEmbeddingGenerator<string, Embedding<float>>>("{0} models cannot embed text", profile.Provider);
        }
    }

    public Result<IImageGenerator> CreateImageGenerator(ModelProfile profile)
    {
        if (profile.FindProblem() is { } problem)
        {
            return Result.Failure<IImageGenerator>(problem);
        }

        if (profile.Provider != ProviderKind.Runware)
        {
            return Result.Failure<IImageGenerator>("{0} models cannot draw images", profile.Provider);
        }

        var baseUrl = profile.Address!;
        if (profile.ApiKeySetting is null)
        {
            return Result.Failure<IImageGenerator>("Runware needs an API key setting, such as Providers:Runware:ApiKey");
        }

        var key = ApiKey(profile.ApiKeySetting, baseUrl);
        return key.IsSuccess
            ? Result.Success<IImageGenerator>(new RunwareImageGenerator(new HttpClient(Handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) }, new Uri(baseUrl), key.Value, profile.ModelId))
            : key.To<IImageGenerator>();
    }

    public static ChatOptions OptionsFor(ModelProfile profile) => new()
    {
        Temperature = (float?)profile.Temperature,
        MaxOutputTokens = profile.MaxOutputTokens,
    };

    private Result<IChatClient> CreateOpenAICompatible(ModelProfile profile)
    {
        var connection = OpenAIConnection(profile);
        return connection.IsSuccess
            ? Result.Success(new OpenAI.Chat.ChatClient(profile.ModelId, connection.Value.Key, connection.Value.Options).AsIChatClient())
            : connection.To<IChatClient>();
    }

    private Result<(ApiKeyCredential Key, OpenAIClientOptions Options)> OpenAIConnection(ModelProfile profile)
    {
        // Local OpenAI-compatible servers take no key, but the SDK refuses an empty credential.
        var key = profile.ApiKeySetting is null ? Result.Success("none") : ApiKey(profile.ApiKeySetting, profile.BaseUrl!);
        if (key.IsFailure)
        {
            return key.To<(ApiKeyCredential, OpenAIClientOptions)>();
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(profile.BaseUrl!),
            Transport = new HttpClientPipelineTransport(new HttpClient(Handler, disposeHandler: false)),
        };
        return Result.Success((new ApiKeyCredential(key.Value), options));
    }

    private Result<string> ApiKey(string setting, string baseUrl)
    {
        if (configuration[setting] is not { Length: > 0 } configured)
        {
            return Result.Failure<string>("The setting {0} has no value; add the API key with dotnet user-secrets", setting);
        }

        // Keys are shared by every user of the app, so one may only travel to the host configured beside it.
        var hostSetting = KeyHostSetting(setting);
        return SameHost(configuration[hostSetting], baseUrl)
            ? Result.Success(configured)
            : Result.Failure<string>("The key in {0} may only be sent to the address in {1}", setting, hostSetting);
    }

    private static string KeyHostSetting(string apiKeySetting) => $"{apiKeySetting[..apiKeySetting.LastIndexOf(':')]}:BaseUrl";

    private static bool SameHost(string? configuredUrl, string profileUrl) =>
        Uri.TryCreate(configuredUrl, UriKind.Absolute, out var configured)
        && Uri.TryCreate(profileUrl, UriKind.Absolute, out var requested)
        && configured.Scheme == requested.Scheme
        && string.Equals(configured.Authority, requested.Authority, StringComparison.OrdinalIgnoreCase);

    private static HttpClient OllamaHttp(ModelProfile profile) => new(Handler, disposeHandler: false)
    {
        BaseAddress = new Uri(profile.Address!),
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static Result<IChatClient> CreateOllama(ModelProfile profile)
    {
        // Thinking models (qwen3) otherwise reason at length before every reply, and the stream shows nothing meanwhile.
        IChatClient ollama = new OllamaApiClient(OllamaHttp(profile), profile.ModelId);
        var client = ollama
            .AsBuilder()
            .ConfigureOptions(options => options.RawRepresentationFactory ??= _ => new ChatRequest { Think = false })
            .Build();
        return Result.Success(client);
    }
}
