using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.Tests.AI;

public sealed class OllamaChatClientTests : IDisposable
{
    private const string Reply = """{"model":"qwen3:8b","message":{"role":"assistant","content":"OK"},"done":true,"done_reason":"stop"}""";

    private readonly HttpListener _server = new();
    private readonly string _baseUrl;
    private readonly TaskCompletionSource<string> _requestBody = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public OllamaChatClientTests()
    {
        _baseUrl = $"http://127.0.0.1:{FreePort()}/";
        _server.Prefixes.Add(_baseUrl);
        _server.Start();
        _ = ServeAsync();
    }

    private IChatClient Client()
    {
        var factory = new ChatClientFactory(new ConfigurationBuilder().Build());
        return factory.Create(new ModelProfile { Name = "Local", Provider = ProviderKind.Ollama, BaseUrl = _baseUrl, ModelId = "qwen3:8b" }).Value;
    }

    [Fact]
    public async Task An_ollama_chat_asks_the_model_not_to_think()
    {
        using var client = Client();

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hi")], new ChatOptions { Temperature = 0.5f });

        Assert.Equal("OK", response.Text);
        using var request = JsonDocument.Parse(await _requestBody.Task);
        Assert.False(request.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal(0.5, request.RootElement.GetProperty("options").GetProperty("temperature").GetDouble(), 3);
    }

    [Fact]
    public async Task An_ollama_stream_asks_the_model_not_to_think()
    {
        using var client = Client();

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]))
        {
        }

        using var request = JsonDocument.Parse(await _requestBody.Task);
        Assert.False(request.RootElement.GetProperty("think").GetBoolean());
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

            using (var reader = new StreamReader(context.Request.InputStream))
            {
                _requestBody.TrySetResult(await reader.ReadToEndAsync());
            }

            var body = Encoding.UTF8.GetBytes(Reply + "\n");
            context.Response.ContentType = "application/x-ndjson";
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
