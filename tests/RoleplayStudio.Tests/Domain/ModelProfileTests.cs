using RoleplayStudio.Domain.Models;

namespace RoleplayStudio.Tests.Domain;

public class ModelProfileTests
{
    private static ModelProfile Valid() => new()
    {
        Name = "OpenRouter",
        Provider = ProviderKind.OpenAICompatible,
        BaseUrl = "https://openrouter.ai/api/v1",
        ModelId = "mistralai/mistral-nemo",
        ApiKeySetting = "Providers:OpenRouter:ApiKey",
    };

    [Fact]
    public void A_complete_profile_has_no_problem() => Assert.Null(Valid().FindProblem());

    [Theory]
    [InlineData(ModelRole.Chat, false)]
    [InlineData(ModelRole.Utility, false)]
    [InlineData(ModelRole.Embedding, false)]
    [InlineData(ModelRole.Image, true)]
    public void Only_an_image_model_may_use_an_image_provider(ModelRole role, bool allowed)
    {
        var profile = Valid();
        profile.Role = role;
        profile.Provider = ProviderKind.Runware;

        Assert.Equal(allowed, profile.FindProblem() is null);
    }

    [Theory]
    [InlineData(ProviderKind.OpenAICompatible)]
    [InlineData(ProviderKind.Ollama)]
    public void An_image_model_needs_a_provider_that_can_draw(ProviderKind provider)
    {
        var profile = Valid();
        profile.Role = ModelRole.Image;
        profile.Provider = provider;

        Assert.NotNull(profile.FindProblem());
    }

    [Theory]
    [InlineData("ConnectionStrings:roleplaydb")]
    [InlineData("providers:OpenRouter:ApiKey")]
    [InlineData("Providers:")]
    public void The_api_key_setting_must_name_a_key_under_providers(string setting)
    {
        var profile = Valid();
        profile.ApiKeySetting = setting;

        Assert.NotNull(profile.FindProblem());
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("openrouter.ai/api/v1")]
    public void The_base_url_must_be_an_absolute_http_address(string url)
    {
        var profile = Valid();
        profile.BaseUrl = url;

        Assert.NotNull(profile.FindProblem());
    }

    [Fact]
    public void An_openai_compatible_profile_needs_a_base_url()
    {
        var profile = Valid();
        profile.BaseUrl = "   ";
        profile.Normalize();

        Assert.NotNull(profile.FindProblem());
    }

    [Fact]
    public void An_ollama_profile_may_leave_the_base_url_and_key_empty()
    {
        var profile = new ModelProfile { Name = "Local", Provider = ProviderKind.Ollama, ModelId = "mistral-nemo", ApiKeySetting = " " };
        profile.Normalize();

        Assert.Null(profile.FindProblem());
        Assert.Null(profile.ApiKeySetting);
    }
}
