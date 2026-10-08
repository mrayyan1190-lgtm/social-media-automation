using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<BrandProfile> BrandProfiles => Set<BrandProfile>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostVersion> PostVersions => Set<PostVersion>();
    public DbSet<AiProviderSettings> AiProviderSettings => Set<AiProviderSettings>();
    public DbSet<AiGenerationRequest> AiGenerationRequests => Set<AiGenerationRequest>();
    public DbSet<AiRequestOutput> AiRequestOutputs => Set<AiRequestOutput>();
    public DbSet<AiCallLog> AiCallLogs => Set<AiCallLog>();
    public DbSet<ContentEvaluation> ContentEvaluations => Set<ContentEvaluation>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<ReviewRequest> ReviewRequests => Set<ReviewRequest>();
    public DbSet<ReviewComment> ReviewComments => Set<ReviewComment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OAuthConnection> OAuthConnections => Set<OAuthConnection>();
    public DbSet<OAuthState> OAuthStates => Set<OAuthState>();
    public DbSet<PublicationJob> PublicationJobs => Set<PublicationJob>();
    public DbSet<PublicationAttempt> PublicationAttempts => Set<PublicationAttempt>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();
    public DbSet<PlatformAiConfiguration> PlatformAiConfigurations => Set<PlatformAiConfiguration>();
    public DbSet<SocialProviderConfiguration> SocialProviderConfigurations => Set<SocialProviderConfiguration>();
    public DbSet<PlatformSecret> PlatformSecrets => Set<PlatformSecret>();
    public DbSet<PlatformAuditLog> PlatformAuditLogs => Set<PlatformAuditLog>();
    public DbSet<SignInEvent> SignInEvents => Set<SignInEvent>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreDbContext).Assembly);
}
