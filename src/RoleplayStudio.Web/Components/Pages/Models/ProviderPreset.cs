using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.Web.Components.Pages.Models;

public sealed record ProviderPreset(string Label, ProviderKind Provider, string? BaseUrl, string? ApiKeySetting, ModelRole Role = ModelRole.Chat)
{
    public static readonly IReadOnlyList<ProviderPreset> All =
    [
        new("OpenRouter", ProviderKind.OpenAICompatible, "https://openrouter.ai/api/v1", "Providers:OpenRouter:ApiKey"),
        new("Featherless", ProviderKind.OpenAICompatible, "https://api.featherless.ai/v1", "Providers:Featherless:ApiKey"),
        new("Ollama", ProviderKind.Ollama, ModelProfile.DefaultOllamaUrl, null),
        new("Runware", ProviderKind.Runware, ModelProfile.DefaultRunwareUrl, "Providers:Runware:ApiKey", ModelRole.Image),
    ];

    public override string ToString() => Label;
}
