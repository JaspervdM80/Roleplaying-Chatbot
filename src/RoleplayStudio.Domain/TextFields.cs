namespace RoleplayStudio.Domain;

public static class TextFields
{
    /// <summary>Trims the text, turning blank into null so an empty field never reaches a prompt.</summary>
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
