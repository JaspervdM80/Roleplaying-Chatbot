using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RoleplayStudio.AI.Memory;

public sealed class MemoryWorker(MemoryQueue queue, MemoryUpkeep upkeep, ILogger<MemoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await upkeep.RunAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Memory upkeep failed for chat {SessionId}", job.SessionId);
            }
        }
    }
}
