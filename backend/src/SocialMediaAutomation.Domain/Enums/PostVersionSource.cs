namespace SocialMediaAutomation.Domain.Enums;

/// <summary>How a PostVersion came to exist. Manual covers human edits, including media changes.</summary>
public enum PostVersionSource
{
    Manual,
    AiGeneration,
    AiRewrite,
    AiImage,
}
