using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RoleplayStudio.AI.Upkeep;

/// <summary>Scene and memory upkeep for one chat after a turn; it runs with no signed-in user, so it carries the owner it works for.</summary>
public sealed record UpkeepJob(string OwnerId, Guid SessionId);

public sealed class UpkeepQueue(ILogger<UpkeepQueue> logger)
{
    private const int Capacity = 256;

    private readonly Channel<UpkeepJob> _jobs = Channel.CreateBounded<UpkeepJob>(new BoundedChannelOptions(Capacity) { SingleReader = true });

    public ChannelReader<UpkeepJob> Reader => _jobs.Reader;

    public void Enqueue(UpkeepJob job)
    {
        if (!_jobs.Writer.TryWrite(job))
        {
            // Dropping is safe: the next turn's job picks up everything this one would have.
            logger.LogWarning("The upkeep queue is full; skipped upkeep for chat {SessionId}", job.SessionId);
        }
    }
}
