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
        var opened = await PreferredModel.OpenAsync(db, ModelRole.Utility, clients.Create, logger, cancellationToken);
        return opened.IsSuccess ? new UtilityModel(opened.Value.Profile, opened.Value.Client, logger) : null;
    }

    public Task<Result<string>> AskAsync(IReadOnlyList<ChatMessage> prompt, CancellationToken cancellationToken) =>
        ProviderErrors.TranslateAsync(profile, logger, async () =>
            Result.Success((await client.GetResponseAsync(prompt, ChatClientFactory.OptionsFor(profile), cancellationToken)).Text));

    public void Dispose() => client.Dispose();
}
