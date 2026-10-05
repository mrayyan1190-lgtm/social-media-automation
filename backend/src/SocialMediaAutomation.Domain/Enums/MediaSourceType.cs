namespace SocialMediaAutomation.Domain.Enums;

/// <summary>Where a MediaAsset's bytes came from. Future AI-generated images get their own value and the same governance.</summary>
public enum MediaSourceType
{
    Upload,
    AiGenerated,
}
