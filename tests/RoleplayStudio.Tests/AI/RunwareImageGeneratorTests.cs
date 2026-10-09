using System.Net;
using System.Text;
using System.Text.Json;
using RoleplayStudio.AI.Images;

namespace RoleplayStudio.Tests.AI;

public class RunwareImageGeneratorTests
{
    private const string Key = "rw-test-key";
    private const string Endpoint = "https://api.runware.ai/v1";
    private const string Diffusion = "runware:101@1";
    private const string Flux = "bfl:flux@3-image";
    private const string Community = "civitai:4201@130072";

    private const string Index = $$"""
        [{"id":"diffusion","air":"{{Diffusion}}","schema":"https://runware.ai/docs/models/diffusion/schema.json"},
         {"id":"flux","air":"{{Flux}}","schema":"https://runware.ai/docs/models/flux/schema.json"},
         {"id":"elsewhere","air":"other:1@1","schema":"https://example.com/schema.json"}]
        """;

    // Shaped like the schemas Runware publishes: the task is the item of RequestBody, fixed sizes are rules under allOf.
    private const string DiffusionSchema = """
        {"components":{"schemas":{"RequestBody":{"type":"array","items":{"type":"object",
          "properties":{"model":{},"positivePrompt":{},"negativePrompt":{},"seed":{},"width":{},"height":{}}}}}}}
        """;

    private const string FluxSchema = """
        {"components":{"schemas":{"RequestBody":{"type":"array","items":{"type":"object",
          "properties":{"model":{},"positivePrompt":{},"width":{},"height":{},
            "inputs":{"type":"object","properties":{"referenceImages":{"type":"array","maxItems":2}}}},
          "allOf":[{"dependentRequired":{"width":["height"]}},
            {"if":{"anyOf":[{"required":["width"]}]},"then":{"oneOf":[
              {"title":"1:1","properties":{"width":{"const":1024},"height":{"const":1024}}},
              {"title":"16:9","properties":{"width":{"const":1344},"height":{"const":752}}},
              {"title":"3:4","properties":{"width":{"const":880},"height":{"const":1184}}}]}}]}}}}}
        """;

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Dictionary<string, string> Documents { get; } = new()
        {
            ["/docs/models/index.json"] = Index,
            ["/docs/models/diffusion/schema.json"] = DiffusionSchema,
            ["/docs/models/flux/schema.json"] = FluxSchema,
        };

        public bool DocumentsDown { get; set; }
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public List<Uri> Fetched { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "runware.ai")
            {
                Fetched.Add(request.RequestUri);
                return DocumentsDown || !Documents.TryGetValue(request.RequestUri.AbsolutePath, out var document)
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(document, Encoding.UTF8, "application/json") };
            }

            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static RunwareImageGenerator Generator(StubHandler handler, string model = Diffusion) =>
        new(new HttpClient(handler), new Uri(Endpoint), Key, model, new RunwareModelCatalog(new HttpClient(handler)));

    private static ImageRequest Request(params ReferenceImage[] references) => new("A lighthouse at dusk", "blurry", 832, 1216, 42, references);

    private static string Drawn(byte[] data) => $$"""{"data":[{"taskType":"imageInference","imageBase64Data":"{{Convert.ToBase64String(data)}}","seed":42}]}""";

    private static JsonElement SentTask(StubHandler handler) => Assert.Single(JsonDocument.Parse(handler.RequestBody!).RootElement.EnumerateArray());

    [Fact]
    public async Task A_request_is_one_inference_task_sent_with_the_key_as_a_bearer_token()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1, 2, 3]));

        await Generator(handler).GenerateAsync(Request());

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization?.Scheme);
        Assert.Equal(Key, handler.Request.Headers.Authorization?.Parameter);
        var task = SentTask(handler);
        Assert.Equal("imageInference", task.GetProperty("taskType").GetString());
        Assert.Equal(Diffusion, task.GetProperty("model").GetString());
        Assert.Equal("A lighthouse at dusk", task.GetProperty("positivePrompt").GetString());
        Assert.Equal("blurry", task.GetProperty("negativePrompt").GetString());
        Assert.Equal(832, task.GetProperty("width").GetInt32());
        Assert.Equal(1216, task.GetProperty("height").GetInt32());
        Assert.Equal(42, task.GetProperty("seed").GetInt64());
        Assert.Equal("base64Data", task.GetProperty("outputType").GetString());
        Assert.True(Guid.TryParse(task.GetProperty("taskUUID").GetString(), out _));
        Assert.False(task.TryGetProperty("inputs", out _));
    }

    [Fact]
    public async Task The_picture_is_decoded_from_the_response_with_the_seed_and_size_used()
    {
        var picture = await Generator(new StubHandler(HttpStatusCode.OK, Drawn([1, 2, 3]))).GenerateAsync(Request());

        Assert.Equal([1, 2, 3], picture.Data);
        Assert.Equal("image/webp", picture.ContentType);
        Assert.Equal((42L, 832, 1216), (picture.Seed!.Value, picture.Width, picture.Height));
    }

    [Fact]
    public async Task A_model_whose_schema_declares_no_negative_prompt_or_seed_is_sent_neither()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1]));

        var picture = await Generator(handler, Flux).GenerateAsync(Request());

        var task = SentTask(handler);
        Assert.False(task.TryGetProperty("negativePrompt", out _));
        Assert.False(task.TryGetProperty("seed", out _));
        Assert.Null(picture.Seed);
    }

    [Fact]
    public async Task Reference_images_travel_as_data_uris_inside_inputs_up_to_the_models_limit()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1]));

        await Generator(handler, Flux).GenerateAsync(Request(new ReferenceImage([9, 9], "image/png"), new ReferenceImage([8], "image/webp"), new ReferenceImage([7], "image/png")));

        var task = SentTask(handler);
        Assert.False(task.TryGetProperty("referenceImages", out _));
        var sent = task.GetProperty("inputs").GetProperty("referenceImages").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Equal([$"data:image/png;base64,{Convert.ToBase64String([9, 9])}", $"data:image/webp;base64,{Convert.ToBase64String([8])}"], sent);
    }

    [Fact]
    public async Task A_model_that_takes_no_reference_images_is_sent_none()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1]));

        await Generator(handler).GenerateAsync(Request(new ReferenceImage([9], "image/png")));

        Assert.False(SentTask(handler).TryGetProperty("inputs", out _));
    }

    [Fact]
    public async Task A_size_the_model_cannot_draw_becomes_the_allowed_one_closest_in_shape()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1]));

        var picture = await Generator(handler, Flux).GenerateAsync(new ImageRequest("A harbour", null, 1344, 768, 7));

        var task = SentTask(handler);
        Assert.Equal((1344, 752), (task.GetProperty("width").GetInt32(), task.GetProperty("height").GetInt32()));
        Assert.Equal((1344, 752), (picture.Width, picture.Height));
    }

    [Fact]
    public void An_exact_shape_at_a_far_larger_size_loses_to_a_near_shape_at_the_size_asked_for()
    {
        var traits = ImageModelTraits.Unknown with { Sizes = [new(1776, 2368), new(656, 912), new(1024, 1024)] };

        Assert.Equal(new ImageSize(656, 912), traits.Fit(768, 1024));
    }

    [Fact]
    public async Task A_model_missing_from_the_index_is_sent_a_negative_prompt_and_seed_but_no_references()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1]));

        await Generator(handler, Community).GenerateAsync(Request(new ReferenceImage([9], "image/png")));

        var task = SentTask(handler);
        Assert.Equal(("blurry", 42L), (task.GetProperty("negativePrompt").GetString(), task.GetProperty("seed").GetInt64()));
        Assert.False(task.TryGetProperty("inputs", out _));
    }

    [Fact]
    public async Task A_schema_is_read_once_per_model_and_only_from_runwares_own_site()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1]));
        var catalog = new RunwareModelCatalog(new HttpClient(handler));

        await catalog.TraitsAsync(Flux, default);
        await catalog.TraitsAsync(Flux, default);
        var elsewhere = await catalog.TraitsAsync("other:1@1", default);

        Assert.Equal(["/docs/models/index.json", "/docs/models/flux/schema.json"], handler.Fetched.Select(u => u.AbsolutePath));
        Assert.Same(ImageModelTraits.Unknown, elsewhere);
    }

    [Fact]
    public async Task Documentation_that_cannot_be_read_now_is_tried_again_next_time()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Drawn([1])) { DocumentsDown = true };
        var catalog = new RunwareModelCatalog(new HttpClient(handler));

        var down = await catalog.TraitsAsync(Flux, default);
        handler.DocumentsDown = false;
        var up = await catalog.TraitsAsync(Flux, default);

        Assert.Same(ImageModelTraits.Unknown, down);
        Assert.Equal((false, false, 2), (up.TakesNegativePrompt, up.TakesSeed, up.MaxReferenceImages));
    }

    [Fact]
    public async Task A_refusal_names_the_providers_error_code_and_parameter()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"errors":[{"code":"invalidParameter","parameter":"width","message":"Unsupported width"}]}""");

        var refusal = await Assert.ThrowsAsync<ImageProviderException>(() => Generator(handler).GenerateAsync(Request()));

        Assert.Equal(("invalidParameter", "width"), (refusal.Code, refusal.Parameter));
    }

    [Fact]
    public async Task A_rejected_key_surfaces_as_its_http_status()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"errors":[{"code":"invalidApiKey"}]}""");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => Generator(handler).GenerateAsync(Request()));

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
    }
}
