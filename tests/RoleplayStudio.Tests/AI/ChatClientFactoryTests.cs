using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.Tests.AI;

public class ChatClientFactoryTests
{
    private const string Secret = "sk-test-secret";

    private static ChatClientFactory Factory() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Providers:OpenRouter:ApiKey"] = Secret,
            ["Providers:OpenRouter:BaseUrl"] = "https://openrouter.ai/api/v1",
            ["Providers:Featherless:BaseUrl"] = "https://api.featherless.ai/v1",
            ["Registration:InviteCode"] = "invite",
        })
        .Build());

    private static ModelProfile OpenRouter(string? apiKeySetting) => new()
    {
        Name = "OpenRouter",
        Provider = ProviderKind.OpenAICompatible,
        BaseUrl = "https://openrouter.ai/api/v1",
        ModelId = "mistralai/mistral-nemo",
        ApiKeySetting = apiKeySetting,
    };

    [Fact]
    public void An_openai_compatible_profile_talks_to_its_base_url()
    {
        var result = Factory().Create(OpenRouter("Providers:OpenRouter:ApiKey"));

        Assert.True(result.IsSuccess);
        var metadata = result.Value.GetService<ChatClientMetadata>();
        Assert.Equal("openrouter.ai", metadata?.ProviderUri?.Host);
        Assert.Equal("mistralai/mistral-nemo", metadata?.DefaultModelId);
    }

    [Fact]
    public void An_ollama_profile_defaults_to_the_local_server()
    {
        var result = Factory().Create(new ModelProfile { Name = "Local", Provider = ProviderKind.Ollama, ModelId = "mistral-nemo" });

        Assert.True(result.IsSuccess);
        Assert.Equal(new Uri(ModelProfile.DefaultOllamaUrl), result.Value.GetService<ChatClientMetadata>()?.ProviderUri);
    }

    [Theory]
    [InlineData("https://collector.example/v1")]
    [InlineData("http://openrouter.ai/api/v1")]
    [InlineData("https://openrouter.ai.collector.example/api/v1")]
    public void A_provider_key_is_never_sent_to_a_host_other_than_its_own(string baseUrl)
    {
        var profile = OpenRouter("Providers:OpenRouter:ApiKey");
        profile.BaseUrl = baseUrl;

        var result = Factory().Create(profile);

        Assert.True(result.IsFailure);
        Assert.Contains("Providers:OpenRouter:BaseUrl", result.Error);
    }

    [Fact]
    public void A_missing_key_names_the_setting_but_never_a_value()
    {
        var result = Factory().Create(OpenRouter("Providers:Featherless:ApiKey"));

        Assert.True(result.IsFailure);
        Assert.Contains("Providers:Featherless:ApiKey", result.Error);
        Assert.DoesNotContain(Secret, result.Error);
    }

    [Fact]
    public void A_setting_outside_the_providers_section_is_never_read()
    {
        var result = Factory().Create(OpenRouter("Registration:InviteCode"));

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("invite", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_provider_cannot_chat()
    {
        var result = Factory().Create(new ModelProfile { Name = "Images", Role = ModelRole.Image, Provider = ProviderKind.Runware, ModelId = "flux" });

        Assert.True(result.IsFailure);
    }
}
