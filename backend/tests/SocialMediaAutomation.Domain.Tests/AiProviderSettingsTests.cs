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
    [InlineData(AiProvider.HuggingFace, null, "https://router.huggingface.co/v1")]
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

    [Fact]
    public void Hugging_Face_images_need_a_Hugging_Face_key()
    {
        var (user, now) = (Guid.NewGuid(), DateTime.UtcNow);
        var config = PlatformAiConfiguration.Create(user, now);
        config.ApiKeyReplaced(user, now); // an OpenAI LLM key
        config.Update(AiProvider.OpenAI, null, "gpt-4o-mini", null, null, null, true, user, now);

        // The OpenAI key cannot be sent to Hugging Face.
        Assert.Throws<BusinessException>(() =>
            config.UpdateImage(AiProvider.HuggingFace, null, "Tongyi-MAI/Z-Image-Turbo", "1024x1024", true, user, now));

        config.ImageApiKeyReplaced(user, now);
        config.UpdateImage(AiProvider.HuggingFace, null, "Tongyi-MAI/Z-Image-Turbo", "1024x1024", true, user, now);
        Assert.True(config.ImageEnabled);

        // A Hugging Face LLM key is the same account, so it may be shared.
        var shared = PlatformAiConfiguration.Create(user, now);
        shared.ApiKeyReplaced(user, now);
        shared.Update(AiProvider.HuggingFace, null, "meta-llama/Llama-3.3-70B-Instruct", null, null, null, true, user, now);
        shared.UpdateImage(AiProvider.HuggingFace, null, "black-forest-labs/FLUX.1-schnell", "1024x1024", true, user, now);
        Assert.True(shared.ImageCanUseLlmKey);
    }

    [Fact]
    public void Vision_never_uses_the_image_generation_key()
    {
        var (user, now) = (Guid.NewGuid(), DateTime.UtcNow);
        var config = PlatformAiConfiguration.Create(user, now);
        config.ApiKeyReplaced(user, now);
        config.Update(AiProvider.Groq, null, "openai/gpt-oss-120b", null, null, null, true, user, now);
        config.ImageApiKeyReplaced(user, now);
        config.UpdateImage(AiProvider.HuggingFace, null, "Tongyi-MAI/Z-Image-Turbo", "1024x1024", true, user, now);
        Assert.Null(config.HuggingFaceTokenSecretName);

        config.VisionApiKeyReplaced(user, now);
        Assert.Equal(PlatformAiConfiguration.VisionApiKeySecretName, config.HuggingFaceTokenSecretName);
    }
}
