using System.Text.Json;

namespace RoleplayStudio.AI.Memory;

public static class StructuredOutput
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
}
