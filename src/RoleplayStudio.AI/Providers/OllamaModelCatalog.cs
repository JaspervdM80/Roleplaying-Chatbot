using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Services;

namespace RoleplayStudio.AI.Providers;

public sealed record OllamaModel(string Name, string? ParameterSize, IReadOnlyList<string> Capabilities);

public sealed class OllamaModelCatalog(ILogger<OllamaModelCatalog> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public Task<Result<IReadOnlyList<OllamaModel>>> ListAsync(string? baseUrl, ModelRole role, CancellationToken cancellationToken = default) =>
        ServiceOperation.RunAsync(logger, "list the Ollama models", cancellationToken, async () =>
        {
            var url = baseUrl ?? ModelProfile.DefaultOllamaUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                return Result.Failure<IReadOnlyList<OllamaModel>>("The base URL must be an http or https address");
            }

            using var http = new HttpClient(ChatClientFactory.Handler, disposeHandler: false) { BaseAddress = uri, Timeout = Timeout };
            TagsResponse? tags;
            try
            {
                tags = await http.GetFromJsonAsync<TagsResponse>("api/tags", cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Ollama at {BaseUrl} could not list its models: {ExceptionType}", url, exception.GetType().Name);
                return Result.Failure<IReadOnlyList<OllamaModel>>("Could not reach Ollama at {0}", url);
            }

            var wanted = role == ModelRole.Embedding ? "embedding" : "completion";
            IReadOnlyList<OllamaModel> models = (tags?.Models ?? [])
                .Where(model => model.Capabilities is null || model.Capabilities.Contains(wanted))
                .Select(model => new OllamaModel(model.Name, model.Details?.ParameterSize, model.Capabilities ?? []))
                .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Result.Success(models);
        });

    private sealed record TagsResponse(List<TagsModel>? Models);

    private sealed record TagsModel(string Name, TagsDetails? Details, List<string>? Capabilities);

    private sealed record TagsDetails([property: JsonPropertyName("parameter_size")] string? ParameterSize);
}
