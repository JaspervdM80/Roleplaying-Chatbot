using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

internal sealed class OllamaProvider : IModelProvider
{
    public ProviderKind Kind => ProviderKind.Ollama;

    Result<IChatClient> IModelProvider.CreateChatClient(ModelProfile profile)
    {
        // Thinking models (qwen3) otherwise reason at length before every reply, and the stream shows nothing meanwhile.
        IChatClient ollama = new OllamaApiClient(Http(profile), profile.ModelId);
        var client = ollama
            .AsBuilder()
            .ConfigureOptions(options => options.RawRepresentationFactory ??= _ => new ChatRequest { Think = false })
            .Build();
        return Result.Success(client);
    }

    Result<IEmbeddingGenerator<string, Embedding<float>>> IModelProvider.CreateEmbeddingGenerator(ModelProfile profile) =>
        Result.Success<IEmbeddingGenerator<string, Embedding<float>>>(new OllamaApiClient(Http(profile), profile.ModelId));

    private static HttpClient Http(ModelProfile profile) => ProviderHttp.Client(Timeout.InfiniteTimeSpan, new Uri(profile.Address!));
}
