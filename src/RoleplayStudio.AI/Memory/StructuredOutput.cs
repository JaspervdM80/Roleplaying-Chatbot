using System.Text.Json;
using System.Text.RegularExpressions;
using RoleplayStudio.Domain;

namespace RoleplayStudio.AI.Memory;

public static partial class StructuredOutput
{
    /// <summary>The outermost JSON object in a model reply, ignoring code fences and chatter around it; null when there is none.</summary>
    public static JsonDocument? ParseObject(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(reply.AsMemory(start, end - start + 1), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A string property of an object, cleaned and cut to <paramref name="maxLength"/>; null when it is missing, blank or a placeholder.</summary>
    public static string? Text(JsonElement element, string property, int maxLength = int.MaxValue) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? Text(value.GetString(), maxLength)
            : null;

    /// <summary>The text cleaned and cut to <paramref name="maxLength"/>; null when it is blank or a placeholder.</summary>
    public static string? Text(string? text, int maxLength = int.MaxValue) =>
        TextFields.Clean(text) is { } clean && !Placeholder().IsMatch(clean)
            ? clean.Length > maxLength ? clean[..maxLength] : clean
            : null;

    // A model that copies the answer template back writes its "..." as a value.
    [GeneratedRegex(@"^[.…]+$")]
    private static partial Regex Placeholder();
}
