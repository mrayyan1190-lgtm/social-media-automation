import { AfterViewInit, Component, effect, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { IconComponent } from '../../../../shared/components/icon.component';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { formatInZone } from '../../../publications/models/time-zones';
import { CampaignDialogComponent } from '../../components/campaign-dialog.component';
import { Campaign, CampaignSummary } from '../../models/campaign.model';
import { CampaignApiService } from '../../services/campaign-api.service';

/** Campaigns are folders for related posts. Creating one is a small modal, never a separate page. */
@Component({
  selector: 'app-campaign-list-page',
  imports: [RouterLink, IconComponent, ProblemBannerComponent, CampaignDialogComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Campaigns</h1>
        <p class="muted">Organize related posts into campaigns.</p>
      </div>
      @if (workspace.canManageCampaigns()) {
        <button type="button" class="primary" (click)="dialog().open()"><app-icon name="plus" [size]="16" />Create Campaign</button>
      }
    </div>
    <app-problem-banner [problem]="problem()" />

    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (!campaigns().length && !includeArchived()) {
      <section class="empty-state">
        <app-icon name="megaphone" [size]="26" />
        <h2>No campaigns yet.</h2>
        <p class="muted">Group posts for a launch, an event or a theme.</p>
        @if (workspace.canManageCampaigns()) {
          <button type="button" class="primary" (click)="dialog().open()"><app-icon name="plus" [size]="16" />Create Campaign</button>
        }
      </section>
    } @else {
      <div class="folder-grid">
        @for (c of campaigns(); track c.id) {
          <a class="folder-card" [routerLink]="['/campaigns', c.id]" [style.--folder]="c.color">
            <span class="folder-dot" aria-hidden="true"></span>
            <span class="folder-body">
              <strong>{{ c.name }}</strong>
              @if (c.description) { <span class="hint clamp">{{ c.description }}</span> }
              <span class="hint">{{ c.postCount }} {{ c.postCount === 1 ? 'post' : 'posts' }} · Updated {{ time(c.updatedAtUtc) }}</span>
            </span>
            @if (c.status === 'Archived') { <span class="badge muted">Archived</span> }
          </a>
        }
      </div>
      <label class="checkbox archived-toggle"><input type="checkbox" [checked]="includeArchived()" (change)="includeArchived.set(!includeArchived())" /> Show archived</label>
    }

    <app-campaign-dialog (saved)="created($event)" />
  `,
})
export class CampaignListPage implements AfterViewInit {
  private readonly api = inject(CampaignApiService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);
  protected readonly dialog = viewChild.required(CampaignDialogComponent);

  /** ?create=1 (from Home) opens the Create Campaign modal right away. */
  readonly create = input<string>();

  protected readonly campaigns = signal<CampaignSummary[]>([]);
  protected readonly includeArchived = signal(false);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const includeArchived = this.includeArchived();
      if (orgId) void this.load(orgId, includeArchived);
    });
  }

  ngAfterViewInit(): void {
    if (!this.create() || !this.workspace.canManageCampaigns()) return;
    this.dialog().open();
    // Drop the flag so Back from a campaign does not reopen the modal.
    void this.router.navigate([], { queryParams: { create: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone(), { dateStyle: 'medium' });
  }

  protected created(campaign: Campaign): void {
    void this.router.navigate(['/campaigns', campaign.id]);
  }

  private async load(orgId: string, includeArchived: boolean): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.campaigns.set(await firstValueFrom(this.api.list(orgId, includeArchived)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
