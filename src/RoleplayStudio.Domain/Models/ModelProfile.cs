namespace RoleplayStudio.Domain.Models;

public enum ModelRole
{
    Chat,
    Utility,
    Embedding,
    Image,
}

public enum ProviderKind
{
    /// <summary>OpenRouter, Featherless, OpenAI or any other OpenAI-compatible endpoint.</summary>
    OpenAICompatible,
    Ollama,
    Runware,
    Fal,
    ComfyUI,
}

public class ModelProfile : OwnedEntity
{
    public string Name { get; set; } = "";
    public ModelRole Role { get; set; }
    public ProviderKind Provider { get; set; }
    public string? BaseUrl { get; set; }
    public string ModelId { get; set; } = "";

    /// <summary>Configuration key that holds the API key (e.g. Providers:OpenRouter:ApiKey), so secrets stay out of the database.</summary>
    public string? ApiKeySetting { get; set; }
    public double? Temperature { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? ContextWindow { get; set; }
    public bool IsDefault { get; set; }
}
