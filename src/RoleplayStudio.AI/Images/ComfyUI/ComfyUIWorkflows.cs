using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RoleplayStudio.AI.Images.ComfyUI;

/// <summary>
/// The workflows one ComfyUI image profile draws with, in ComfyUI's API format: a portrait, a scene, and optionally a scene
/// drawn after reference portraits. Placeholders such as <c>{{prompt}}</c> and <c>{{reference1}}</c> are filled in per picture.
/// </summary>
public sealed record ComfyUIWorkflowSet(string Name, JsonObject Portrait, JsonObject Scene, JsonObject? References)
{
    public const string OutputNode = "output";

    public int MaxReferences => References is null ? 0 : ComfyUIWorkflows.ReferenceSlots(References);

    public bool TakesNegativePrompt => new[] { Portrait, Scene, References }.Any(w => w?.ToJsonString().Contains("{{negative}}", StringComparison.Ordinal) == true);

    public JsonObject For(bool isPortrait, int references) => references > 0 && References is not null ? References : isPortrait ? Portrait : Scene;
}

/// <summary>The workflow sets shipped in this assembly, and how a template becomes the graph ComfyUI is sent.</summary>
public static partial class ComfyUIWorkflows
{
    public const string ResourcePrefix = "comfyui-workflows/";

    private static readonly Lazy<IReadOnlyDictionary<string, ComfyUIWorkflowSet>> Sets = new(Load);

    public static IReadOnlyCollection<string> Names => Sets.Value.Keys.ToList();

    public static ComfyUIWorkflowSet? Find(string name) => Sets.Value.GetValueOrDefault(name.Trim());

    /// <summary>
    /// A copy of <paramref name="template"/> with its placeholders filled in. Reference slots past the uploaded files are
    /// removed, and whatever read from a removed node reads the <c>conditioning</c> or <c>model</c> it was given instead.
    /// </summary>
    public static JsonObject Fill(JsonObject template, string prompt, string? negative, int width, int height, long seed, IReadOnlyList<string> references)
    {
        var graph = template.DeepClone().AsObject();
        var removed = graph
            .Where(n => SlotOf(n.Key) is { } slot && slot > references.Count)
            .ToDictionary(n => n.Key, n => n.Value!.AsObject());
        foreach (var id in removed.Keys)
        {
            graph.Remove(id);
        }

        foreach (var (id, node) in graph)
        {
            if (node?["inputs"] is not JsonObject inputs)
            {
                throw new InvalidOperationException($"Node {id} of the ComfyUI workflow has no inputs");
            }

            foreach (var name in inputs.Select(i => i.Key).ToList())
            {
                inputs[name] = Resolve(inputs[name], removed, prompt, negative, width, height, seed, references);
            }
        }

        if (!graph.ContainsKey(ComfyUIWorkflowSet.OutputNode))
        {
            throw new InvalidOperationException($"The ComfyUI workflow has no node called \"{ComfyUIWorkflowSet.OutputNode}\" to read the picture from");
        }

        return graph;
    }

    internal static int ReferenceSlots(JsonObject template) => template.Select(n => SlotOf(n.Key) ?? 0).DefaultIfEmpty(0).Max();

    private static JsonNode? Resolve(JsonNode? value, IReadOnlyDictionary<string, JsonObject> removed, string prompt, string? negative, int width, int height, long seed, IReadOnlyList<string> references)
    {
        if (value is JsonArray { Count: 2 } link && link[0]?.GetValueKind() == System.Text.Json.JsonValueKind.String)
        {
            var source = link[0]!.GetValue<string>();
            while (removed.TryGetValue(source, out var gone))
            {
                if ((gone["inputs"]?["conditioning"] ?? gone["inputs"]?["model"]) is not JsonArray passed)
                {
                    throw new InvalidOperationException($"A node reads from the unused reference slot {source}, which passes nothing on");
                }

                link = passed;
                source = link[0]!.GetValue<string>();
            }

            return link.DeepClone();
        }

        if (value?.GetValueKind() != System.Text.Json.JsonValueKind.String)
        {
            return value?.DeepClone();
        }

        var text = value.GetValue<string>();
        return text switch
        {
            "{{prompt}}" => prompt,
            "{{negative}}" => negative ?? "",
            "{{width}}" => width,
            "{{height}}" => height,
            "{{seed}}" => seed,
            _ when ReferencePlaceholder().Match(text) is { Success: true } match => references[int.Parse(match.Groups[1].Value) - 1],
            _ => text,
        };
    }

    private static int? SlotOf(string nodeId) => ReferenceNode().Match(nodeId) is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;

    private static Dictionary<string, ComfyUIWorkflowSet> Load()
    {
        var assembly = typeof(ComfyUIWorkflows).Assembly;
        var templates = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => (Parts: n[ResourcePrefix.Length..].Split('/', '\\'), Resource: n))
            .Where(t => t.Parts.Length == 2)
            .GroupBy(t => t.Parts[0], StringComparer.OrdinalIgnoreCase);

        var sets = new Dictionary<string, ComfyUIWorkflowSet>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in templates)
        {
            JsonObject? Read(string kind) => set.FirstOrDefault(t => t.Parts[1] == $"{kind}.json") is { Resource: { } resource }
                ? JsonNode.Parse(assembly.GetManifestResourceStream(resource)!)!.AsObject()
                : null;

            if (Read("portrait") is { } portrait && Read("scene") is { } scene)
            {
                sets[set.Key] = new ComfyUIWorkflowSet(set.Key, portrait, scene, Read("references"));
            }
        }

        return sets;
    }

    [GeneratedRegex(@"^\{\{reference([1-9][0-9]*)\}\}$")]
    private static partial Regex ReferencePlaceholder();

    [GeneratedRegex(@"^reference([1-9][0-9]*)(_|$)")]
    private static partial Regex ReferenceNode();
}
