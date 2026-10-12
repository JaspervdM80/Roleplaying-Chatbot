using Microsoft.Extensions.Configuration;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

internal sealed class ProviderKeys(IConfiguration configuration)
{
    public Result<string> Find(string setting, string baseUrl)
    {
        if (configuration[setting] is not { Length: > 0 } configured)
        {
            return Result.Failure<string>("The setting {0} has no value; add the API key with dotnet user-secrets", setting);
        }

        // Keys are shared by every user of the app, so one may only travel to the host configured beside it.
        var hostSetting = HostSetting(setting);
        return SameHost(configuration[hostSetting], baseUrl)
            ? Result.Success(configured)
            : Result.Failure<string>("The key in {0} may only be sent to the address in {1}", setting, hostSetting);
    }

    private static string HostSetting(string apiKeySetting) => $"{apiKeySetting[..apiKeySetting.LastIndexOf(':')]}:BaseUrl";

    private static bool SameHost(string? configuredUrl, string profileUrl) =>
        Uri.TryCreate(configuredUrl, UriKind.Absolute, out var configured)
        && Uri.TryCreate(profileUrl, UriKind.Absolute, out var requested)
        && configured.Scheme == requested.Scheme
        && string.Equals(configured.Authority, requested.Authority, StringComparison.OrdinalIgnoreCase);
}
