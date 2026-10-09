using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Posts.Commands;

/// <summary>A manual post with Version 1.</summary>
/// GroupWithPostId: the post this one is a platform copy of (Composer "Publish to" several platforms).
public sealed record CreatePostCommand(Guid OrganizationId, SocialPlatform Platform, PostContent Content, Guid? GroupWithPostId = null);

/// <summary>Any meaningful change, including attaching, replacing or removing the image, produces a new version.</summary>
public sealed record RevisePostCommand(Guid OrganizationId, Guid PostId, PostContent Content, byte[]? ExpectedRowVersion);
