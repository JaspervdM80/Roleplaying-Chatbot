using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

public sealed record OpenedModel<T>(ModelProfile Profile, T Client);

public static class PreferredModel
{
    /// <summary>The owner's preferred profile for <paramref name="role"/> made into a client; fails with <paramref name="missing"/> when there is none.</summary>
    public static async Task<Result<OpenedModel<T>>> OpenAsync<T>(
        ApplicationDbContext db,
        ModelRole role,
        Func<ModelProfile, Result<T>> create,
        string missing,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var profile = await db.ModelProfiles.PreferredFor(role).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            logger.LogDebug("No {Role} model", role);
            return Result.Failure<OpenedModel<T>>(missing);
        }

        var created = create(profile);
        if (created.IsFailure)
        {
            logger.LogWarning("{Role} model profile {ProfileId} cannot be turned into a client", role, profile.Id);
            return created.To<OpenedModel<T>>();
        }

        return Result.Success(new OpenedModel<T>(profile, created.Value));
    }
}
