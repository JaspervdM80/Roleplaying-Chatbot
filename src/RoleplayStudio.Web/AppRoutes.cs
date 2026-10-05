namespace RoleplayStudio.Web;

public static class AppRoutes
{
    public const string Home = "/";
    public const string Models = "/models";
    public const string Playground = "/playground";

    public static string PlaygroundWith(Guid profileId) => $"{Playground}?profile={profileId}";
}
