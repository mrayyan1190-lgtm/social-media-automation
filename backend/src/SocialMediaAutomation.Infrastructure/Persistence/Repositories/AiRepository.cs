using Microsoft.EntityFrameworkCore;
using SocialMediaAutomation.Application.Abstractions;
using SocialMediaAutomation.Application.AiGeneration.DTOs;
using SocialMediaAutomation.Domain.Entities;
using SocialMediaAutomation.Domain.Enums;

namespace SocialMediaAutomation.Infrastructure.Persistence.Repositories;

internal sealed class AiRepository(CoreDbContext db) : IAiRepository
{
    public Task<AiProviderSettings?> GetSettingsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.AiProviderSettings.SingleOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

    public Task<AiGenerationRequest?> GetRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        db.AiGenerationRequests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);

    public Task<AiGenerationRequest?> FindActiveRequestAsync(Guid organizationId, AiRequestKind kind, Guid postId, CancellationToken cancellationToken) =>
        db.AiGenerationRequests
            .Where(r => r.OrganizationId == organizationId && r.Kind == kind && r.PostId == postId
                && (r.Status == AiRequestStatus.PENDING || r.Status == AiRequestStatus.PROCESSING))
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AiGenerationRequest?> FindActiveGenerationAsync(
        Guid organizationId, Guid requestedByUserId, Guid? campaignId, string inputSnapshot, CancellationToken cancellationToken) =>
        db.AiGenerationRequests
            .Where(r => r.OrganizationId == organizationId && r.Kind == AiRequestKind.Generation && r.RequestedByUserId == requestedByUserId
                && r.CampaignId == campaignId && (r.Status == AiRequestStatus.PENDING || r.Status == AiRequestStatus.PROCESSING)
                && r.InputSnapshot == inputSnapshot)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AiOutputDto>> ListOutputsAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        await db.AiRequestOutputs.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && o.RequestId == requestId)
            .OrderBy(o => o.Platform)
            .Select(o => new AiOutputDto(o.Platform, o.PostId, o.PostVersionId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContentEvaluationDto>> ListEvaluationsAsync(Guid organizationId, Guid postId, CancellationToken cancellationToken) =>
        (await db.ContentEvaluations.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId && e.PostId == postId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken))
        .Select(e => new ContentEvaluationDto(e.PostVersionId, e.BrandAlignment, e.AudienceRelevance, e.Clarity, e.PlatformFit, e.CtaRelevance,
            e.Summary, e.Issues, e.HasCriticalIssue, e.EvaluatorModel, e.CreatedAtUtc))
        .ToList();

    public void Add(AiProviderSettings settings) => db.AiProviderSettings.Add(settings);

    public void Add(AiGenerationRequest request) => db.AiGenerationRequests.Add(request);

    public void Add(AiRequestOutput output) => db.AiRequestOutputs.Add(output);

    public void Add(AiCallLog log) => db.AiCallLogs.Add(log);

    public void Add(ContentEvaluation evaluation) => db.ContentEvaluations.Add(evaluation);
}
