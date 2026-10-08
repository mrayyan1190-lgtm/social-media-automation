using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Application.Publications.DTOs;
using SocialMediaAutomation.Application.Reviews.DTOs;
using SocialMediaAutomation.Application.SocialAccounts.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Application.Dashboard;

/// <summary>
/// One workspace-health view (TASKS.md Phase 23). Editorial counts come from posts and delivery counts from publication
/// jobs, never mixed; PUBLISHED and SIMULATED are separate numbers so a simulation never inflates real results.
/// </summary>
public sealed record DashboardDto(
    IReadOnlyDictionary<EditorialState, int> Editorial,
    IReadOnlyDictionary<DeliveryState, int> Delivery,
    int PendingReviewCount,
    IReadOnlyList<ReviewSummaryDto> PendingReviews,
    IReadOnlyList<PublicationJobDto> Upcoming,
    IReadOnlyList<PublicationJobDto> NeedsAttention,
    IReadOnlyList<SocialAccountDto> Destinations);

public sealed class DashboardService(
    OrganizationAuthorizer authorizer,
    IPostRepository posts,
    IPublicationRepository publications,
    IReviewRepository reviews,
    ISocialAccountRepository socialAccounts)
{
    private static readonly DeliveryState[] Upcoming =
        [DeliveryState.SCHEDULED, DeliveryState.PROCESSING, DeliveryState.WAITING_MEDIA, DeliveryState.RETRY_PENDING];

    private static readonly DeliveryState[] Attention =
        [DeliveryState.ACTION_REQUIRED, DeliveryState.RECONCILIATION_REQUIRED, DeliveryState.FAILED];

    public async Task<DashboardDto> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewContent, cancellationToken);
        var org = access.OrganizationId;
        var editorial = await posts.CountByEditorialStateAsync(org, cancellationToken);
        var delivery = await publications.CountByDeliveryStateAsync(org, cancellationToken);
        var pending = await reviews.ListAsync(org, ReviewStatus.PENDING, null, cancellationToken);
        return new DashboardDto(
            Enum.GetValues<EditorialState>().ToDictionary(s => s, s => editorial.GetValueOrDefault(s)),
            Enum.GetValues<DeliveryState>().ToDictionary(s => s, s => delivery.GetValueOrDefault(s)),
            pending.Count,
            pending.OrderBy(r => r.SubmittedAtUtc).Take(5).ToList(),
            await publications.ListAsync(org, new PublicationQuery(States: Upcoming, SoonestFirst: true, Take: 5), cancellationToken),
            await publications.ListAsync(org, new PublicationQuery(States: Attention, Take: 5), cancellationToken),
            await socialAccounts.ListAsync(org, null, cancellationToken));
    }
}
