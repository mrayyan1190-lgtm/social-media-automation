using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Posts.Commands;

/// <summary>A manual post with Version 1.</summary>
public sealed record CreatePostCommand(Guid OrganizationId, SocialPlatform Platform, PostContent Content);

/// <summary>Any meaningful change, including attaching, replacing or removing the image, produces a new version.</summary>
public sealed record RevisePostCommand(Guid OrganizationId, Guid PostId, PostContent Content, byte[]? ExpectedRowVersion);
