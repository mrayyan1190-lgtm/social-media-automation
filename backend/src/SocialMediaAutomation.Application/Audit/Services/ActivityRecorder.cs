using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Audit.Services;

/// <summary>
/// Adds audit entries and notifications to the current unit of work, so they commit (or roll back) together with the
/// business change that caused them. It never saves on its own.
/// </summary>
public sealed class ActivityRecorder(IActivityRepository activity, IOrganizationRepository organizations, TimeProvider timeProvider)
{
    public const string OrganizationResource = "Organization";
    public const string BrandProfileResource = "BrandProfile";
    public const string CampaignResource = "Campaign";
    public const string PostResource = "Post";
    public const string ReviewResource = "ReviewRequest";
    public const string SocialAccountResource = "SocialAccount";

    public void Audit(Guid organizationId, Guid? actorUserId, string action, string resourceType, Guid resourceId, IReadOnlyDictionary<string, string?>? metadata = null) =>
        activity.Add(AuditLog.Record(organizationId, actorUserId, action, resourceType, resourceId, metadata, timeProvider.GetUtcNow().UtcDateTime));

    /// <summary>
    /// Audits a new version. When the post was APPROVED before the edit, also records that the approval was revoked:
    /// the old approved version stays immutable and the new version starts as an unapproved DRAFT (PRD §21).
    /// </summary>
    public void VersionCreated(Post post, PostVersion version, Guid? actorUserId, Guid? revokedApprovalId)
    {
        Audit(post.OrganizationId, actorUserId, AuditActions.PostVersionCreated, PostResource, post.Id, new Dictionary<string, string?>
        {
            ["postVersionId"] = version.Id.ToString(), ["versionNumber"] = version.VersionNumber.ToString(), ["source"] = version.CreationSource.ToString(),
        });
        if (revokedApprovalId is { } approvalId)
        {
            Audit(post.OrganizationId, actorUserId, AuditActions.ApprovalRevoked, PostResource, post.Id, new Dictionary<string, string?>
            {
                ["revokedReviewRequestId"] = approvalId.ToString(), ["newPostVersionId"] = version.Id.ToString(),
            });
        }
    }

    public void Notify(Guid organizationId, IEnumerable<Guid> recipients, NotificationType type, string title, string message, string resourceType, Guid resourceId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var recipient in recipients.Distinct())
            activity.Add(Notification.Create(organizationId, recipient, type, title, message, resourceType, resourceId, now));
    }

    /// <summary>Active members holding any of the given roles, excluding one user (usually the actor).</summary>
    public async Task<IReadOnlyList<Guid>> MembersAsync(Guid organizationId, IReadOnlyCollection<OrganizationRole> roles, Guid? except, CancellationToken cancellationToken) =>
        (await organizations.ListActiveMemberUserIdsAsync(organizationId, roles, cancellationToken)).Where(id => id != except).ToList();
}
