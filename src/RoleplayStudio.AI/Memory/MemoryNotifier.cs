namespace RoleplayStudio.AI.Memory;

/// <summary>Announces that a chat's memories or summary changed in the background; a handler gets only the session id and reloads through its own owner.</summary>
public sealed class MemoryNotifier
{
    /// <summary>The session id, and whether this is the end of a summary rebuild, finished or given up.</summary>
    public event Action<Guid, bool>? Changed;

    public void Notify(Guid sessionId, bool rebuildEnded = false) => Changed?.Invoke(sessionId, rebuildEnded);
}
