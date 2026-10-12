using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

internal sealed class OpenAICompatibleProvider(ProviderKeys keys) : IModelProvider
{
    public ProviderKind Kind => ProviderKind.OpenAICompatible;

    Result<IChatClient> IModelProvider.CreateChatClient(ModelProfile profile)
    {
        var connection = Connect(profile);
        return connection.IsSuccess
            ? Result.Success(new OpenAI.Chat.ChatClient(profile.ModelId, connection.Value.Key, connection.Value.Options).AsIChatClient())
            : connection.To<IChatClient>();
    }

    Result<IEmbeddingGenerator<string, Embedding<float>>> IModelProvider.CreateEmbeddingGenerator(ModelProfile profile)
    {
        var connection = Connect(profile);
        return connection.IsSuccess
            ? Result.Success(new OpenAI.Embeddings.EmbeddingClient(profile.ModelId, connection.Value.Key, connection.Value.Options).AsIEmbeddingGenerator())
            : connection.To<IEmbeddingGenerator<string, Embedding<float>>>();
    }

    private Result<(ApiKeyCredential Key, OpenAIClientOptions Options)> Connect(ModelProfile profile)
    {
        // Local OpenAI-compatible servers take no key, but the SDK refuses an empty credential.
        var key = profile.ApiKeySetting is null ? Result.Success("none") : keys.Find(profile.ApiKeySetting, profile.BaseUrl!);
        if (key.IsFailure)
        {
            return key.To<(ApiKeyCredential, OpenAIClientOptions)>();
        }

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(profile.BaseUrl!),
            Transport = new HttpClientPipelineTransport(ProviderHttp.Client()),
        };
        return Result.Success((new ApiKeyCredential(key.Value), options));
    }
}
