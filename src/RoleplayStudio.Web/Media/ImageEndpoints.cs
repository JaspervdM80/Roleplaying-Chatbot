using Microsoft.AspNetCore.Components.Authorization;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.Web.Media;

public static class ImageEndpoints
{
    public static string Url(Guid imageId) => $"/images/{imageId}";

    public static IEndpointRouteBuilder MapImageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/images/{id:guid}", async (Guid id, HttpContext http, AuthenticationStateProvider authentication, ImageService images, CancellationToken cancellationToken) =>
        {
            // Outside a component nothing hands the request's user to the provider that ICurrentUser reads.
            if (authentication is IHostEnvironmentAuthenticationStateProvider host)
            {
                host.SetAuthenticationState(Task.FromResult(new AuthenticationState(http.User)));
            }

            var opened = await images.OpenAsync(id, cancellationToken);
            if (opened.IsFailure)
            {
                return Results.NotFound();
            }

            // Revalidated each time, so a signed-out browser cannot show a cached picture without the owner check.
            http.Response.Headers.CacheControl = "private, no-cache";
            return Results.Stream(opened.Value.Content, opened.Value.ContentType);
        }).RequireAuthorization();

        return endpoints;
    }
}
