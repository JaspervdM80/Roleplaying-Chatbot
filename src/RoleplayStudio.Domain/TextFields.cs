namespace RoleplayStudio.Domain;

public static class TextFields
{
    /// <summary>Trims the text, turning blank into null so an empty field never reaches a prompt.</summary>
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The user's <paramref name="edited"/> text, or <paramref name="current"/> when they left what they opened alone.</summary>
    public static string? Edited(string? current, string? opened, string? edited) =>
        Clean(edited) is var cleaned && cleaned == Clean(opened) ? current : cleaned;
}
