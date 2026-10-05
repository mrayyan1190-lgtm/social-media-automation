using System.ComponentModel.DataAnnotations;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Contracts;

/// <summary>The full public-facing content of the next version. Omitting MediaAssetId removes the image; omitting SocialAccountId clears the destination.</summary>
public record SavePostContentRequest(
    string? Hook,
    string Body,
    string? CallToAction,
    string? DestinationUrl,
    string[]? Hashtags,
    Guid? MediaAssetId,
    string? AltText,
    Guid? SocialAccountId = null)
{
    public PostContent ToContent() => new(Hook, Body, CallToAction, DestinationUrl, Hashtags, MediaAssetId, AltText, SocialAccountId);
}

public sealed record CreatePostRequest(
    [Required] SocialPlatform? Platform,
    string? Hook,
    string Body,
    string? CallToAction,
    string? DestinationUrl,
    string[]? Hashtags,
    Guid? MediaAssetId,
    string? AltText,
    Guid? SocialAccountId = null)
    : SavePostContentRequest(Hook, Body, CallToAction, DestinationUrl, Hashtags, MediaAssetId, AltText, SocialAccountId);
