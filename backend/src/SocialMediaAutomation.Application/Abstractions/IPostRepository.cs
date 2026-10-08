using SocialMediaAutomation.Application.Posts.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Abstractions;

public interface IPostRepository
{
    /// <summary>Loads by id only; callers must verify OrganizationId against the authorized organization.</summary>
    Task<Post?> GetAsync(Guid postId, CancellationToken cancellationToken);

    /// <summary>A version of the given post within the given organization, or null.</summary>
    Task<PostVersion?> GetVersionAsync(Guid organizationId, Guid postId, Guid versionId, CancellationToken cancellationToken);

    /// <summary>Non-archived posts of the organization, newest activity first, with the latest publication job.</summary>
    Task<IReadOnlyList<PostListItemDto>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>All versions of a post, newest first, with creator names and media metadata.</summary>
    Task<IReadOnlyList<PostVersionDto>> ListVersionsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<EditorialState, int>> CountByEditorialStateAsync(Guid organizationId, CancellationToken cancellationToken);

    void Add(Post post);

    void Add(PostVersion version);
}
