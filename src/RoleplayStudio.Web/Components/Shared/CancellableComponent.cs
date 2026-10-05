using Microsoft.AspNetCore.Components;

namespace RoleplayStudio.Web.Components.Shared;

public abstract class CancellableComponent : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>Cancelled when the user leaves the page; for reads and generation, never for saving what the user wrote.</summary>
    protected CancellationToken Cancellation => _cancellation.Token;

    // Cancelled but never disposed: code still running after the user leaves must be able to read the token.
    public virtual void Dispose()
    {
        _cancellation.Cancel();
        GC.SuppressFinalize(this);
    }
}
