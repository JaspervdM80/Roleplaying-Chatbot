using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using RoleplayStudio.AI.Images;
using RoleplayStudio.AI.Images.ComfyUI;

namespace RoleplayStudio.Tests.AI;

public class ComfyUIImageGeneratorTests
{
    private const string Shipped = "flux-dev-nunchaku";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private sealed class StubComfyUI : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];
        public JsonObject? Queued { get; private set; }
        public string? PromptRefusal { get; set; }
        public string? History { get; set; }
        public int EmptyPolls { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((path + request.RequestUri.Query, body));
            return path switch
            {
                "/upload/image" => Json($$"""{"name":"{{Uploaded(body)}}","subfolder":"","type":"input"}"""),
                "/prompt" when PromptRefusal is not null => Json(PromptRefusal, HttpStatusCode.BadRequest),
                "/prompt" => Queue(body),
                "/history/p-1" when EmptyPolls-- > 0 => Json("{}"),
                "/history/p-1" => Json(History ?? """{"p-1":{"status":{"status_str":"success","completed":true},"outputs":{"output":{"images":[{"filename":"pic.png","subfolder":"","type":"temp"}]}}}}"""),
                "/view" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) { Headers = { ContentType = new("image/png") } } },
                _ => Json("{}"),
            };
        }

        private HttpResponseMessage Queue(string body)
        {
            Queued = JsonNode.Parse(body)!["prompt"]!.AsObject();
            return Json("""{"prompt_id":"p-1","number":1,"node_errors":{}}""");
        }

        private static string Uploaded(string multipart) => multipart.Split("filename=")[1].Split(['\r', '\n', ';'])[0].Trim('"');

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static ComfyUIImageGenerator Generator(StubComfyUI server) =>
        new(new HttpClient(server), new Uri("http://127.0.0.1:8188"), ComfyUIWorkflows.Find(Shipped)!);

    private static ImageRequest Request(bool isPortrait = false, params ReferenceImage[] references) =>
        new("A lighthouse at dusk", null, 1344, 768, 42, references, isPortrait);

    private static ReferenceImage Portrait(byte marker) => new([marker, 1, 2, 3], "image/png");

    [Fact]
    public void The_shipped_workflow_set_has_a_portrait_a_scene_and_three_reference_slots()
    {
        var set = ComfyUIWorkflows.Find(Shipped);

        Assert.NotNull(set);
        Assert.Equal(3, set.MaxReferences);
        Assert.False(set.TakesNegativePrompt);
    }

    [Fact]
    public void The_Juggernaut_XL_set_takes_a_negative_prompt_and_one_reference_portrait()
    {
        var set = ComfyUIWorkflows.Find("juggernaut-xl");

        Assert.NotNull(set);
        Assert.Equal(1, set.MaxReferences);
        Assert.True(set.TakesNegativePrompt);
    }

    [Fact]
    public void An_unused_slot_in_a_model_chain_hands_the_model_it_was_given_to_whatever_read_from_it()
    {
        var template = JsonNode.Parse("""
            {"checkpoint":{"class_type":"CheckpointLoaderSimple","inputs":{"ckpt_name":"x"}},
             "reference1_image":{"class_type":"LoadImage","inputs":{"image":"{{reference1}}"}},
             "reference1":{"class_type":"IPAdapterAdvanced","inputs":{"model":["checkpoint",0],"image":["reference1_image",0]}},
             "output":{"class_type":"KSampler","inputs":{"model":["reference1",0],"negative":"{{negative}}"}}}
            """)!.AsObject();

        var graph = ComfyUIWorkflows.Fill(template, "p", "blurry", 512, 512, 1, []);

        Assert.Equal(["checkpoint", "output"], graph.Select(n => n.Key));
        Assert.Equal("checkpoint", graph["output"]!["inputs"]!["model"]![0]!.GetValue<string>());
        Assert.Equal("blurry", graph["output"]!["inputs"]!["negative"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_scene_is_queued_with_the_prompt_size_and_seed_filled_in_and_the_picture_comes_back()
    {
        var server = new StubComfyUI { EmptyPolls = 1 };

        var picture = await Generator(server).GenerateAsync(Request());

        var graph = server.Queued!;
        Assert.Equal("A lighthouse at dusk", graph["positive"]!["inputs"]!["text"]!.GetValue<string>());
        Assert.Equal((1344, 768, 42L), (graph["latent"]!["inputs"]!["width"]!.GetValue<int>(), graph["latent"]!["inputs"]!["height"]!.GetValue<int>(), graph["sampler"]!["inputs"]!["seed"]!.GetValue<long>()));
        Assert.DoesNotContain("{{", graph.ToJsonString());
        Assert.Equal(Png, picture.Data);
        Assert.Equal(("image/png", 42L), (picture.ContentType, picture.Seed));
        Assert.Contains(server.Requests, r => r.Path == "/view?filename=pic.png&subfolder=&type=temp");
    }

    [Fact]
    public async Task A_portrait_and_a_scene_are_drawn_with_their_own_workflow()
    {
        var set = ComfyUIWorkflows.Find(Shipped)!;
        var server = new StubComfyUI();

        await Generator(server).GenerateAsync(Request(isPortrait: true));

        Assert.Equal(set.Portrait["sampler"]!["inputs"]!["steps"]!.GetValue<int>(), server.Queued!["sampler"]!["inputs"]!["steps"]!.GetValue<int>());
        Assert.NotEqual(set.Scene["sampler"]!["inputs"]!["steps"]!.GetValue<int>(), set.Portrait["sampler"]!["inputs"]!["steps"]!.GetValue<int>());
    }

    [Fact]
    public async Task References_are_uploaded_and_unused_slots_are_cut_out_of_the_conditioning_chain()
    {
        var server = new StubComfyUI();

        await Generator(server).GenerateAsync(Request(false, Portrait(1), Portrait(2)));

        var graph = server.Queued!;
        Assert.Equal(2, server.Requests.Count(r => r.Path == "/upload/image"));
        Assert.StartsWith("roleplay-studio-", graph["reference2_image"]!["inputs"]!["image"]!.GetValue<string>());
        Assert.DoesNotContain(graph, n => n.Key.StartsWith("reference3", StringComparison.Ordinal));
        Assert.Equal("reference2", graph["guidance"]!["inputs"]!["conditioning"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Without_references_a_scene_does_not_use_the_reference_workflow()
    {
        var server = new StubComfyUI();

        await Generator(server).GenerateAsync(Request());

        Assert.DoesNotContain(server.Queued!, n => n.Key.StartsWith("reference", StringComparison.Ordinal));
        Assert.Equal("positive", server.Queued!["guidance"]!["inputs"]!["conditioning"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task A_workflow_ComfyUI_refuses_names_the_node_and_what_it_is_missing()
    {
        var server = new StubComfyUI
        {
            PromptRefusal = """
                {"error":{"type":"prompt_outputs_failed_validation","message":"Prompt outputs failed validation"},
                 "node_errors":{"model":{"class_type":"NunchakuFluxDiTLoader","errors":[{"message":"Value not in list","details":"model_path: 'svdq.safetensors' not in []"}]}}}
                """,
        };

        var refusal = await Assert.ThrowsAsync<ImageProviderException>(() => Generator(server).GenerateAsync(Request()));

        Assert.Equal("prompt_outputs_failed_validation", refusal.Code);
        Assert.Equal("NunchakuFluxDiTLoader: Value not in list model_path: 'svdq.safetensors' not in []", refusal.Parameter);
    }

    [Fact]
    public async Task A_workflow_that_fails_while_running_reports_the_node_that_failed()
    {
        var server = new StubComfyUI
        {
            History = """{"p-1":{"status":{"status_str":"error","completed":false,"messages":[["execution_start",{}],["execution_error",{"node_type":"KSampler","exception_message":"CUDA out of memory\n"}]]},"outputs":{}}}""",
        };

        var failure = await Assert.ThrowsAsync<ImageProviderException>(() => Generator(server).GenerateAsync(Request()));

        Assert.Equal(("executionError", "KSampler: CUDA out of memory"), (failure.Code, failure.Parameter));
    }

    [Fact]
    public async Task A_picture_nobody_waits_for_is_taken_off_the_ComfyUI_queue()
    {
        var server = new StubComfyUI { EmptyPolls = int.MaxValue };
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Generator(server).GenerateAsync(Request(), stop.Token));

        Assert.Contains(server.Requests, r => r.Path == "/queue" && r.Body.Contains("p-1"));
    }
}
