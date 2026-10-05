using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.Audit.Services;
using SocialMediaAutomation.Application.BrandProfiles.DTOs;
using SocialMediaAutomation.Application.Common;
using SocialMediaAutomation.Application.Common.Authorization;
using SocialMediaAutomation.Domain.Constants;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;
using SocialMediaAutomation.Domain.Exceptions;

namespace SocialMediaAutomation.Application.BrandProfiles.Services;

public sealed class BrandProfileService(
    OrganizationAuthorizer authorizer,
    IBrandProfileRepository brandProfiles,
    ActivityRecorder activity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<BrandProfileDto> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ViewBrandProfile, cancellationToken);
        var profile = await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken)
            ?? throw new BusinessException(ErrorCodes.NotFound, "This workspace has no brand profile yet.");
        return BrandProfileDto.From(profile);
    }

    /// <summary>
    /// Creates the workspace's single brand profile, or updates it. Updating requires the current row version;
    /// creating must not send one, so a client that believes a profile exists cannot silently create a second state.
    /// </summary>
    public async Task<BrandProfileDto> SaveAsync(Guid organizationId, BrandProfileDetails details, byte[]? expectedRowVersion, CancellationToken cancellationToken)
    {
        var access = await authorizer.AuthorizeAsync(organizationId, Permission.ManageBrandProfile, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var profile = await brandProfiles.GetByOrganizationAsync(access.OrganizationId, cancellationToken);

        if (profile is null)
        {
            if (expectedRowVersion is not null)
                throw new BusinessException(ErrorCodes.ConcurrencyConflict, "The brand profile no longer exists. Reload and try again.");
            profile = BrandProfile.Create(Guid.NewGuid(), access.OrganizationId, details, access.UserId, now);
            brandProfiles.Add(profile);
        }
        else
        {
            ConcurrencyGuard.EnsureCurrent(profile.RowVersion, expectedRowVersion);
            profile.Update(details, access.UserId, now);
        }
        activity.Audit(access.OrganizationId, access.UserId, AuditActions.BrandProfileSaved, ActivityRecorder.BrandProfileResource, profile.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BrandProfileDto.From(profile);
    }
}
