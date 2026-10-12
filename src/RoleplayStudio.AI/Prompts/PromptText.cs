using System.Text;
using RoleplayStudio.Domain;

namespace RoleplayStudio.AI.Prompts;

/// <summary>Pieces of prompt text; a blank value leaves its piece out rather than sending an empty label to the model.</summary>
public static class PromptText
{
    public static string? Labelled(string label, object? value) =>
        TextFields.Clean(value?.ToString()) is { } text ? $"{label}: {text}" : null;

    public static string? OneLine(string? text, string separator = "; ") => TextFields.Clean(text)?.ReplaceLineEndings(separator);

    /// <summary>The parts on lines of their own, or null when there are none.</summary>
    public static string? Lines(params string?[] parts)
    {
        var present = parts.Select(TextFields.Clean).OfType<string>().ToList();
        return present.Count == 0 ? null : string.Join("\n", present);
    }

    /// <summary>The parts as sentences on one line, each ending in one full stop.</summary>
    public static string Sentences(params string?[] parts) =>
        string.Join(" ", parts.Select(TextFields.Clean).OfType<string>().Select(Sentence));

    /// <summary>A blank line, the heading and the body; nothing when the body is blank.</summary>
    public static void Section(StringBuilder text, string heading, string? body)
    {
        if (TextFields.Clean(body) is { } clean)
        {
            text.AppendLine().AppendLine(heading).AppendLine(clean);
        }
    }

    private static string Sentence(string text)
    {
        var trimmed = text.TrimEnd('.');
        return trimmed.EndsWith('!') || trimmed.EndsWith('?') ? trimmed : $"{trimmed}.";
    }
}
