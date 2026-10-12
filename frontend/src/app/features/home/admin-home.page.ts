import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { MODE_LABELS, PROBLEM_DELIVERY, customerStatus } from '../../shared/models/states';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../shared/models/platform.model';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { PostListItem } from '../content/models/post.model';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { Dashboard, DashboardApiService } from '../dashboard/dashboard-api.service';
import { formatInZone } from '../publications/models/time-zones';
import { Activity, art, fromAudit, ringFill, utcMs } from './home.utils';

const DAY = 864e5;
const ARTS = ['a', 'b', 'c', 'd', 'e'];
const ART_WORDS: [string, string][] = [['MAKE', 'MORE.'], ['GROW', 'SMART.'], ['IDEAS', 'MOVE.'], ['THINK', 'AHEAD.'], ['CREATE', 'MORE.']];
const PLATFORM_MARK: Record<SocialPlatform, string> = { LinkedIn: 'in', Facebook: 'f', Instagram: '◎' };
/** Avatar colours of the activity rows, in the design's order. */
const AVATARS = [['#E1E4FE', '#6069B5'], ['#EADFFA', '#8463B7'], ['#F8E4DC', '#BD765D']];
/** Donut circumference (r = 61). */
const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

type Filter = 'all' | 'draft' | 'scheduled';

/**
 * Workspace Admin home: the AI studio entry, the publishing overview, recent content, channels, approvals, this week's
 * drafting and recent workspace activity. Every number comes from the workspace's posts, reviews, accounts and audit
 * log. Creators keep their own home (HomePage).
 */
@Component({
  selector: 'app-admin-home-page',
  imports: [ProblemBannerComponent, RouterLink, FormsModule],
  templateUrl: './admin-home.page.html',
  styleUrl: './admin-home.page.css',
})
export class AdminHomePage {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly mediaApi = inject(MediaApiService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly posts = signal<PostListItem[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly activity = signal<Activity[]>([]);

  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  protected readonly platforms = SOCIAL_PLATFORMS;

  private readonly zone = computed(() => this.workspace.timeZone());
  private readonly status = (p: PostListItem) => customerStatus(p.editorialState, p.latestDeliveryState);

  // ---- Header --------------------------------------------------------------------------------------

  protected readonly greeting = computed(() => {
    const hour = Number(new Intl.DateTimeFormat('en-US', { timeZone: this.zone(), hour: 'numeric', hourCycle: 'h23' }).format(new Date()));
    return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
  });
  protected readonly today = computed(() => this.fmt(new Date().toISOString(), { month: 'long', day: 'numeric', year: 'numeric' }));

  // ---- Publishing overview -------------------------------------------------------------------------

  protected readonly overview = computed(() => {
    const posts = this.posts();
    const drafts = posts.filter((p) => p.editorialState === 'DRAFT').length;
    const done = posts.filter((p) => p.latestDeliveryState === 'PUBLISHED' || PROBLEM_DELIVERY.includes(p.latestDeliveryState ?? ''));
    const counts: [number, string][] = [
      [drafts, '#8073D0'],
      [posts.filter((p) => p.editorialState === 'IN_REVIEW').length, '#E8AC90'],
      [posts.filter((p) => this.status(p) === 'Scheduled').length, '#65AFD7'],
      [posts.filter((p) => p.latestDeliveryState === 'PUBLISHED').length, '#66BD9C'],
    ];
    return {
      fill: ringFill(counts),
      total: posts.length,
      drafts,
      review: posts.filter((p) => p.editorialState === 'IN_REVIEW').length,
      scheduled: posts.filter((p) => this.status(p) === 'Scheduled').length,
      published: posts.filter((p) => p.latestDeliveryState === 'PUBLISHED').length,
      // Published out of every publication that finished (failed ones count against it); none finished yet: "—".
      success: done.length ? `${Math.round((done.filter((p) => p.latestDeliveryState === 'PUBLISHED').length / done.length) * 100)}%` : '—',
    };
  });

  // ---- Your content --------------------------------------------------------------------------------

  protected readonly cards = computed(() => {
    const q = this.search().trim().toLowerCase();
    const filter = this.filter();
    return this.posts()
      .filter((p) => filter === 'all' || (filter === 'draft' ? p.editorialState === 'DRAFT' : this.status(p) === 'Scheduled'))
      .filter((p) => (p.hook || p.bodyExcerpt).toLowerCase().includes(q))
      .sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc))
      // ponytail: newest six only; the Planner lists everything. Add paging here if Home should show more.
      .slice(0, 6)
      .map((p) => {
        const n = art(p.id) % 5;
        return {
          id: p.id, title: p.hook || p.bodyExcerpt, platform: p.platform, mark: PLATFORM_MARK[p.platform], status: this.status(p),
          mediaId: p.mediaAssetId ?? null, art: ARTS[n], words: ART_WORDS[n],
          time: `${this.fmt(p.updatedAtUtc, { month: 'short', day: 'numeric', year: 'numeric' })} · ${this.fmt(p.updatedAtUtc, { timeStyle: 'short' })}`,
        };
      });
  });

  // ---- Channels and approvals ----------------------------------------------------------------------

  protected readonly channels = computed(() => {
    const accounts = this.dashboard()?.destinations ?? [];
    const since = Date.now() - 30 * DAY;
    return SOCIAL_PLATFORMS.map((platform) => {
      const account = accounts.find((a) => a.platform === platform && a.isEligibleDestination) ?? null;
      return {
        platform,
        connected: !!account,
        // The mode stays visible (a demo or test destination is never shown as plain "Connected").
        label: account ? MODE_LABELS[account.executionMode] ?? 'Connected' : 'Not connected',
        published: this.posts().filter((p) => p.platform === platform && p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc
          && utcMs(p.latestPublishAtUtc) >= since).length,
      };
    });
  });
  protected readonly connectedCount = computed(() => this.channels().filter((c) => c.connected).length);
  protected readonly pendingApprovals = computed(() => this.reviews().filter((r) => r.status === 'PENDING').length);

  // ---- Creative momentum: this week, Monday to Sunday in the workspace time zone ---------------------

  protected readonly week = computed(() => {
    const zone = this.zone();
    const key = (ms: number) => new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(ms);
    const now = Date.now();
    const dow = (WEEKDAYS.indexOf(new Intl.DateTimeFormat('en-US', { timeZone: zone, weekday: 'short' }).format(now)) + 7) % 7;
    // ponytail: steps whole days from now, so a DST change can shift a post made near midnight into the next day.
    const days = WEEKDAYS.map((label, i) => ({ label, key: key(now - (dow - i) * DAY), ms: now - (dow - i) * DAY }));
    const keys = new Set(days.map((d) => d.key));
    const drafted = this.posts().map((p) => key(utcMs(p.createdAtUtc))).filter((k) => keys.has(k));
    const published = this.posts().filter((p) => p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc && keys.has(key(utcMs(p.latestPublishAtUtc)))).length;
    const short = (ms: number, o: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat('en-US', { timeZone: zone, ...o }).format(ms);
    const first = days[0].ms, last = days[6].ms;
    return {
      total: drafted.length,
      published,
      range: short(first, { month: 'short' }) === short(last, { month: 'short' })
        ? `${short(first, { month: 'short', day: 'numeric' })} – ${short(last, { day: 'numeric' })}`
        : `${short(first, { month: 'short', day: 'numeric' })} – ${short(last, { month: 'short', day: 'numeric' })}`,
      bars: days.map((d) => {
        const value = drafted.filter((k) => k === d.key).length;
        return { label: d.label, value, height: value ? Math.min(103, 26 + value * 15) : 3 };
      }),
    };
  });

  protected readonly avatars = AVATARS;

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
  }

  protected ws(path: string): string {
    return `/${this.workspace.slug()}${path}`;
  }

  protected preview(mediaId: string): string {
    return this.mediaApi.previewUrl(this.workspace.orgId()!, mediaId);
  }

  /** Start creating / Create with AI: straight to AI Studio. */
  protected startCreating(): void {
    void this.router.navigate([this.ws('/ai-studio')]);
  }

  protected scrollToActivity(target: HTMLElement): void {
    target.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  }

  protected ago(utc: string): string {
    const minutes = Math.round((Date.now() - utcMs(utc)) / 60000);
    if (minutes < 1) return 'Just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.round(hours / 24);
    return days < 7 ? `${days}d ago` : this.fmt(utc, { month: 'short', day: 'numeric' });
  }

  private fmt(utc: string, options: Intl.DateTimeFormatOptions): string {
    return formatInZone(utc, this.zone(), options);
  }

  private async load(orgId: string): Promise<void> {
    this.problem.set(null);
    try {
      const [dashboard, posts, reviews, audit] = await Promise.all([
        firstValueFrom(this.dashboardApi.get(orgId)),
        firstValueFrom(this.postsApi.listAll(orgId)),
        firstValueFrom(this.reviewsApi.list(orgId)),
        firstValueFrom(this.dashboardApi.audit(orgId)),
      ]);
      this.dashboard.set(dashboard);
      this.posts.set(posts);
      this.reviews.set(reviews);
      const byId = new Map(posts.map((p) => [p.id, p]));
      this.activity.set(audit.map((e) => fromAudit(e, byId)).filter((a): a is Activity => a !== null).slice(0, 3));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }
}
