using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.Web.Security;

public sealed class AuthenticationStateCurrentUser(AuthenticationStateProvider authenticationStateProvider) : ICurrentUser
{
    public async Task<string?> GetUserIdAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
