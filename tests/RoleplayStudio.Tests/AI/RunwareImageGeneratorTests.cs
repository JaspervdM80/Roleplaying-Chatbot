using System.Net;
using System.Text;
using System.Text.Json;
using RoleplayStudio.AI.Images;

namespace RoleplayStudio.Tests.AI;

public class RunwareImageGeneratorTests
{
    private const string Key = "rw-test-key";

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static RunwareImageGenerator Generator(StubHandler handler) =>
        new(new HttpClient(handler), new Uri("https://api.runware.ai/v1"), Key, "runware:101@1");

    private static ImageRequest Request(ReferenceImage? reference = null) => new("A lighthouse at dusk", "blurry", 832, 1216, 42, reference);

    private static string Drawn(byte[] data) => $$"""{"data":[{"taskType":"imageInference","imageBase64Data":"{{Convert.ToBase64String(data)}}","seed":42}]}""";

    [Fact]
    public async Task A_request_is_one_inference_task_sent_with_the_key_as_a_bearer_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1, 2, 3]));

        await Generator(handler).GenerateAsync(Request());

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization?.Scheme);
        Assert.Equal(Key, handler.Request.Headers.Authorization?.Parameter);
        var task = Assert.Single(JsonDocument.Parse(handler.RequestBody!).RootElement.EnumerateArray());
        Assert.Equal("imageInference", task.GetProperty("taskType").GetString());
        Assert.Equal("runware:101@1", task.GetProperty("model").GetString());
        Assert.Equal("A lighthouse at dusk", task.GetProperty("positivePrompt").GetString());
        Assert.Equal("blurry", task.GetProperty("negativePrompt").GetString());
        Assert.Equal(832, task.GetProperty("width").GetInt32());
        Assert.Equal(1216, task.GetProperty("height").GetInt32());
        Assert.Equal(42, task.GetProperty("seed").GetInt64());
        Assert.Equal("base64Data", task.GetProperty("outputType").GetString());
        Assert.True(Guid.TryParse(task.GetProperty("taskUUID").GetString(), out _));
        Assert.False(task.TryGetProperty("referenceImages", out _));
    }

    [Fact]
    public async Task The_picture_is_decoded_from_the_response_with_the_seed_used()
    {
        var picture = await Generator(new StubHandler(HttpStatusCode.OK, Drawn([1, 2, 3]))).GenerateAsync(Request());

        Assert.Equal([1, 2, 3], picture.Data);
        Assert.Equal("image/webp", picture.ContentType);
        Assert.Equal(42, picture.Seed);
    }

    [Fact]
    public async Task A_reference_image_travels_as_a_data_uri()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1]));

        await Generator(handler).GenerateAsync(Request(new ReferenceImage([9, 9], "image/png")));

        var task = JsonDocument.Parse(handler.RequestBody!).RootElement[0];
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String([9, 9])}", task.GetProperty("referenceImages")[0].GetString());
    }

    [Fact]
    public async Task A_refusal_names_the_providers_error_code()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"errors":[{"code":"invalidModel","message":"Unknown model"}]}""");

        var refusal = await Assert.ThrowsAsync<ImageProviderException>(() => Generator(handler).GenerateAsync(Request()));

        Assert.Equal("invalidModel", refusal.Code);
    }

    [Fact]
    public async Task A_rejected_key_surfaces_as_its_http_status()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"errors":[{"code":"invalidApiKey"}]}""");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => Generator(handler).GenerateAsync(Request()));

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
    }
}
