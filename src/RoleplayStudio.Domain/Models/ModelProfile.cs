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

    // A key outside this section (a connection string, the invite code) must never be sendable to a user-chosen URL.
    public const string ApiKeySettingPrefix = "Providers:";

    public const string DefaultOllamaUrl = "http://localhost:11434";

    public static bool CanChat(ProviderKind provider) => provider is ProviderKind.OpenAICompatible or ProviderKind.Ollama;

    public bool IsChatModel => Role == ModelRole.Chat && CanChat(Provider);

    public void CopyEditableFieldsFrom(ModelProfile other)
    {
        Name = other.Name;
        Role = other.Role;
        Provider = other.Provider;
        BaseUrl = other.BaseUrl;
        ModelId = other.ModelId;
        ApiKeySetting = other.ApiKeySetting;
        Temperature = other.Temperature;
        MaxOutputTokens = other.MaxOutputTokens;
        ContextWindow = other.ContextWindow;
        IsDefault = other.IsDefault;
    }

    public void Normalize()
    {
        Name = Name.Trim();
        ModelId = ModelId.Trim();
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl.Trim();
        ApiKeySetting = string.IsNullOrWhiteSpace(ApiKeySetting) ? null : ApiKeySetting.Trim();
    }

    /// <summary>The first reason this profile cannot be saved, as a message template, or null when it can.</summary>
    public string? FindProblem()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Give the profile a name";
        }

        if (string.IsNullOrWhiteSpace(ModelId))
        {
            return "Enter the model id";
        }

        if (Role is not ModelRole.Image && !CanChat(Provider))
        {
            return "Chat, utility and embedding models need an OpenAI-compatible or Ollama provider";
        }

        if (BaseUrl is not null && !(Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            return "The base URL must be an http or https address";
        }

        if (Provider == ProviderKind.OpenAICompatible && BaseUrl is null)
        {
            return "An OpenAI-compatible provider needs a base URL";
        }

        if (ApiKeySetting is not null && (!ApiKeySetting.StartsWith(ApiKeySettingPrefix, StringComparison.Ordinal) || ApiKeySetting.Length == ApiKeySettingPrefix.Length))
        {
            return "The API key setting must be a name under Providers:, such as Providers:OpenRouter:ApiKey";
        }

        if (Temperature is < 0 or > 2)
        {
            return "The temperature must be between 0 and 2";
        }

        if (MaxOutputTokens is <= 0 || ContextWindow is <= 0)
        {
            return "Token limits must be positive";
        }

        return null;
    }
}
