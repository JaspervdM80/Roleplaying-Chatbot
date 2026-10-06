namespace RoleplayStudio.AI.Scene;

/// <summary>Announces that a chat's scene changed in the background; a handler gets only the session id and reloads through its own owner.</summary>
public sealed class SceneNotifier
{
    public event Action<Guid>? Changed;

    public void Notify(Guid sessionId) => Changed?.Invoke(sessionId);
}
