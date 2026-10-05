import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { OrganizationApiService } from '../../../../core/services/organization-api.service';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { IconComponent } from '../../../../shared/components/icon.component';
import { PlatformIconComponent } from '../../../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { DELIVERY_LABELS } from '../../../../shared/models/states';
import { ReviewApiService } from '../../../approvals/services/review-api.service';
import { AiApiService } from '../../../content/services/ai-api.service';
import { formatInZone } from '../../../publications/models/time-zones';
import { PublicationApiService } from '../../../publications/services/publication-api.service';
import { BrandProfile } from '../../../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../../../settings/services/brand-profile-api.service';
import { CampaignContentComponent } from '../../components/campaign-content/campaign-content.component';
import { CampaignFactsEditorComponent } from '../../components/campaign-facts-editor/campaign-facts-editor.component';
import { CampaignFormComponent, CampaignFormValue } from '../../components/campaign-form/campaign-form.component';
import { CTA_LABELS, Campaign, CampaignFact, OBJECTIVE_LABELS } from '../../models/campaign.model';
import { CampaignApiService } from '../../services/campaign-api.service';

type Tab = 'overview' | 'content' | 'activity';

interface ActivityItem {
  at: string;
  text: string;
}

/** One campaign: Overview (brief and brand context), Content (one post per platform) and Activity. */
@Component({
  selector: 'app-campaign-detail-page',
  imports: [
    RouterLink, RouterLinkActive, IconComponent, PlatformIconComponent, ProblemBannerComponent,
    CampaignFormComponent, CampaignFactsEditorComponent, CampaignContentComponent,
  ],
  templateUrl: './campaign-detail.page.html',
})
export class CampaignDetailPage {
  private readonly api = inject(CampaignApiService);
  private readonly brandApi = inject(BrandProfileApiService);
  private readonly organizations = inject(OrganizationApiService);
  private readonly ai = inject(AiApiService);
  private readonly reviews = inject(ReviewApiService);
  private readonly publications = inject(PublicationApiService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);

  /** Route parameter and route data / query parameter, bound by the router. */
  readonly campaignId = input.required<string>();
  readonly tab = input<Tab>('overview');
  readonly generate = input<string>();

  protected readonly objectiveLabels = OBJECTIVE_LABELS;
  protected readonly ctaLabels = CTA_LABELS;
  protected readonly campaign = signal<Campaign | null>(null);
  protected readonly facts = signal<CampaignFact[]>([]);
  protected readonly brand = signal<BrandProfile | null>(null);
  protected readonly createdByName = signal<string | null>(null);
  protected readonly activity = signal<ActivityItem[] | null>(null);
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly editable = computed(() => this.workspace.canManageCampaigns() && this.campaign()?.status !== 'Archived');

  constructor() {
    effect(() => {
      if (this.workspace.orgId() && this.campaignId()) void this.load();
    });
    effect(() => {
      if (this.tab() === 'activity' && this.campaign()) void this.loadActivity();
    });
  }

  protected time(utc: string, options: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }): string {
    return formatInZone(utc, this.workspace.timeZone(), options);
  }

  async load(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.problem.set(null);
    try {
      const campaign = await firstValueFrom(this.api.get(orgId, this.campaignId()));
      this.campaign.set(campaign);
      this.facts.set(campaign.facts);
      const [brand, members] = await Promise.all([
        firstValueFrom(this.brandApi.get(orgId)).catch(() => null),
        firstValueFrom(this.organizations.members(orgId)).catch(() => []),
      ]);
      this.brand.set(brand);
      this.createdByName.set(members.find((m) => m.userId === campaign.createdByUserId)?.displayName ?? null);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected async reloadFacts(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    try {
      this.facts.set((await firstValueFrom(this.api.get(orgId, this.campaignId()))).facts);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected generateContent(): void {
    void this.router.navigate(['/campaigns', this.campaignId(), 'content'], { queryParams: { generate: 1 } });
  }

  protected async update({ input, platforms }: CampaignFormValue): Promise<void> {
    await this.mutate(async (orgId, c) => {
      const updated = await firstValueFrom(this.api.update(orgId, c.id, input, c.rowVersion));
      const unchanged = platforms.length === updated.platforms.length && platforms.every((p) => updated.platforms.includes(p));
      return unchanged ? updated : firstValueFrom(this.api.setPlatforms(orgId, c.id, platforms, updated.rowVersion));
    });
    if (!this.problem()) this.editing.set(false);
  }

  protected archive(): Promise<void> {
    if (!confirm('Archive this campaign? It becomes read-only.')) return Promise.resolve();
    return this.mutate((orgId, c) => firstValueFrom(this.api.archive(orgId, c.id, c.rowVersion)));
  }

  private async mutate(action: (orgId: string, campaign: Campaign) => Promise<Campaign>): Promise<void> {
    const orgId = this.workspace.orgId();
    const current = this.campaign();
    if (!orgId || !current) return;
    this.saving.set(true);
    this.problem.set(null);
    try {
      const saved = await action(orgId, current);
      this.campaign.set(saved);
      this.facts.set(saved.facts);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  /** A readable timeline from generation requests, reviews and publishing for this campaign. */
  private async loadActivity(): Promise<void> {
    const orgId = this.workspace.orgId();
    const campaignId = this.campaignId();
    if (!orgId) return;
    try {
      const [requests, reviews, jobs] = await Promise.all([
        firstValueFrom(this.ai.listForCampaign(orgId, campaignId)),
        firstValueFrom(this.reviews.list(orgId)),
        firstValueFrom(this.publications.list(orgId)),
      ]);
      const items: ActivityItem[] = [];
      for (const r of requests) {
        const what = r.kind === 'Generation' ? `Content generated for ${r.platforms.join(', ')}` : `${r.platforms.join(', ')} post rewritten`;
        items.push({ at: r.completedAtUtc ?? r.createdAtUtc, text: r.status === 'FAILED' ? `${what}: did not complete` : r.status === 'COMPLETED' ? what : `${what} (in progress)` });
      }
      for (const r of reviews.filter((x) => x.campaignId === campaignId)) {
        items.push({ at: r.submittedAtUtc, text: `${r.submittedByName} submitted ${r.platform} Version ${r.versionNumber} for review` });
        if (r.decidedAtUtc) {
          items.push({ at: r.decidedAtUtc, text: `${r.decidedByName} ${r.status === 'APPROVED' ? 'approved' : 'requested changes on'} ${r.platform} Version ${r.versionNumber}` });
        }
      }
      for (const j of jobs.filter((x) => x.campaignId === campaignId)) {
        items.push({ at: j.createdAtUtc, text: `${j.platform} Version ${j.versionNumber} scheduled for ${this.time(j.publishAtUtc)}` });
        if (j.deliveryState !== 'SCHEDULED') items.push({ at: j.publishAtUtc, text: `${j.platform}: ${DELIVERY_LABELS[j.deliveryState]}` });
      }
      this.activity.set(items.sort((a, b) => b.at.localeCompare(a.at)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }
}
