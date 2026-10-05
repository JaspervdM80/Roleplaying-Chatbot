using Microsoft.EntityFrameworkCore;

namespace RoleplayStudio.Infrastructure.Data;

public static class OwnerScopedContexts
{
    public static async Task<ApplicationDbContext> CreateForOwnerAsync(this IDbContextFactory<ApplicationDbContext> factory, string ownerId, CancellationToken cancellationToken = default) =>
        (await factory.CreateDbContextAsync(cancellationToken)).ScopeToOwner(ownerId);
}
