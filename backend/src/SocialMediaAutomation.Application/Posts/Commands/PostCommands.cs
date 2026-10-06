using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Posts.Commands;

/// <summary>A manual post with Version 1; <see cref="CampaignId"/> optionally files it into a campaign folder.</summary>
public sealed record CreatePostCommand(Guid OrganizationId, Guid? CampaignId, SocialPlatform Platform, PostContent Content);

/// <summary>Any meaningful change, including attaching, replacing or removing the image, produces a new version.</summary>
public sealed record RevisePostCommand(Guid OrganizationId, Guid PostId, PostContent Content, byte[]? ExpectedRowVersion);

/// <summary>Files the post into a campaign folder (or none, when null). Organizational only: no new version, approval unchanged.</summary>
public sealed record MovePostToCampaignCommand(Guid OrganizationId, Guid PostId, Guid? CampaignId, byte[]? ExpectedRowVersion);
