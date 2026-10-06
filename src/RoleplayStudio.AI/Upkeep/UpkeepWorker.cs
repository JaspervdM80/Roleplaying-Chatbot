using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Memory;
using RoleplayStudio.AI.Scene;

namespace RoleplayStudio.AI.Upkeep;

public sealed class UpkeepWorker(UpkeepQueue queue, SceneUpkeep scene, MemoryUpkeep memory, ILogger<UpkeepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            // The scene goes first: the next turn's prompt needs it sooner than it needs new memories.
            if (!await RunAsync("Scene tracking", job, () => scene.RunAsync(job, stoppingToken), stoppingToken)
                || !await RunAsync("Memory upkeep", job, () => memory.RunAsync(job, stoppingToken), stoppingToken))
            {
                return;
            }
        }
    }

    /// <summary>False only when the app is stopping; any other failure is logged and the next step still runs.</summary>
    private async Task<bool> RunAsync(string step, UpkeepJob job, Func<Task> run, CancellationToken stoppingToken)
    {
        try
        {
            await run();
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Step} failed for chat {SessionId}", step, job.SessionId);
            return true;
        }
    }
}
