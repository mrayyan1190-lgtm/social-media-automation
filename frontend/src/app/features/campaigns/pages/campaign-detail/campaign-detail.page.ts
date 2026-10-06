import { Component, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { IconComponent } from '../../../../shared/components/icon.component';
import { PlatformIconComponent } from '../../../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { CustomerStatus, customerStatus, statusGroup, statusTone } from '../../../../shared/models/states';
import { PostListItem } from '../../../content/models/post.model';
import { PostApiService } from '../../../content/services/post-api.service';
import { formatInZone } from '../../../publications/models/time-zones';
import { CampaignDialogComponent } from '../../components/campaign-dialog.component';
import { Campaign } from '../../models/campaign.model';
import { CampaignApiService } from '../../services/campaign-api.service';

const FILTERS: (CustomerStatus | 'All')[] = ['All', 'Draft', 'In Review', 'Changes Requested', 'Approved', 'Scheduled', 'Published'];

/** One campaign folder: its posts with a status filter. Opening a post goes to Composer. */
@Component({
  selector: 'app-campaign-detail-page',
  imports: [RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent, CampaignDialogComponent],
  template: `
    <a class="back" routerLink="/campaigns">← Campaigns</a>
    <div class="page-header">
      <div class="title-row">
        <span class="color-dot lg" [style.background]="campaign()?.color ?? '#94a3b8'" aria-hidden="true"></span>
        <div>
          <h1>{{ campaign()?.name ?? 'Campaign' }} @if (campaign()?.status === 'Archived') { <span class="badge muted">Archived</span> }</h1>
          <p class="muted">{{ posts().length }} {{ posts().length === 1 ? 'Post' : 'Posts' }}@if (campaign()?.description) { · {{ campaign()?.description }} }</p>
        </div>
      </div>
      @if (editable()) {
        <div class="actions">
          <a class="button primary" routerLink="/composer" [queryParams]="{ campaignId: campaignId() }"><app-icon name="plus" [size]="16" />Create Post</a>
          <a class="button" routerLink="/ai-studio" [queryParams]="{ campaignId: campaignId() }"><app-icon name="sparkles" [size]="16" />Create with AI</a>
          <button type="button" class="icon-btn" title="Edit campaign" aria-label="Edit campaign" (click)="dialog().open(campaign()!)"><app-icon name="edit" [size]="16" /></button>
          <button type="button" class="icon-btn" title="Archive campaign" aria-label="Archive campaign" (click)="archive()"><app-icon name="archive" [size]="16" /></button>
        </div>
      }
    </div>
    <app-problem-banner [problem]="problem()" (reload)="load()" />

    <div class="chip-filters" role="tablist" aria-label="Status">
      @for (f of filters; track f) {
        <button type="button" role="tab" class="chip" [class.on]="filter() === f" [attr.aria-selected]="filter() === f" (click)="filter.set(f)">
          {{ f }} <span class="chip-count">{{ count(f) }}</span>
        </button>
      }
    </div>

    @if (loading() && !posts().length) {
      <p class="muted">Loading…</p>
    } @else {
      <div class="post-table card flush">
        @for (p of shown(); track p.id) {
          <a class="post-row" [routerLink]="['/composer', p.id]">
            <span class="post-main">
              <strong class="ellipsis-line">{{ p.hook || p.bodyExcerpt }}</strong>
              @if (p.hook) { <span class="hint ellipsis-line">{{ p.bodyExcerpt }}</span> }
            </span>
            <span><app-platform-icon [platform]="p.platform" [label]="true" /></span>
            <span><span [class]="'badge delivery ' + tone(p.status)">{{ p.status }}</span></span>
            <span class="hint nowrap">{{ time(p.updatedAtUtc) }}</span>
          </a>
        } @empty {
          <div class="empty-row">
            @if (posts().length) { No {{ filter() }} posts. } @else { No posts in this campaign yet. }
          </div>
        }
      </div>
    }

    <app-campaign-dialog (saved)="campaign.set($event)" />
  `,
})
export class CampaignDetailPage {
  private readonly api = inject(CampaignApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);
  protected readonly dialog = viewChild.required(CampaignDialogComponent);

  /** Route parameter. */
  readonly campaignId = input.required<string>();

  protected readonly filters = FILTERS;
  protected readonly tone = statusTone;
  protected readonly campaign = signal<Campaign | null>(null);
  protected readonly posts = signal<(PostListItem & { status: CustomerStatus })[]>([]);
  protected readonly filter = signal<CustomerStatus | 'All'>('All');
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly editable = computed(() => this.workspace.canManageCampaigns() && !!this.campaign() && this.campaign()?.status !== 'Archived');
  protected readonly shown = computed(() => this.filter() === 'All' ? this.posts() : this.posts().filter((p) => statusGroup(p.status) === this.filter()));

  constructor() {
    effect(() => {
      if (this.workspace.orgId() && this.campaignId()) void this.load();
    });
  }

  protected count(f: CustomerStatus | 'All'): number {
    return f === 'All' ? this.posts().length : this.posts().filter((p) => statusGroup(p.status) === f).length;
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone(), { dateStyle: 'medium', timeStyle: 'short' });
  }

  async load(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.loading.set(true);
    this.problem.set(null);
    try {
      const [campaign, posts] = await Promise.all([
        firstValueFrom(this.api.get(orgId, this.campaignId())),
        firstValueFrom(this.postsApi.listAll(orgId, this.campaignId())),
      ]);
      this.campaign.set(campaign);
      this.posts.set(posts.map((p) => ({ ...p, status: customerStatus(p.editorialState, p.latestDeliveryState) })));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async archive(): Promise<void> {
    const orgId = this.workspace.orgId();
    const c = this.campaign();
    if (!orgId || !c || !confirm(`Archive “${c.name}”? Its posts stay as they are; the campaign becomes read-only.`)) return;
    try {
      await firstValueFrom(this.api.archive(orgId, c.id, c.rowVersion));
      await this.router.navigate(['/campaigns']);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }
}
