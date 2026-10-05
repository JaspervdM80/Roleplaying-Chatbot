namespace RoleplayStudio.Domain;

public static class TextFields
{
    /// <summary>Trims the text, turning blank into null so an empty field never reaches a prompt.</summary>
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>"Label: value" lines for the fields that have a value, or null when none do.</summary>
    public static string? Describe(params (string Label, string? Value)[] fields)
    {
        var lines = fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => $"{f.Label}: {f.Value!.Trim()}").ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
