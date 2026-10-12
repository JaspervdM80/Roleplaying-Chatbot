using Microsoft.Extensions.AI;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using IImageGenerator = RoleplayStudio.AI.Images.IImageGenerator;

namespace RoleplayStudio.AI.Providers;

/// <summary>Makes the clients one <see cref="ProviderKind"/> offers; what it does not override, it refuses.</summary>
internal interface IModelProvider
{
    ProviderKind Kind { get; }

    Result<IChatClient> CreateChatClient(ModelProfile profile) =>
        Result.Failure<IChatClient>("{0} models cannot chat", profile.Provider);

    Result<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGenerator(ModelProfile profile) =>
        Result.Failure<IEmbeddingGenerator<string, Embedding<float>>>("{0} models cannot embed text", profile.Provider);

    Result<IImageGenerator> CreateImageGenerator(ModelProfile profile) =>
        Result.Failure<IImageGenerator>("{0} models cannot draw images", profile.Provider);
}
