using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.Tests.AI;

public sealed class OllamaModelCatalogTests : IDisposable
{
    private const string Tags = """
        {"models":[
          {"name":"qwen3:8b","details":{"parameter_size":"8.2B"},"capabilities":["completion","tools","thinking"]},
          {"name":"nomic-embed-text:latest","details":{"parameter_size":"137M"},"capabilities":["embedding"]},
          {"name":"Mistral-Nemo:latest","details":{}}
        ]}
        """;

    private readonly HttpListener _server = new();
    private readonly string _baseUrl;

    public OllamaModelCatalogTests()
    {
        _baseUrl = $"http://127.0.0.1:{FreePort()}/";
        _server.Prefixes.Add(_baseUrl);
        _server.Start();
        _ = ServeAsync();
    }

    private static OllamaModelCatalog Catalog() => new(NullLogger<OllamaModelCatalog>.Instance);

    [Fact]
    public async Task A_chat_profile_is_offered_only_the_models_that_can_chat()
    {
        var result = await Catalog().ListAsync(_baseUrl, ModelRole.Chat);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Mistral-Nemo:latest", "qwen3:8b"], result.Value.Select(model => model.Name));
        Assert.Equal("8.2B", result.Value.Single(model => model.Name == "qwen3:8b").ParameterSize);
    }

    [Fact]
    public async Task An_embedding_profile_is_offered_the_embedding_models()
    {
        var result = await Catalog().ListAsync(_baseUrl, ModelRole.Embedding);

        Assert.Equal(["Mistral-Nemo:latest", "nomic-embed-text:latest"], result.Value.Select(model => model.Name));
    }

    [Fact]
    public async Task An_unreachable_server_is_a_readable_failure()
    {
        var url = $"http://127.0.0.1:{FreePort()}";

        var result = await Catalog().ListAsync(url, ModelRole.Chat);

        Assert.True(result.IsFailure);
        Assert.Contains(url, result.Error);
    }

    public void Dispose() => _server.Close();

    private async Task ServeAsync()
    {
        while (_server.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _server.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            var body = Encoding.UTF8.GetBytes(context.Request.Url?.AbsolutePath == "/api/tags" ? Tags : "{}");
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
