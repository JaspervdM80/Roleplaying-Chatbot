using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Upkeep;

/// <summary>The owner's preferred utility model, opened for one background job.</summary>
public sealed class UtilityModel(ModelProfile profile, IChatClient client, ILogger logger) : IDisposable
{
    /// <summary>Null when the owner has no utility model or it cannot be turned into a client; the job is then skipped.</summary>
    public static async Task<UtilityModel?> OpenAsync(ApplicationDbContext db, IChatClientFactory clients, ILogger logger, CancellationToken cancellationToken)
    {
        var profile = await db.ModelProfiles.PreferredFor(ModelRole.Utility).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            logger.LogDebug("No utility model; skipped upkeep");
            return null;
        }

        var created = clients.Create(profile);
        if (created.IsFailure)
        {
            logger.LogWarning("Utility model profile {ProfileId} cannot be turned into a client", profile.Id);
            return null;
        }

        return new UtilityModel(profile, created.Value, logger);
    }

    public Task<Result<string>> AskAsync(IReadOnlyList<ChatMessage> prompt, CancellationToken cancellationToken) =>
        ProviderErrors.TranslateAsync(profile, logger, async () =>
            Result.Success((await client.GetResponseAsync(prompt, ChatClientFactory.OptionsFor(profile), cancellationToken)).Text));

    public void Dispose() => client.Dispose();
}
