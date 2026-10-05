import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { CUSTOMER_STATUSES, CustomerStatus, customerStatus, statusTone } from '../../shared/models/states';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { SOCIAL_PLATFORMS } from '../campaigns/models/campaign.model';
import { PostListItem } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';
import { SocialAccount } from '../social-accounts/models/social-account.model';
import { SocialAccountApiService } from '../social-accounts/services/social-account-api.service';

type View = 'planner' | 'all' | 'drafts';

interface Row extends PostListItem {
  status: CustomerStatus;
}

const TITLES: Record<View, { title: string; subtitle: string }> = {
  planner: { title: 'Planner', subtitle: 'All content across campaigns: what exists, what state it is in and when it publishes.' },
  all: { title: 'All Posts', subtitle: 'Every post in the workspace, one per platform per campaign.' },
  drafts: { title: 'Drafts', subtitle: 'Content still being created or waiting for your changes.' },
};

/**
 * Planner, All Posts and Drafts: one post browser with compact filters. The Planner adds a month calendar of publishing
 * dates. Status is shown in customer language; editorial and delivery state stay separate underneath.
 */
@Component({
  selector: 'app-publisher-posts-page',
  imports: [FormsModule, RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  templateUrl: './posts.page.html',
})
export class PublisherPostsPage {
  private readonly postsApi = inject(PostApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);

  readonly view = input<View>('all');

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly statuses = CUSTOMER_STATUSES;
  protected readonly statusTone = statusTone;
  protected readonly titles = TITLES;
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly rows = signal<Row[]>([]);
  protected readonly accounts = signal<SocialAccount[]>([]);
  protected readonly layout = signal<'calendar' | 'list'>('calendar');

  // Filters
  protected readonly search = signal('');
  protected readonly platform = signal('');
  protected readonly campaign = signal('');
  protected readonly status = signal(inject(ActivatedRoute).snapshot.queryParamMap.get('status') ?? '');
  protected readonly createdBy = signal('');
  protected readonly account = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly month = signal(new Date().toISOString().slice(0, 7));

  protected readonly campaigns = computed(() => unique(this.rows().map((r) => [r.campaignId, r.campaignName] as const)));
  protected readonly creators = computed(() => unique(this.rows().map((r) => [r.createdByUserId, r.createdByName] as const)));

  protected readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const zone = this.workspace.timeZone();
    return this.rows().filter((r) => {
      if (this.view() === 'drafts' && r.status !== 'Draft' && r.status !== 'Changes Requested') return false;
      if (q && !`${r.campaignName} ${r.hook ?? ''} ${r.bodyExcerpt}`.toLowerCase().includes(q)) return false;
      if (this.platform() && r.platform !== this.platform()) return false;
      if (this.campaign() && r.campaignId !== this.campaign()) return false;
      if (this.status() && r.status !== this.status()) return false;
      if (this.createdBy() && r.createdByUserId !== this.createdBy()) return false;
      if (this.account() && r.socialAccountId !== this.account()) return false;
      const day = r.latestPublishAtUtc ? dayKeyInZone(r.latestPublishAtUtc, zone) : null;
      if (this.from() && (!day || day < this.from())) return false;
      if (this.to() && (!day || day > this.to())) return false;
      return true;
    });
  });

  /** Month grid (weeks of yyyy-MM-dd days, Monday first) with the posts publishing on each day. */
  protected readonly weeks = computed(() => {
    const [year, month] = this.month().split('-').map(Number);
    const first = new Date(Date.UTC(year, month - 1, 1));
    const start = new Date(first.getTime() - ((first.getUTCDay() + 6) % 7) * 864e5);
    const zone = this.workspace.timeZone();
    const byDay = new Map<string, Row[]>();
    for (const r of this.filtered()) {
      if (!r.latestPublishAtUtc || r.status === 'Approved' || r.latestDeliveryState === 'CANCELED') continue;
      const key = dayKeyInZone(r.latestPublishAtUtc, zone);
      byDay.set(key, [...(byDay.get(key) ?? []), r]);
    }
    const today = dayKeyInZone(new Date().toISOString(), zone);
    return Array.from({ length: 6 }, (_, w) => Array.from({ length: 7 }, (_, d) => {
      const date = new Date(start.getTime() + (w * 7 + d) * 864e5).toISOString().slice(0, 10);
      return { date, day: Number(date.slice(8)), inMonth: date.slice(0, 7) === this.month(), today: date === today, posts: byDay.get(date) ?? [] };
    })).filter((week) => week.some((day) => day.inMonth));
  });
  protected readonly monthLabel = computed(() => {
    const [year, month] = this.month().split('-').map(Number);
    return new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)));
  });
  protected readonly filtersActive = computed(() =>
    !!(this.search() || this.platform() || this.campaign() || this.status() || this.createdBy() || this.account() || this.from() || this.to()));

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
  }

  protected time(utc: string | null, options: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }): string {
    return utc ? formatInZone(utc, this.workspace.timeZone(), options) : '—';
  }

  protected shiftMonth(delta: number): void {
    const [year, month] = this.month().split('-').map(Number);
    this.month.set(new Date(Date.UTC(year, month - 1 + delta, 1)).toISOString().slice(0, 7));
  }

  protected clearFilters(): void {
    for (const f of [this.search, this.platform, this.campaign, this.status, this.createdBy, this.account, this.from, this.to]) f.set('');
  }

  protected accountName(id: string): string {
    return this.accounts().find((a) => a.id === id)?.displayName ?? 'Account';
  }

  protected canWork(r: Row): boolean {
    return this.workspace.canManageCampaigns() && (r.editorialState === 'DRAFT' || r.editorialState === 'CHANGES_REQUESTED');
  }

  /** Submits the exact current version. Without a destination yet, the campaign's Content tab asks for one first. */
  protected async submit(r: Row): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    if (!r.socialAccountId) {
      await this.router.navigate(['/campaigns', r.campaignId, 'content']);
      return;
    }
    this.busy.set(r.id);
    this.problem.set(null);
    this.notice.set(null);
    try {
      const post = await firstValueFrom(this.postsApi.get(orgId, r.id));
      const review = await firstValueFrom(this.reviewsApi.submit(orgId, post.id, null, post.rowVersion));
      this.notice.set(`${r.platform} Version ${review.versionNumber} of “${r.campaignName}” was sent for review.`);
      await this.load(orgId);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }

  private async load(orgId: string): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      const [posts, accounts] = await Promise.all([
        firstValueFrom(this.postsApi.listAll(orgId)),
        firstValueFrom(this.accountsApi.list(orgId)).catch(() => []),
      ]);
      this.rows.set(posts.map((p) => ({ ...p, status: customerStatus(p.editorialState, p.latestDeliveryState) })));
      this.accounts.set(accounts);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}

function unique(pairs: (readonly [string, string])[]): { id: string; name: string }[] {
  return [...new Map(pairs).entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name));
}
