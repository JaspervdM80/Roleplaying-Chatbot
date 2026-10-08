using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoleplayStudio.AI.Images;

/// <summary>A refusal the provider explained with an error code, such as an unknown model or a size it cannot make.</summary>
public sealed class ImageProviderException(string code) : Exception($"The image provider refused the request: {code}")
{
    public string Code { get; } = code;
}

public sealed class RunwareImageGenerator(HttpClient http, Uri endpoint, string apiKey, string modelId) : IImageGenerator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ModelId => modelId;

    public async Task<GeneratedPicture> GenerateAsync(ImageRequest request, CancellationToken cancellationToken = default)
    {
        var task = new InferenceTask(
            TaskUUID: Guid.NewGuid(),
            Model: modelId,
            PositivePrompt: request.Prompt,
            NegativePrompt: string.IsNullOrWhiteSpace(request.NegativePrompt) ? null : request.NegativePrompt,
            Width: request.Width,
            Height: request.Height,
            Seed: request.Seed,
            ReferenceImages: request.Reference is { } reference ? [$"data:{reference.ContentType};base64,{Convert.ToBase64String(reference.Data)}"] : null);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(new[] { task }, options: Json) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(message, cancellationToken);

        var body = await ReadBodyAsync(response, cancellationToken);
        if (body?.Errors is [var error, ..] && response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest)
        {
            throw new ImageProviderException(error.Code ?? "unknown");
        }

        response.EnsureSuccessStatusCode();
        if (body?.Data is not [{ ImageBase64Data: { Length: > 0 } image } result, ..])
        {
            throw new ImageProviderException("noImage");
        }

        return new GeneratedPicture(Convert.FromBase64String(image), "image/webp", result.Seed ?? request.Seed);
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
        long Seed,
        string[]? ReferenceImages)
    {
        public string TaskType => "imageInference";
        public int NumberResults => 1;
        public string OutputType => "base64Data";
        public string OutputFormat => "WEBP";
    }

    private sealed record RunwareResponse(List<RunwareImage>? Data, List<RunwareError>? Errors);

    private sealed record RunwareImage(string? ImageBase64Data, long? Seed);

    private sealed record RunwareError(string? Code);
}
