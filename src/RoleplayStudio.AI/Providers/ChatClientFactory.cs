using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using OpenAI;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

public interface IChatClientFactory
{
    Result<IChatClient> Create(ModelProfile profile);
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

    public static ChatOptions OptionsFor(ModelProfile profile) => new()
    {
        Temperature = (float?)profile.Temperature,
        MaxOutputTokens = profile.MaxOutputTokens,
    };

    private Result<IChatClient> CreateOpenAICompatible(ModelProfile profile)
    {
        string apiKey;
        if (profile.ApiKeySetting is null)
        {
            // Local OpenAI-compatible servers take no key, but the SDK refuses an empty credential.
            apiKey = "none";
        }
        else if (configuration[profile.ApiKeySetting] is not { Length: > 0 } configured)
        {
            return Result.Failure<IChatClient>("The setting {0} has no value; add the API key with dotnet user-secrets", profile.ApiKeySetting);
        }
        else if (KeyHostSetting(profile.ApiKeySetting) is var hostSetting && !SameHost(configuration[hostSetting], profile.BaseUrl!))
        {
            // Keys are shared by every user of the app, so one may only travel to the host configured beside it.
            return Result.Failure<IChatClient>("The key in {0} may only be sent to the address in {1}", profile.ApiKeySetting, hostSetting);
        }
        else
        {
            apiKey = configured;
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(profile.BaseUrl!),
            Transport = new HttpClientPipelineTransport(new HttpClient(Handler, disposeHandler: false)),
        };
        var client = new OpenAI.Chat.ChatClient(profile.ModelId, new ApiKeyCredential(apiKey), options);
        return Result.Success(client.AsIChatClient());
    }

    private static string KeyHostSetting(string apiKeySetting) => $"{apiKeySetting[..apiKeySetting.LastIndexOf(':')]}:BaseUrl";

    private static bool SameHost(string? configuredUrl, string profileUrl) =>
        Uri.TryCreate(configuredUrl, UriKind.Absolute, out var configured)
        && Uri.TryCreate(profileUrl, UriKind.Absolute, out var requested)
        && configured.Scheme == requested.Scheme
        && string.Equals(configured.Authority, requested.Authority, StringComparison.OrdinalIgnoreCase);

    private static Result<IChatClient> CreateOllama(ModelProfile profile)
    {
        var http = new HttpClient(Handler, disposeHandler: false)
        {
            BaseAddress = new Uri(profile.BaseUrl ?? ModelProfile.DefaultOllamaUrl),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        // Thinking models (qwen3) otherwise reason at length before every reply, and the stream shows nothing meanwhile.
        IChatClient ollama = new OllamaApiClient(http, profile.ModelId);
        var client = ollama
            .AsBuilder()
            .ConfigureOptions(options => options.RawRepresentationFactory ??= _ => new ChatRequest { Think = false })
            .Build();
        return Result.Success(client);
    }
}
