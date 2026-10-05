using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Domain.Tests;

public class AiProviderSettingsTests
{
    [Theory]
    [InlineData(AiProvider.OpenAI, "https://ignored.example", "https://api.openai.com/v1")]
    [InlineData(AiProvider.Groq, null, "https://api.groq.com/openai/v1")]
    [InlineData(AiProvider.OpenAICompatible, "https://api.together.xyz/v1/", "https://api.together.xyz/v1")]
    [InlineData(AiProvider.OpenAICompatible, "http://localhost:11434/v1", "http://localhost:11434/v1")]
    [InlineData(AiProvider.OpenAICompatible, "http://127.0.0.1:4010/v1", "http://127.0.0.1:4010/v1")]
    public void Known_providers_use_official_endpoints_and_custom_endpoints_are_kept(AiProvider provider, string? input, string expected) =>
        Assert.Equal(expected, AiProviderSettings.ResolveBaseUrl(provider, input));

    [Theory]
    [InlineData("http://llm.example.com/v1")] // key would travel in clear text
    [InlineData("ftp://llm.example.com")]
    [InlineData("")]
    public void Custom_endpoint_must_be_https_unless_loopback(string url) =>
        Assert.Equal(ErrorCodes.ValidationFailed,
            Assert.Throws<BusinessException>(() => AiProviderSettings.ResolveBaseUrl(AiProvider.OpenAICompatible, url)).Code);

    [Fact]
    public void Key_is_required_on_creation_and_kept_when_omitted_on_update()
    {
        Assert.Throws<BusinessException>(() => AiProviderSettings.Create(Guid.NewGuid(), Guid.NewGuid(), AiProvider.OpenAI, null, "m", null!, null!, Guid.NewGuid(), DateTime.UtcNow));

        var settings = AiProviderSettings.Create(Guid.NewGuid(), Guid.NewGuid(), AiProvider.OpenAI, null, "m", "protected-1", "abcd", Guid.NewGuid(), DateTime.UtcNow);
        settings.Update(AiProvider.OpenAI, null, "m2", null, null, Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(("protected-1", "abcd", "m2"), (settings.ProtectedApiKey, settings.ApiKeyHint, settings.Model));
    }
}
