import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { CUSTOMER_STATUSES, CustomerStatus, customerStatus, statusGroup } from '../../shared/models/states';
import { SOCIAL_PLATFORMS } from '../campaigns/models/campaign.model';
import { PostListItem } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';

type DateRange = '' | 'today' | 'next7' | 'last7' | 'month';

/** Calendar/list colour per customer status (design palette). */
const PALETTE: Record<CustomerStatus, string> = {
  Draft: 'st-draft',
  'In Review': 'st-review',
  'Changes Requested': 'st-failed',
  Approved: 'st-published',
  Scheduled: 'st-scheduled',
  Published: 'st-published',
  'Demo (not sent)': 'st-draft',
  Failed: 'st-failed',
};

interface Row extends PostListItem {
  status: CustomerStatus;
  /** Scheduled or published time; null when the post has no live publication intent. */
  publishAt: string | null;
}

/**
 * Planner: every post with one customer-facing status, as a month calendar (scheduled, published, failed) or a list.
 * Editorial and delivery state stay separate in the data; technical worker states are never shown.
 */
@Component({
  selector: 'app-planner-page',
  imports: [FormsModule, RouterLink, PlatformIconComponent, ProblemBannerComponent],
  templateUrl: './planner.page.html',
  styleUrl: './planner.page.css',
})
export class PlannerPage {
  private readonly postsApi = inject(PostApiService);
  protected readonly workspace = inject(WorkspaceService);

  /** Query parameters from the sidebar shortcuts: ?status=Draft (a list filter) or ?view=list. */
  readonly statusQuery = input<string>(undefined, { alias: 'status' });
  readonly viewQuery = input<string>(undefined, { alias: 'view' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly statuses = CUSTOMER_STATUSES;
  protected readonly thisMonth = new Date().toISOString().slice(0, 7);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly rows = signal<Row[]>([]);
  protected readonly layout = signal<'calendar' | 'list'>('calendar');

  protected readonly platform = signal('');
  protected readonly campaign = signal('');
  protected readonly status = signal('');
  protected readonly range = signal<DateRange>('');
  protected readonly month = signal(this.thisMonth);

  protected readonly campaigns = computed(() =>
    [...new Map(this.rows().filter((r) => r.campaignId).map((r) => [r.campaignId!, r.campaignName!])).entries()]
      .map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name)));

  protected readonly filtersActive = computed(() => !!(this.platform() || this.campaign() || this.status() || this.range()));

  protected readonly filtered = computed(() => {
    const zone = this.workspace.timeZone();
    const today = dayKeyInZone(new Date().toISOString(), zone);
    const shift = (days: number) => dayKeyInZone(new Date(Date.now() + days * 864e5).toISOString(), zone);
    return this.rows().filter((r) => {
      if (this.platform() && r.platform !== this.platform()) return false;
      if (this.campaign() === 'none' ? !!r.campaignId : this.campaign() && r.campaignId !== this.campaign()) return false;
      if (this.status() && statusGroup(r.status) !== this.status()) return false;
      if (this.range()) {
        const day = dayKeyInZone(r.publishAt ?? r.updatedAtUtc, zone);
        if (this.range() === 'today' && day !== today) return false;
        if (this.range() === 'next7' && (day < today || day > shift(7))) return false;
        if (this.range() === 'last7' && (day > today || day < shift(-7))) return false;
        if (this.range() === 'month' && day.slice(0, 7) !== today.slice(0, 7)) return false;
      }
      return true;
    });
  });

  /** "Drafts", "Scheduled posts"… when opened from a Publisher shortcut (or filtered by status). */
  protected readonly listTitle = computed(() => {
    const status = this.status();
    if (!status) return 'All posts';
    return status === 'Draft' ? 'Drafts' : status === 'Failed' ? 'Failed posts' : `${status} posts`;
  });

  /** List order: most recently updated first. */
  protected readonly listRows = computed(() => [...this.filtered()].sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)));

  /** Six-week month grid (yyyy-MM-dd days, Monday first) with the posts publishing on each day. */
  protected readonly days = computed(() => {
    const [year, month] = this.month().split('-').map(Number);
    const first = new Date(Date.UTC(year, month - 1, 1));
    const start = new Date(first.getTime() - ((first.getUTCDay() + 6) % 7) * 864e5);
    const byDay = this.byDay();
    const today = dayKeyInZone(new Date().toISOString(), this.workspace.timeZone());
    return Array.from({ length: 42 }, (_, i) => {
      const date = new Date(start.getTime() + i * 864e5).toISOString().slice(0, 10);
      const inMonth = date.slice(0, 7) === this.month();
      return { date, day: Number(date.slice(8)), inMonth, today: date === today, posts: inMonth ? byDay.get(date) ?? [] : [] };
    });
  });

  protected readonly monthCount = computed(() =>
    [...this.byDay().entries()].filter(([day]) => day.startsWith(this.month())).reduce((n, [, posts]) => n + posts.length, 0));

  protected readonly monthName = computed(() => {
    const [year, month] = this.month().split('-').map(Number);
    return new Intl.DateTimeFormat(undefined, { month: 'long', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)));
  });

  /** Filtered posts with a publishing time, by day in the workspace time zone. */
  private readonly byDay = computed(() => {
    const zone = this.workspace.timeZone();
    const byDay = new Map<string, Row[]>();
    for (const r of this.filtered()) {
      if (!r.publishAt) continue;
      const key = dayKeyInZone(r.publishAt, zone);
      byDay.set(key, [...(byDay.get(key) ?? []), r]);
    }
    for (const posts of byDay.values()) posts.sort((a, b) => a.publishAt!.localeCompare(b.publishAt!));
    return byDay;
  });

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
    effect(() => {
      const status = this.statusQuery() ?? '';
      const list = !!status || this.viewQuery() === 'list';
      untracked(() => {
        this.status.set(status);
        this.layout.set(list ? 'list' : 'calendar');
      });
    });
  }

  protected palette(status: CustomerStatus): string {
    return PALETTE[status];
  }

  protected title(r: PostListItem): string {
    return r.hook || r.bodyExcerpt;
  }

  protected time(utc: string | null, options: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' }): string {
    return utc ? formatInZone(utc, this.workspace.timeZone(), options) : '—';
  }

  protected shiftMonth(delta: number): void {
    const [year, month] = this.month().split('-').map(Number);
    this.month.set(new Date(Date.UTC(year, month - 1 + delta, 1)).toISOString().slice(0, 7));
  }

  protected clear(): void {
    this.platform.set('');
    this.campaign.set('');
    this.status.set('');
    this.range.set('');
  }

  private async load(orgId: string): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      const posts = await firstValueFrom(this.postsApi.listAll(orgId));
      this.rows.set(posts.map((p) => {
        const status = customerStatus(p.editorialState, p.latestDeliveryState);
        // A canceled intent no longer has a publishing date; neither does an approved post that was never scheduled.
        const live = !!p.latestDeliveryState && p.latestDeliveryState !== 'CANCELED' && status !== 'Approved';
        return { ...p, status, publishAt: live ? p.latestPublishAtUtc : null };
      }));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
