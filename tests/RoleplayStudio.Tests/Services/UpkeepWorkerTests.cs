using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class UpkeepWorkerTests(PostgresFixture postgres)
{
    private sealed class ThrowsFirstFactory(IChatClientFactory inner) : IChatClientFactory
    {
        private int _calls;

        public Result<IChatClient> Create(ModelProfile profile) =>
            Interlocked.Increment(ref _calls) == 1 ? throw new InvalidOperationException("Provider exploded") : inner.Create(profile);

        public Result<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGenerator(ModelProfile profile) => inner.CreateEmbeddingGenerator(profile);

        public Result<RoleplayStudio.AI.Images.IImageGenerator> CreateImageGenerator(ModelProfile profile) => inner.CreateImageGenerator(profile);
    }

    private async Task<string> SummaryAsync(StudioUser user, Guid sessionId)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        return await db.ChatSessions.Where(s => s.Id == sessionId).Select(s => s.Summary.Text).SingleAsync();
    }

    [Fact]
    public async Task A_step_that_throws_does_not_stop_the_next_step_or_the_next_job()
    {
        var user = new StudioUser(postgres);
        var first = await user.ChatAsync("Mira");
        var second = await user.ChatAsync("Jun");
        await user.UtilityModelAsync();
        foreach (var session in new[] { first, second })
        {
            for (var i = 0; i < 10; i++)
            {
                Assert.True((await user.Sessions.AddUserMessageAsync(session.Id, new string('x', 1_500))).IsSuccess);
                Assert.True((await user.Sessions.AddReplyAsync(session.Id, new string('y', 1_500))).IsSuccess);
            }
        }

        var clients = new ThrowsFirstFactory(new FakeChatClientFactory(new ScriptedChatClient(_ => "Sam came in for coffee.")));
        using var worker = new UpkeepWorker(user.UpkeepQueue, user.SceneUpkeep(clients), user.Upkeep(clients), NullLogger<UpkeepWorker>.Instance);
        await worker.StartAsync(default);
        user.UpkeepQueue.Enqueue(new UpkeepJob(user.User.UserId!, first.Id));
        user.UpkeepQueue.Enqueue(new UpkeepJob(user.User.UserId!, second.Id));

        // The first job's scene step throws; its memory step and the second job must still run.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await SummaryAsync(user, first.Id) == "" || await SummaryAsync(user, second.Id) == "") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await worker.StopAsync(default);
        Assert.Equal("Sam came in for coffee.", await SummaryAsync(user, first.Id));
        Assert.Equal("Sam came in for coffee.", await SummaryAsync(user, second.Id));
    }
}
