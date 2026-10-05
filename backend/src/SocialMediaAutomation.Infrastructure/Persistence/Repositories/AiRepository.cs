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

    public Task<AiGenerationRequest?> FindActiveRequestAsync(Guid organizationId, Guid campaignId, AiRequestKind kind, Guid? postId, CancellationToken cancellationToken) =>
        db.AiGenerationRequests
            .Where(r => r.OrganizationId == organizationId && r.CampaignId == campaignId && r.Kind == kind && r.PostId == postId
                && (r.Status == AiRequestStatus.PENDING || r.Status == AiRequestStatus.PROCESSING))
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AiGenerationRequestDto>> ListRequestsForCampaignAsync(Guid organizationId, Guid campaignId, int take, CancellationToken cancellationToken)
    {
        var requests = await db.AiGenerationRequests.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.CampaignId == campaignId)
            .OrderByDescending(r => r.CreatedAtUtc).Take(take).ToListAsync(cancellationToken);
        var ids = requests.Select(r => r.Id).ToList();
        var outputs = await db.AiRequestOutputs.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId && ids.Contains(o.RequestId)).ToListAsync(cancellationToken);
        return requests.Select(r => AiGenerationRequestDto.From(r,
            outputs.Where(o => o.RequestId == r.Id).Select(o => new AiOutputDto(o.Platform, o.PostId, o.PostVersionId)).ToList())).ToList();
    }

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
