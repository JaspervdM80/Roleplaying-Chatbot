using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Memory;

public sealed class MemoryEmbeddings(IChatClientFactory clients, ILogger<MemoryEmbeddings> logger)
{
    /// <summary>
    /// One vector per text with the owner's embedding model, in order. A text gets null when there is no usable
    /// embedding model, the provider fails, or the model's vectors are not <see cref="MemoryEntry.EmbeddingDimensions"/> long.
    /// </summary>
    public async Task<IReadOnlyList<Vector?>> EmbedAsync(ApplicationDbContext db, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var none = texts.Select(_ => (Vector?)null).ToList();
        if (texts.Count == 0)
        {
            return none;
        }

        var opened = await PreferredModel.OpenAsync(db, ModelRole.Embedding, clients.CreateEmbeddingGenerator, logger, cancellationToken);
        if (opened.IsFailure)
        {
            return none;
        }

        var (profile, client) = opened.Value;
        using var generator = client;
        var generated = await ProviderErrors.TranslateAsync(profile, logger, async () =>
            Result.Success(await generator.GenerateAsync(texts, cancellationToken: cancellationToken)));
        if (generated.IsFailure)
        {
            return none;
        }

        var embeddings = generated.Value;
        if (embeddings.Count != texts.Count || embeddings.Any(e => e.Vector.Length != MemoryEntry.EmbeddingDimensions))
        {
            logger.LogWarning(
                "Embedding model profile {ProfileId} returned vectors of {Dimensions} dimensions; memories need {Expected}",
                profile.Id,
                embeddings.FirstOrDefault()?.Vector.Length,
                MemoryEntry.EmbeddingDimensions);
            return none;
        }

        return embeddings.Select(e => (Vector?)new Vector(e.Vector)).ToList();
    }
}
