using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Images;

/// <summary>Draws queued pictures one at a time, apart from scene and memory upkeep so a slow drawing never holds a chat's upkeep back.</summary>
public sealed class PictureWorker(PictureQueue queue, PictureDrawing drawing, PictureNotifier notifier, ILogger<PictureWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            var outcome = await RunAsync(job, stoppingToken);
            queue.Finish(job.Id);
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                notifier.Notify(new PictureChange(job.Id, job.SessionId, job.CharacterId, outcome));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Announcing picture {JobId} failed", job.Id);
            }
        }
    }

    public async Task<Result<Guid>> RunAsync(PictureJob job, CancellationToken stoppingToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, queue.StopTokenFor(job.Id));
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            return await drawing.DrawAsync(job, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            logger.LogDebug("Cancelled picture {JobId}", job.Id);
            return Result.Cancelled().To<Guid>();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Drawing picture {JobId} failed", job.Id);
            return Result.Failure<Guid>("Could not draw the picture");
        }
    }
}
