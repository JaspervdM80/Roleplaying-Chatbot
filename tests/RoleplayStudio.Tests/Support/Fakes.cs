using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using RoleplayStudio.AI.Providers;
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

public sealed class FakeChatClientFactory(IChatClient client) : IChatClientFactory
{
    public Result<IChatClient> Create(ModelProfile profile) => Result.Success(client);
}
