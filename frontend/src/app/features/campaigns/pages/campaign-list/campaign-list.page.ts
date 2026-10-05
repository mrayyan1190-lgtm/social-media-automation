import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { IconComponent } from '../../../../shared/components/icon.component';
import { PlatformIconComponent } from '../../../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { formatInZone } from '../../../publications/models/time-zones';
import { CampaignSummary, OBJECTIVE_LABELS } from '../../models/campaign.model';
import { CampaignApiService } from '../../services/campaign-api.service';

/** Starter ideas for the empty state; each fills the prompt with an editable example. */
const IDEAS: { label: string; prompt: string }[] = [
  { label: 'Product Launch', prompt: 'We are launching a new product. Explain what it does, who it is for and encourage people to try it.' },
  { label: 'Brand Awareness', prompt: 'Introduce our business, what makes us different and the problems we solve for our customers.' },
  { label: 'Announcement', prompt: 'Share an important update with our audience and explain what it means for them.' },
  { label: 'Lead Generation', prompt: 'Encourage our target audience to get in touch or book a call to learn how we can help them.' },
  { label: 'Event Promotion', prompt: 'Promote our upcoming event: what it is, who should attend, when it happens and how to register.' },
];

/** Campaigns: lightweight containers for one content initiative. New workspaces get a prompt-first empty state. */
@Component({
  selector: 'app-campaign-list-page',
  imports: [RouterLink, FormsModule, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  template: `
    <app-problem-banner [problem]="problem()" />
    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (campaigns().length === 0 && !includeArchived()) {
      <section class="empty-hero">
        <app-icon name="megaphone" [size]="28" />
        <h1>Create your first campaign</h1>
        <p class="muted">Tell AI what you want to communicate and we will use your saved brand information automatically.</p>
        @if (workspace.canManageCampaigns()) {
          <textarea rows="4" [(ngModel)]="prompt" aria-label="Campaign prompt"
            placeholder="We are launching our AI reporting dashboard for small businesses. Focus on saving reporting time and encourage users to book a demo."></textarea>
          <div class="chips">
            @for (idea of ideas; track idea.label) {
              <button type="button" class="chip" (click)="prompt = idea.prompt">{{ idea.label }}</button>
            }
          </div>
          <button type="button" class="primary" (click)="start()"><app-icon name="plus" [size]="16" />Create Campaign</button>
        } @else {
          <p class="hint">Campaigns created by your team will appear here.</p>
        }
      </section>
    } @else {
      <div class="page-header">
        <div>
          <h1>Campaigns</h1>
          <p class="muted">Each campaign holds one post per platform, with its own versions, review and publishing.</p>
        </div>
        @if (workspace.canManageCampaigns()) {
          <a class="button primary" routerLink="/campaigns/new"><app-icon name="plus" [size]="16" />Create Campaign</a>
        }
      </div>
      <label class="checkbox"><input type="checkbox" [checked]="includeArchived()" (change)="includeArchived.set(!includeArchived())" /> Show archived</label>
      <div class="campaign-grid">
        @for (c of campaigns(); track c.id) {
          <a class="campaign-card" [routerLink]="['/campaigns', c.id]">
            <strong>{{ c.name }}</strong>
            <span class="hint">{{ objectiveLabels[c.objective] }}@if (c.status === 'Archived') { · Archived }</span>
            <span class="platform-row">@for (p of c.platforms; track p) { <app-platform-icon [platform]="p" /> }</span>
            <span class="hint">Updated {{ time(c.updatedAtUtc) }}</span>
          </a>
        } @empty {
          <p class="empty">No campaigns.</p>
        }
      </div>
    }
  `,
})
export class CampaignListPage {
  private readonly api = inject(CampaignApiService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly ideas = IDEAS;
  protected readonly objectiveLabels = OBJECTIVE_LABELS;
  protected readonly campaigns = signal<CampaignSummary[]>([]);
  protected readonly includeArchived = signal(false);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected prompt = '';

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const includeArchived = this.includeArchived();
      if (orgId) void this.load(orgId, includeArchived);
    });
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone(), { dateStyle: 'medium' });
  }

  protected start(): void {
    void this.router.navigate(['/campaigns/new'], { queryParams: this.prompt.trim() ? { prompt: this.prompt.trim() } : {} });
  }

  private async load(orgId: string, includeArchived: boolean): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      const all = await firstValueFrom(this.api.list(orgId, includeArchived));
      this.campaigns.set([...all].sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
