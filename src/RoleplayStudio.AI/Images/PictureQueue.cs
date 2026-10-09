using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace RoleplayStudio.AI.Images;

/// <summary>
/// A background picture carrying its owner: of the chat at <see cref="SessionId"/> (of <see cref="CharacterId"/> when set),
/// else <see cref="CharacterId"/>'s portrait, which <see cref="RenewsReference"/> makes their new reference.
/// </summary>
public sealed record PictureJob(string OwnerId, Guid? SessionId, Guid? MessageId, Guid? CharacterId, Guid? RedrawOf = null, bool RenewsReference = false)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>A picture still being drawn, as a page shows its placeholder; the caption arrives once the prompt is written.</summary>
public sealed record PendingPicture(Guid Id, Guid? SessionId, Guid? MessageId, Guid? CharacterId, string? Caption);

/// <summary>Pictures waiting to be drawn, and those being drawn, so a page can show and cancel them.</summary>
public sealed class PictureQueue(ILogger<PictureQueue> logger)
{
    private const int Capacity = 64;

    private readonly Channel<PictureJob> _jobs = Channel.CreateBounded<PictureJob>(new BoundedChannelOptions(Capacity) { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, Waiting> _pending = new();
    private long _order;

    public ChannelReader<PictureJob> Reader => _jobs.Reader;

    /// <summary>False when the queue is full and the job was dropped.</summary>
    public bool Enqueue(PictureJob job)
    {
        var waiting = new Waiting(job, Interlocked.Increment(ref _order));
        _pending[job.Id] = waiting;
        if (_jobs.Writer.TryWrite(job))
        {
            return true;
        }

        Finish(job.Id);
        logger.LogWarning("The picture queue is full; skipped a picture for owner {OwnerId}", job.OwnerId);
        return false;
    }

    public IReadOnlyList<PendingPicture> PendingFor(string ownerId) =>
        _pending.Values
            .Where(w => w.Job.OwnerId == ownerId)
            .OrderBy(w => w.Order)
            .Select(w => new PendingPicture(w.Job.Id, w.Job.SessionId, w.Job.MessageId, w.Job.CharacterId, w.Caption))
            .ToList();

    /// <summary>False when the owner has no such picture waiting or being drawn.</summary>
    public bool Cancel(string ownerId, Guid id)
    {
        if (!_pending.TryGetValue(id, out var waiting) || waiting.Job.OwnerId != ownerId)
        {
            return false;
        }

        waiting.Stop.Cancel();
        _pending.TryRemove(id, out _);
        return true;
    }

    /// <summary>The token that stops this job when its owner cancels it; already cancelled when the job is gone.</summary>
    public CancellationToken StopTokenFor(Guid id) => _pending.TryGetValue(id, out var waiting) ? waiting.Stop.Token : new CancellationToken(canceled: true);

    public void Describe(Guid id, string? caption)
    {
        if (_pending.TryGetValue(id, out var waiting))
        {
            waiting.Caption = caption;
        }
    }

    // The token source is left undisposed: a cancel racing the job's end must not throw.
    public void Finish(Guid id) => _pending.TryRemove(id, out _);

    private sealed class Waiting(PictureJob job, long order)
    {
        public PictureJob Job { get; } = job;
        public long Order { get; } = order;
        public CancellationTokenSource Stop { get; } = new();
        public string? Caption { get; set; }
    }
}
