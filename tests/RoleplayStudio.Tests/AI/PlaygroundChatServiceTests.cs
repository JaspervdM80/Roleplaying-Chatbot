using System.ClientModel;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using RoleplayStudio.AI.Playground;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.AI;

[Collection(PostgresCollection.Name)]
public class PlaygroundChatServiceTests(PostgresFixture postgres)
{
    private static readonly PlaygroundCharacter Mira = new("Mira", "A cheerful innkeeper.");
    private static readonly IReadOnlyList<PlaygroundTurn> Hello = [new(true, "Hello!")];

    private async Task<(PlaygroundChatService Chat, Guid ProfileId)> ArrangeAsync(FakeChatClient client, ICurrentUser? caller = null)
    {
        var owner = FakeCurrentUser.NewUser();
        var profiles = new ModelProfileService(postgres.DbFactory, owner, NullLogger<ModelProfileService>.Instance);
        var profile = await profiles.CreateAsync(new ModelProfile { Name = "Local", Provider = ProviderKind.Ollama, ModelId = "mistral-nemo" });

        var callerProfiles = caller is null ? profiles : new ModelProfileService(postgres.DbFactory, caller, NullLogger<ModelProfileService>.Instance);
        var chat = new PlaygroundChatService(callerProfiles, new FakeChatClientFactory(client), NullLogger<PlaygroundChatService>.Instance);
        return (chat, profile.Value.Id);
    }

    [Fact]
    public async Task A_reply_streams_into_the_callback_in_order()
    {
        var client = new FakeChatClient(["*waves* ", "Welcome to ", "the inn!"]);
        var (chat, profileId) = await ArrangeAsync(client);
        var received = new List<string>();

        var result = await chat.StreamReplyAsync(profileId, Mira, Hello, received.Add);

        Assert.True(result.IsSuccess);
        Assert.Equal(["*waves* ", "Welcome to ", "the inn!"], received);
        Assert.Equal(ChatRole.System, client.LastMessages![0].Role);
        Assert.Equal("Hello!", client.LastMessages[^1].Text);
    }

    [Fact]
    public async Task Stopping_a_reply_keeps_what_already_arrived_and_reports_cancellation()
    {
        var (chat, profileId) = await ArrangeAsync(new FakeChatClient(["one ", "two ", "three"]));
        using var stop = new CancellationTokenSource();
        var received = new List<string>();

        var result = await chat.StreamReplyAsync(profileId, Mira, Hello, text =>
        {
            received.Add(text);
            stop.Cancel();
        }, stop.Token);

        Assert.True(result.IsCancelled);
        Assert.Equal(["one "], received);
    }

    [Fact]
    public async Task A_rejected_api_key_becomes_a_readable_failure()
    {
        var unauthorized = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        var (chat, profileId) = await ArrangeAsync(new FakeChatClient([], unauthorized));

        var result = await chat.StreamReplyAsync(profileId, Mira, Hello, _ => { });

        Assert.True(result.IsFailure);
        Assert.Contains("rejected the API key", result.Error);
    }

    [Fact]
    public async Task An_unreachable_openai_compatible_host_says_so()
    {
        // The OpenAI SDK reports a transport failure as a ClientResultException without a status.
        var unreachable = new ClientResultException("Connection refused", null, new HttpRequestException("Connection refused"));
        var (chat, profileId) = await ArrangeAsync(new FakeChatClient([], unreachable));

        var result = await chat.StreamReplyAsync(profileId, Mira, Hello, _ => { });

        Assert.True(result.IsFailure);
        Assert.Contains("Could not reach", result.Error);
    }

    [Fact]
    public async Task Another_users_model_profile_cannot_be_used()
    {
        var client = new FakeChatClient(["should never stream"]);
        var (chat, profileId) = await ArrangeAsync(client, caller: FakeCurrentUser.NewUser());

        var result = await chat.StreamReplyAsync(profileId, Mira, Hello, _ => { });

        Assert.True(result.IsFailure);
        Assert.Null(client.LastMessages);
    }
}
