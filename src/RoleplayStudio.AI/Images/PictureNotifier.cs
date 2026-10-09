using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Images;

/// <summary>A pending picture changed: its prompt was written (<see cref="Outcome"/> null), or it is finished, failed or cancelled.</summary>
public sealed record PictureChange(Guid JobId, Guid? SessionId, Guid? CharacterId, Result<Guid>? Outcome);

/// <summary>Announces pictures drawn in the background; a handler matches its own chat or character and reloads through its own owner.</summary>
public sealed class PictureNotifier
{
    public event Action<PictureChange>? Changed;

    public void Notify(PictureChange change) => Changed?.Invoke(change);
}
