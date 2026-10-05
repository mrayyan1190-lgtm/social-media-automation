using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Seed;

/// <summary>
/// Development/demo seed: Organization A (Admin, Creator, Reviewer; distinct-reviewer ON; completed brand profile) and
/// Organization B (one Admin, no brand profile) for tenant-isolation checks. Each part is added only when missing.
/// </summary>
public sealed class DemoDataSeeder(CoreDbContext db, IPasswordHasher passwordHasher, TimeProvider timeProvider)
{
    public async Task<bool> SeedAsync(string demoPassword, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = false;

        if (!await db.Organizations.AnyAsync(o => o.Id == DemoData.OrganizationAId, cancellationToken))
        {
            SeedOrganizationsAndUsers(passwordHasher.Hash(demoPassword), now);
            changed = true;
        }

        if (!await db.BrandProfiles.AnyAsync(p => p.OrganizationId == DemoData.OrganizationAId, cancellationToken))
        {
            db.BrandProfiles.Add(BrandProfile.Create(Guid.NewGuid(), DemoData.OrganizationAId, DemoBrand, DemoData.AdminA.Id, now));
            changed = true;
        }

        // Clearly labelled destinations so the full review workflow can be demonstrated without provider approval.
        if (!await db.SocialAccounts.AnyAsync(a => a.OrganizationId == DemoData.OrganizationAId, cancellationToken))
        {
            foreach (var platform in Enum.GetValues<SocialPlatform>())
                db.SocialAccounts.Add(SocialAccount.CreateUnconnected(Guid.NewGuid(), DemoData.OrganizationAId, platform,
                    $"Brightline {platform} (simulated)", ExecutionMode.SIMULATED, DemoData.AdminA.Id, now));
            db.SocialAccounts.Add(SocialAccount.CreateUnconnected(Guid.NewGuid(), DemoData.OrganizationAId, SocialPlatform.Facebook,
                "Brightline Facebook Page (not connected)", ExecutionMode.NOT_CONFIGURED, DemoData.AdminA.Id, now));
            db.SocialAccounts.Add(SocialAccount.CreateUnconnected(Guid.NewGuid(), DemoData.OrganizationBId, SocialPlatform.LinkedIn,
                "Demo B LinkedIn (simulated)", ExecutionMode.SIMULATED, DemoData.AdminB.Id, now));
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    /// <summary>
    /// "Centaiva Demo Workspace" with an Admin, a Creator and a Reviewer, a brand profile and demo (simulated) destinations,
    /// so all three can sign in at /login and land on the dashboard. Added only when missing.
    /// </summary>
    public async Task<bool> SeedCentaivaAsync(string password, CancellationToken cancellationToken)
    {
        if (await db.Organizations.AnyAsync(o => o.Id == DemoData.CentaivaOrganizationId, cancellationToken))
            return false;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var orgId = DemoData.CentaivaOrganizationId;
        var adminId = DemoData.CentaivaAdmin.Id;
        db.Organizations.Add(Organization.Create(orgId, "Centaiva Demo Workspace", "Software / Technology", "en", "Europe/London", requireDistinctReviewer: true, now));
        var hash = passwordHasher.Hash(password);
        foreach (var (demoUser, role) in new[]
        {
            (DemoData.CentaivaAdmin, OrganizationRole.Admin),
            (DemoData.CentaivaCreator, OrganizationRole.Creator),
            (DemoData.CentaivaReviewer, OrganizationRole.Reviewer),
        })
        {
            db.Users.Add(User.Create(demoUser.Id, demoUser.Email, demoUser.DisplayName, hash, now));
            db.OrganizationMembers.Add(OrganizationMember.Create(Guid.NewGuid(), orgId, demoUser.Id, role, now));
        }
        db.BrandProfiles.Add(BrandProfile.Create(Guid.NewGuid(), orgId, CentaivaBrand, adminId, now));
        foreach (var platform in Enum.GetValues<SocialPlatform>())
            db.SocialAccounts.Add(SocialAccount.CreateUnconnected(Guid.NewGuid(), orgId, platform, $"Centaiva {platform} (demo)", ExecutionMode.SIMULATED, adminId, now));

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Development-only internal system administrator. Created only when no user has that email; an existing account is
    /// never promoted (that would turn a customer identity into an operator). Safe to run on every start.
    /// </summary>
    public async Task<bool> SeedSystemAdminAsync(string email, string password, string displayName, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == User.NormalizeEmail(email), cancellationToken))
            return false;

        db.Users.Add(User.CreateSystemAdmin(Guid.NewGuid(), email, displayName, passwordHasher.Hash(password), timeProvider.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void SeedOrganizationsAndUsers(string passwordHash, DateTime now)
    {
        db.Organizations.AddRange(
            Organization.Create(DemoData.OrganizationAId, "Demo Organization A", "Software / Technology", "en", "Europe/London", requireDistinctReviewer: true, now),
            Organization.Create(DemoData.OrganizationBId, "Demo Organization B", "Retail", "en", "America/New_York", requireDistinctReviewer: true, now));

        foreach (var (demoUser, organizationId, role) in new[]
        {
            (DemoData.AdminA, DemoData.OrganizationAId, OrganizationRole.Admin),
            (DemoData.CreatorA, DemoData.OrganizationAId, OrganizationRole.Creator),
            (DemoData.ReviewerA, DemoData.OrganizationAId, OrganizationRole.Reviewer),
            (DemoData.AdminB, DemoData.OrganizationBId, OrganizationRole.Admin),
        })
        {
            db.Users.Add(User.Create(demoUser.Id, demoUser.Email, demoUser.DisplayName, passwordHash, now));
            db.OrganizationMembers.Add(OrganizationMember.Create(Guid.NewGuid(), organizationId, demoUser.Id, role, now));
        }
    }

    private static readonly BrandProfileDetails CentaivaBrand = new(
        BusinessDescription: "Centaiva helps marketing teams plan, write and approve social media content in one place.",
        TargetAudience: "Marketing managers at small and medium-sized businesses.",
        BrandTone: BrandTone.Professional,
        Website: "https://centaiva.test",
        ProhibitedTerms: ["guaranteed"]);

    private static readonly BrandProfileDetails DemoBrand = new(
        BusinessDescription: "Brightline Analytics builds a reporting platform that connects finance and operations data for mid-sized companies: "
            + "self-service dashboards and month-end reporting automation.",
        TargetAudience: "Finance and operations leaders at companies with 100-1,000 employees.",
        BrandTone: BrandTone.Professional,
        Website: "https://brightline.example",
        ProhibitedTerms: ["guaranteed", "best in the world", "#1", "revolutionary"]);
}
