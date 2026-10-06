using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RoleplayStudio.AI.Memory;

/// <summary>Memory upkeep for one chat; it runs with no signed-in user, so it carries the owner it works for.</summary>
public sealed record MemoryJob(string OwnerId, Guid SessionId);

public sealed class MemoryQueue(ILogger<MemoryQueue> logger)
{
    private const int Capacity = 256;

    private readonly Channel<MemoryJob> _jobs = Channel.CreateBounded<MemoryJob>(new BoundedChannelOptions(Capacity) { SingleReader = true });

    public ChannelReader<MemoryJob> Reader => _jobs.Reader;

    public void Enqueue(MemoryJob job)
    {
        if (!_jobs.Writer.TryWrite(job))
        {
            // Dropping is safe: the next turn's job picks up everything this one would have.
            logger.LogWarning("The memory queue is full; skipped upkeep for chat {SessionId}", job.SessionId);
        }
    }
}
