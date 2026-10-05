import { Component, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { BrandProfile } from '../../../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../../../settings/services/brand-profile-api.service';
import { CampaignDraft, CampaignFormComponent, CampaignFormValue } from '../../components/campaign-form/campaign-form.component';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../models/campaign.model';
import { CampaignApiService } from '../../services/campaign-api.service';

/**
 * Create Campaign: name, prompt and platforms. The brand profile supplies audience and tone. Query parameters
 * (name, prompt, platforms) prefill the form when coming from the empty state or AI Studio.
 */
@Component({
  selector: 'app-campaign-create-page',
  imports: [RouterLink, CampaignFormComponent, ProblemBannerComponent],
  template: `
    <section class="narrow">
      <div class="page-header">
        <div>
          <h1>Create Campaign</h1>
          <p class="muted">Describe what you want to communicate. AI writes a separate post for each platform; nothing is published without approval.</p>
        </div>
      </div>
      <app-problem-banner [problem]="problem()" />
      <div class="card">
        <app-campaign-form [brand]="brand()" [draft]="draft()" [saving]="saving()" submitLabel="Create Campaign" (save)="create($event)">
          <a class="button" routerLink="/campaigns">Cancel</a>
        </app-campaign-form>
      </div>
    </section>
  `,
})
export class CampaignCreatePage {
  private readonly api = inject(CampaignApiService);
  private readonly brandApi = inject(BrandProfileApiService);
  private readonly router = inject(Router);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  protected readonly workspace = inject(WorkspaceService);

  protected readonly saving = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly brand = signal<BrandProfile | null>(null);
  protected readonly draft = computed<CampaignDraft>(() => ({
    name: this.query.get('name') ?? undefined,
    prompt: this.query.get('prompt') ?? undefined,
    platforms: (this.query.get('platforms')?.split(',') ?? []).filter((p): p is SocialPlatform => SOCIAL_PLATFORMS.includes(p as SocialPlatform)),
  }));

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void firstValueFrom(this.brandApi.get(orgId)).then((b) => this.brand.set(b)).catch(() => this.brand.set(null));
    });
  }

  protected async create({ input, platforms }: CampaignFormValue): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.saving.set(true);
    this.problem.set(null);
    try {
      const created = await firstValueFrom(this.api.create(orgId, input));
      await firstValueFrom(this.api.setPlatforms(orgId, created.id, platforms, created.rowVersion));
      await this.router.navigate(['/campaigns', created.id]);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.saving.set(false);
    }
  }
}
