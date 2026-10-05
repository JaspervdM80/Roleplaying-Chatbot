namespace RoleplayStudio.Infrastructure.Services;

public interface ICurrentUser
{
    /// <summary>The signed-in user's id, or null when nobody is signed in.</summary>
    Task<string?> GetUserIdAsync();
}
