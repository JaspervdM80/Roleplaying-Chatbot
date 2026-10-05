namespace RoleplayStudio.Web.Components.Shared;

public static class Wording
{
    public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
