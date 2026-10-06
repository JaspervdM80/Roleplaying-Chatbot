using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.Infrastructure.Data;

public static class ModelProfileQueries
{
    /// <summary>The caller's profiles for a role, the default first.</summary>
    public static IQueryable<ModelProfile> PreferredFor(this IQueryable<ModelProfile> profiles, ModelRole role) =>
        profiles.Where(p => p.Role == role).OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name);
}
