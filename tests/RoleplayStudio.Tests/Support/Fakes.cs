using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Images;
using IImageGenerator = RoleplayStudio.AI.Images.IImageGenerator;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Memory;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.Tests.Support;

public sealed class FakeCurrentUser(string? userId) : ICurrentUser
{
    public static FakeCurrentUser NewUser() => new(Guid.NewGuid().ToString());

    public string? UserId { get; } = userId;

    public Task<string?> GetUserIdAsync() => Task.FromResult(UserId);
}

/// <summary>Streams canned chunks, then optionally throws, and records what it was sent.</summary>
public sealed class FakeChatClient(IReadOnlyList<string> chunks, Exception? failAfterChunks = null) : IChatClient
{
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        return failAfterChunks is null
            ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(chunks))))
            : Task.FromException<ChatResponse>(failAfterChunks);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }

        if (failAfterChunks is not null)
        {
            throw failAfterChunks;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>Answers each request with whatever <paramref name="answer"/> makes of the prompt.</summary>
public sealed class ScriptedChatClient(Func<IReadOnlyList<ChatMessage>, string> answer) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer(messages.ToList()))));

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>Embeds each text as the vector <paramref name="embed"/> picks for it, such as one of the <see cref="Axis"/> vectors.</summary>
public sealed class FakeEmbeddingGenerator(Func<string, float[]> embed) : IEmbeddingGenerator<string, Embedding<float>>
{
    public static float[] Axis(int index, int dimensions = MemoryEntry.EmbeddingDimensions)
    {
        var vector = new float[dimensions];
        vector[index] = 1;
        return vector;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(embed(v)))));

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>Draws a one-pixel PNG for every request and records what it was asked for.</summary>
public sealed class FakeImageGenerator : IImageGenerator
{
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    public ImageRequest? LastRequest { get; private set; }

    public string ModelId => "fake-diffusion";

    public Task<GeneratedPicture> GenerateAsync(ImageRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new GeneratedPicture(Png, "image/png", request.Seed));
    }
}

public sealed class FakeChatClientFactory(IChatClient client, IEmbeddingGenerator<string, Embedding<float>>? embeddings = null, IImageGenerator? images = null) : IChatClientFactory
{
    public Result<IChatClient> Create(ModelProfile profile) => Result.Success(client);

    public Result<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGenerator(ModelProfile profile) =>
        embeddings is null ? Result.Failure<IEmbeddingGenerator<string, Embedding<float>>>("No embeddings in this test") : Result.Success(embeddings);

    public Result<IImageGenerator> CreateImageGenerator(ModelProfile profile) =>
        images is null ? Result.Failure<IImageGenerator>("No images in this test") : Result.Success(images);
}
