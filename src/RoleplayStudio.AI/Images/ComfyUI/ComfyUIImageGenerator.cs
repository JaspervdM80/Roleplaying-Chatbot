using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoleplayStudio.AI.Images.ComfyUI;

/// <summary>
/// Draws through a ComfyUI server: uploads the reference portraits, queues the workflow the picture calls for, waits for
/// its history entry and downloads what the <c>output</c> node made. The profile's model id names the workflow set.
/// </summary>
public sealed class ComfyUIImageGenerator(HttpClient http, Uri address, ComfyUIWorkflowSet workflows) : IImageGenerator
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Uri _address = address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");

    public string ModelId => workflows.Name;

    public Task<ImageModelTraits> TraitsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImageModelTraits(workflows.TakesNegativePrompt, TakesSeed: true, workflows.MaxReferences, []));

    public async Task<GeneratedPicture> GenerateAsync(ImageRequest request, CancellationToken cancellationToken = default)
    {
        var references = new List<string>();
        foreach (var reference in (request.References ?? []).Take(workflows.MaxReferences))
        {
            references.Add(await UploadAsync(reference, cancellationToken));
        }

        var template = workflows.For(request.IsPortrait, references.Count);
        var graph = ComfyUIWorkflows.Fill(template, request.Prompt, request.NegativePrompt, request.Width, request.Height, request.Seed, references);
        var promptId = await QueueAsync(graph, cancellationToken);
        try
        {
            var picture = await AwaitPictureAsync(promptId, cancellationToken);
            return new GeneratedPicture(picture.Data, picture.ContentType, request.Seed, request.Width, request.Height);
        }
        catch (OperationCanceledException)
        {
            await AbandonAsync(promptId);
            throw;
        }
    }

    private async Task<string> UploadAsync(ReferenceImage reference, CancellationToken cancellationToken)
    {
        // Named by content, so the same portrait is stored once however many pictures it is sent with.
        var extension = reference.ContentType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            _ => "webp",
        };
        var name = $"roleplay-studio-{Convert.ToHexStringLower(SHA256.HashData(reference.Data))[..32]}.{extension}";
        using var file = new ByteArrayContent(reference.Data);
        file.Headers.ContentType = new MediaTypeHeaderValue(reference.ContentType);
        using var form = new MultipartFormDataContent { { file, "image", name }, { new StringContent("true"), "overwrite" } };
        using var response = await http.PostAsync(new Uri(_address, "upload/image"), form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var uploaded = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return uploaded?["name"]?.GetValue<string>() ?? name;
    }

    private async Task<string> QueueAsync(JsonObject graph, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["prompt"] = graph, ["client_id"] = "roleplay-studio" };
        using var response = await http.PostAsJsonAsync(new Uri(_address, "prompt"), body, cancellationToken);
        var answer = await ReadObjectAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw (Exception?)Refusal(answer) ?? new HttpRequestException($"ComfyUI answered with status {(int)response.StatusCode}", null, response.StatusCode);
        }

        return answer?["prompt_id"]?.GetValue<string>() ?? throw new ImageProviderException("noPromptId");
    }

    private async Task<(byte[] Data, string ContentType)> AwaitPictureAsync(string promptId, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var response = await http.GetAsync(new Uri(_address, $"history/{Uri.EscapeDataString(promptId)}"), cancellationToken);
            response.EnsureSuccessStatusCode();
            var history = await ReadObjectAsync(response, cancellationToken);
            if (history?[promptId] is JsonObject entry)
            {
                if (entry["status"]?["status_str"]?.GetValue<string>() == "error")
                {
                    throw ExecutionError(entry);
                }

                if (entry["outputs"]?[ComfyUIWorkflowSet.OutputNode]?["images"] is JsonArray { Count: > 0 } images)
                {
                    return await DownloadAsync(images[0]!.AsObject(), cancellationToken);
                }

                if (entry["status"]?["completed"]?.GetValue<bool>() == true)
                {
                    throw new ImageProviderException("noImage", ComfyUIWorkflowSet.OutputNode);
                }
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<(byte[] Data, string ContentType)> DownloadAsync(JsonObject image, CancellationToken cancellationToken)
    {
        var query = string.Join("&", new[] { "filename", "subfolder", "type" }.Select(k => $"{k}={Uri.EscapeDataString(image[k]?.GetValue<string>() ?? "")}"));
        using var response = await http.GetAsync(new Uri(_address, $"view?{query}"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (data, response.Content.Headers.ContentType?.MediaType is { } type && type.StartsWith("image/", StringComparison.Ordinal) ? type : "image/png");
    }

    /// <summary>Takes a picture nobody waits for any more off the queue, or stops it drawing; the server being gone is no reason to fail.</summary>
    private async Task AbandonAsync(string promptId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var deleted = await http.PostAsJsonAsync(new Uri(_address, "queue"), new { delete = new[] { promptId } }, timeout.Token);
            using var interrupted = await http.PostAsJsonAsync(new Uri(_address, "interrupt"), new { prompt_id = promptId }, timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
        }
    }

    private static ImageProviderException? Refusal(JsonObject? answer)
    {
        if (answer?["node_errors"] is JsonObject { Count: > 0 } nodes && nodes.First().Value is JsonObject node)
        {
            var detail = node["errors"]?[0];
            return new ImageProviderException(
                answer["error"]?["type"]?.GetValue<string>() ?? "invalidWorkflow",
                $"{node["class_type"]?.GetValue<string>()}: {detail?["message"]?.GetValue<string>()} {detail?["details"]?.GetValue<string>()}".Trim());
        }

        return answer?["error"]?["type"]?.GetValue<string>() is { } type
            ? new ImageProviderException(type, answer["error"]?["message"]?.GetValue<string>())
            : null;
    }

    private static ImageProviderException ExecutionError(JsonObject entry)
    {
        var failure = entry["status"]?["messages"]?.AsArray()
            .Select(m => m?.AsArray())
            .FirstOrDefault(m => m?[0]?.GetValue<string>() == "execution_error")?[1];
        return new ImageProviderException(
            "executionError",
            failure is null ? null : $"{failure["node_type"]?.GetValue<string>()}: {failure["exception_message"]?.GetValue<string>()?.Trim()}");
    }

    private static async Task<JsonObject?> ReadObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
