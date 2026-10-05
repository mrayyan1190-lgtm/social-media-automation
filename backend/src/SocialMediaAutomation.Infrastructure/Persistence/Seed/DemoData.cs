namespace SocialMediaAutomation.Infrastructure.Persistence.Seed;

/// <summary>Fixed identities of the development/demo seed. Passwords are not stored here; they come from "Seed:DemoPassword".</summary>
public static class DemoData
{
    public static readonly Guid OrganizationAId = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    public static readonly Guid OrganizationBId = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    public static readonly DemoUser AdminA = new(Guid.Parse("a0000000-0000-0000-0000-000000000001"), "admin@demo-a.example", "Avery Admin");
    public static readonly DemoUser CreatorA = new(Guid.Parse("a0000000-0000-0000-0000-000000000002"), "creator@demo-a.example", "Casey Creator");
    public static readonly DemoUser ReviewerA = new(Guid.Parse("a0000000-0000-0000-0000-000000000003"), "reviewer@demo-a.example", "Riley Reviewer");
    public static readonly DemoUser AdminB = new(Guid.Parse("b0000000-0000-0000-0000-000000000001"), "admin@demo-b.example", "Blake Admin");

    // Development customer workspace for manual testing ("Seed:DemoWorkspacePassword"). None of these is a system admin.
    public static readonly Guid CentaivaOrganizationId = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    public static readonly DemoUser CentaivaAdmin = new(Guid.Parse("c0000000-0000-0000-0000-000000000001"), "admin@centaiva.test", "Centaiva Admin");
    public static readonly DemoUser CentaivaCreator = new(Guid.Parse("c0000000-0000-0000-0000-000000000002"), "creator@centaiva.test", "Centaiva Creator");
    public static readonly DemoUser CentaivaReviewer = new(Guid.Parse("c0000000-0000-0000-0000-000000000003"), "reviewer@centaiva.test", "Centaiva Reviewer");

    public sealed record DemoUser(Guid Id, string Email, string DisplayName);
}
