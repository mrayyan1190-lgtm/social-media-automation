using System.ComponentModel.DataAnnotations;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Api.Contracts;

/// <summary>
/// The full public-facing content of the next version. Images are in publishing order (at most 4); omitting them removes
/// every image; omitting SocialAccountId clears the destination.
/// </summary>
public record SavePostContentRequest(
    string? Hook,
    string Body,
    string? CallToAction,
    string? DestinationUrl,
    string[]? Hashtags,
    PostImage[]? Images,
    Guid? SocialAccountId = null)
{
    public PostContent ToContent() => new(Hook, Body, CallToAction, DestinationUrl, Hashtags, Images, SocialAccountId);
}

public sealed record CreatePostRequest(
    [Required] SocialPlatform? Platform,
    string? Hook,
    string Body,
    string? CallToAction,
    string? DestinationUrl,
    string[]? Hashtags,
    PostImage[]? Images,
    Guid? SocialAccountId = null)
    : SavePostContentRequest(Hook, Body, CallToAction, DestinationUrl, Hashtags, Images, SocialAccountId);
