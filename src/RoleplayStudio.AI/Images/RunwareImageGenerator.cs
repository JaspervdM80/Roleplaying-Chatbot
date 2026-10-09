using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoleplayStudio.AI.Images;

/// <summary>A refusal the provider explained with an error code, such as an unknown model or a size it cannot make.</summary>
public sealed class ImageProviderException(string code, string? parameter = null) : Exception($"The image provider refused the request: {code}")
{
    public string Code { get; } = code;

    /// <summary>The request parameter the refusal names, if any.</summary>
    public string? Parameter { get; } = parameter;
}

public sealed class RunwareImageGenerator(HttpClient http, Uri endpoint, string apiKey, string modelId, RunwareModelCatalog catalog) : IImageGenerator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ModelId => modelId;

    public Task<ImageModelTraits> TraitsAsync(CancellationToken cancellationToken = default) => catalog.TraitsAsync(modelId, cancellationToken);

    public async Task<GeneratedPicture> GenerateAsync(ImageRequest request, CancellationToken cancellationToken = default)
    {
        var traits = await TraitsAsync(cancellationToken);
        var size = traits.Fit(request.Width, request.Height);
        var references = (request.References ?? []).Take(traits.MaxReferenceImages).Select(r => $"data:{r.ContentType};base64,{Convert.ToBase64String(r.Data)}").ToList();
        var task = new InferenceTask(
            TaskUUID: Guid.NewGuid(),
            Model: modelId,
            PositivePrompt: request.Prompt,
            NegativePrompt: traits.TakesNegativePrompt && !string.IsNullOrWhiteSpace(request.NegativePrompt) ? request.NegativePrompt : null,
            Width: size.Width,
            Height: size.Height,
            Seed: traits.TakesSeed ? request.Seed : null,
            Inputs: references.Count > 0 ? new InferenceInputs(references) : null);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(new[] { task }, options: Json) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(message, cancellationToken);

        var body = await ReadBodyAsync(response, cancellationToken);
        if (body?.Errors is [var error, ..] && response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest)
        {
            throw new ImageProviderException(error.Code ?? "unknown", error.Parameter);
        }

        response.EnsureSuccessStatusCode();
        if (body?.Data is not [{ ImageBase64Data: { Length: > 0 } image } result, ..])
        {
            throw new ImageProviderException("noImage");
        }

        return new GeneratedPicture(Convert.FromBase64String(image), "image/webp", task.Seed is null ? null : result.Seed ?? task.Seed, size.Width, size.Height);
    }

    private static async Task<RunwareResponse?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<RunwareResponse>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record InferenceTask(
        Guid TaskUUID,
        string Model,
        string PositivePrompt,
        string? NegativePrompt,
        int Width,
        int Height,
        long? Seed,
        InferenceInputs? Inputs)
    {
        public string TaskType => "imageInference";
        public int NumberResults => 1;
        public string OutputType => "base64Data";
        public string OutputFormat => "WEBP";
    }

    private sealed record InferenceInputs(IReadOnlyList<string> ReferenceImages);

    private sealed record RunwareResponse(List<RunwareImage>? Data, List<RunwareError>? Errors);

    private sealed record RunwareImage(string? ImageBase64Data, long? Seed);

    private sealed record RunwareError(string? Code, string? Parameter);
}
