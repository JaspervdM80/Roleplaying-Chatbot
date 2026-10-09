using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace RoleplayStudio.AI.Images;

/// <summary>
/// Reads what a Runware model accepts from the schema Runware publishes for it, since a model refuses any parameter it does not declare.
/// A model missing from the index (a community checkpoint) is <see cref="ImageModelTraits.Unknown"/>.
/// </summary>
public sealed class RunwareModelCatalog(HttpClient http)
{
    public static readonly Uri IndexAddress = new("https://runware.ai/docs/models/index.json");

    private readonly ConcurrentDictionary<string, ImageModelTraits> _traits = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, Uri>? _schemas;

    /// <summary>The model's traits; <see cref="ImageModelTraits.Unknown"/> when the schema cannot be read now, which is not remembered.</summary>
    public async Task<ImageModelTraits> TraitsAsync(string modelId, CancellationToken cancellationToken)
    {
        if (_traits.TryGetValue(modelId, out var known))
        {
            return known;
        }

        try
        {
            _schemas ??= await ReadIndexAsync(cancellationToken);
            var traits = _schemas.TryGetValue(modelId, out var schema) ? Parse(await GetJsonAsync(schema, cancellationToken)) : ImageModelTraits.Unknown;
            return _traits[modelId] = traits;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ImageModelTraits.Unknown;
        }
    }

    /// <summary>The traits a model's schema document declares for one inference task.</summary>
    public static ImageModelTraits Parse(JsonElement schema)
    {
        var task = schema.GetProperty("components").GetProperty("schemas").GetProperty("RequestBody").GetProperty("items");
        var properties = task.GetProperty("properties");
        var references = properties.TryGetProperty("inputs", out var inputs)
            && inputs.TryGetProperty("properties", out var inputProperties)
            && inputProperties.TryGetProperty("referenceImages", out var referenceImages)
                ? referenceImages.TryGetProperty("maxItems", out var max) && max.TryGetInt32(out var count) ? count : int.MaxValue
                : 0;

        var sizes = new List<ImageSize>();
        if (task.TryGetProperty("allOf", out var rules))
        {
            CollectSizes(rules, sizes);
        }

        return new ImageModelTraits(properties.TryGetProperty("negativePrompt", out _), properties.TryGetProperty("seed", out _), references, sizes.Distinct().ToList());
    }

    private async Task<IReadOnlyDictionary<string, Uri>> ReadIndexAsync(CancellationToken cancellationToken)
    {
        var index = await GetJsonAsync(IndexAddress, cancellationToken);
        var schemas = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in index.EnumerateArray())
        {
            if (model.TryGetProperty("air", out var air) && air.ValueKind == JsonValueKind.String
                && model.TryGetProperty("schema", out var schema) && Uri.TryCreate(schema.GetString(), UriKind.Absolute, out var address)
                && address.Host == IndexAddress.Host)
            {
                schemas.TryAdd(air.GetString()!, address);
            }
        }

        return schemas;
    }

    private async Task<JsonElement> GetJsonAsync(Uri address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        // The documentation site refuses a request that names no client.
        request.Headers.UserAgent.ParseAdd("RoleplayStudio");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    // Fixed sizes appear as rules pairing a constant width with a constant height.
    private static void CollectSizes(JsonElement element, List<ImageSize> sizes)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectSizes(item, sizes);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("properties", out var properties)
            && Constant(properties, "width") is { } width
            && Constant(properties, "height") is { } height)
        {
            sizes.Add(new ImageSize(width, height));
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            CollectSizes(property.Value, sizes);
        }
    }

    private static int? Constant(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var property) && property.TryGetProperty("const", out var value) && value.TryGetInt32(out var number) ? number : null;
}
