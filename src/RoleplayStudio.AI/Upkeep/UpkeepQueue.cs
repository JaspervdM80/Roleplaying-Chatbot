using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RoleplayStudio.AI.Upkeep;

/// <summary>Scene and memory upkeep for one chat after a turn; it runs with no signed-in user, so it carries the owner it works for.</summary>
/// <param name="RebuildSummary">Throw the summary away first and write it again from the whole chat.</param>
public sealed record UpkeepJob(string OwnerId, Guid SessionId, bool RebuildSummary = false);

public sealed class UpkeepQueue(ILogger<UpkeepQueue> logger)
{
    private const int Capacity = 256;

    private readonly Channel<UpkeepJob> _jobs = Channel.CreateBounded<UpkeepJob>(new BoundedChannelOptions(Capacity) { SingleReader = true });

    public ChannelReader<UpkeepJob> Reader => _jobs.Reader;

    /// <summary>False when the queue is full and the job was dropped.</summary>
    public bool Enqueue(UpkeepJob job)
    {
        if (_jobs.Writer.TryWrite(job))
        {
            return true;
        }

        // A turn's job is safe to drop: the next turn's job picks up everything it would have.
        logger.LogWarning("The upkeep queue is full; skipped upkeep for chat {SessionId}", job.SessionId);
        return false;
    }
}
