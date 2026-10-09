import { Component, ViewEncapsulation, computed, effect, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { AppNotification, NotificationApiService } from '../../core/services/notification-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { MODE_LABELS, PROBLEM_DELIVERY, UPCOMING_DELIVERY, customerStatus } from '../../shared/models/states';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../shared/models/platform.model';
import { PostListItem } from '../content/models/post.model';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { AuditEntry, Dashboard, DashboardApiService } from '../dashboard/dashboard-api.service';
import { formatInZone } from '../publications/models/time-zones';

const DAY = 864e5;
const WEEK = 7 * DAY;

type Tone = 'blue' | 'amber' | 'slate' | 'green' | 'rose' | 'violet';
type Chip = 'sched' | 'review' | 'draft' | 'ready' | 'attn' | 'fail';

interface Kpi {
  label: string;
  value: number;
  tone: Tone;
  icon: 'calendar' | 'clock' | 'doc' | 'bars' | 'trend' | 'pen';
  /** Shown after the value (e.g. %). */
  unit?: string;
  title?: string;
  delta: { text: string; dir: 'up' | 'down' | 'flat'; good: boolean } | null;
  bars: number[];
  /** Which bar is the current period (the darker one). */
  current: number;
  link: string;
  query?: Record<string, string>;
}

interface Row {
  id: string;
  title: string;
  platform: SocialPlatform;
  mediaId: string | null;
  /** Which design illustration stands in when the post has no image (stable per post). */
  art: number;
  chip: Chip;
  chipLabel: string;
  link: string;
  query?: Record<string, string>;
  /** Pipeline: date and time; attention: "by …" and a relative time. */
  date?: string;
  /** Creator home: the reviewer's comment on a post sent back for changes. */
  note?: string;
  time?: string;
  by?: string;
  ago?: string;
}

interface Activity {
  id: string;
  actor: string;
  text: string;
  at: string;
}

const COLORS: Record<Tone, string> = { blue: '#2f6fed', amber: '#e08a1e', slate: '#5b6a86', green: '#12a05f', rose: '#dc4d5b', violet: '#7c5cff' };
const SPARK: Record<SocialPlatform, string> = { LinkedIn: '#12a05f', Facebook: '#2f6fed', Instagram: '#7c5cff' };

/** Chart geometry (SVG user units; the SVG scales to its box). */
const CW = 520, CH = 146, PL = 34, PR = 10, PT = 8, PB = 18;

/**
 * Home: KPIs, the content pipeline, what needs attention, connected channels, publishing activity and recent team
 * activity. Every number comes from the workspace's posts, reviews, publication jobs and accounts; nothing is
 * estimated (the app does not track followers or engagement, so channels and the chart show publishing instead).
 */
@Component({
  selector: 'app-home-page',
  imports: [ProblemBannerComponent, RouterLink, NgTemplateOutlet, FormsModule],
  templateUrl: './home.page.html',
  styleUrl: './home.page.css',
  // The page is a standalone design; Shadow DOM keeps the app's global element styles out of it.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class HomePage {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly notificationsApi = inject(NotificationApiService);
  private readonly mediaApi = inject(MediaApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);
  /** Creator home: the quick-start prompt, handed to AI Studio. */
  protected idea = '';

  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly loaded = signal(false);
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly posts = signal<PostListItem[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly activity = signal<Activity[]>([]);
  protected readonly range = signal<7 | 30 | 90>(30);
  protected readonly hover = signal<number | null>(null);
  protected readonly colors = COLORS;
  protected readonly cw = CW;
  protected readonly ch = CH;

  private readonly zone = computed(() => this.workspace.timeZone());
  private readonly byId = computed(() => new Map(this.posts().map((p) => [p.id, p])));
  /** Creator home works on the signed-in person's own posts only. */
  private readonly mine = computed(() => {
    const me = this.auth.me()?.userId;
    return this.posts().filter((p) => p.createdByUserId === me);
  });

  // ---- KPIs -----------------------------------------------------------------------------------------

  protected readonly kpis = computed<Kpi[]>(() => {
    if (!this.workspace.isAdmin()) return this.creatorKpis();
    const posts = this.posts();
    const now = Date.now();
    const status = (p: PostListItem) => customerStatus(p.editorialState, p.latestDeliveryState);
    const published = posts.filter((p) => p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc).map((p) => p.latestPublishAtUtc!);
    const failed = posts.filter((p) => status(p) === 'Failed');
    const upcoming = posts.filter((p) => status(p) === 'Scheduled' && p.latestPublishAtUtc);
    const drafts = posts.filter((p) => p.editorialState === 'DRAFT');
    const pending = this.reviews().filter((r) => r.status === 'PENDING');
    const admin = this.workspace.isAdmin();
    const success = successRate(posts, now);

    // Scheduled looks ahead: posts going out in each of the next six weeks (this week first).
    const ahead = Array.from({ length: 6 }, (_, i) =>
      upcoming.filter((p) => { const t = utcMs(p.latestPublishAtUtc!); return t >= now + i * WEEK && t < now + (i + 1) * WEEK; }).length);
    return [
      { label: 'Scheduled', title: 'Going out in the next 7 days compared with the 7 days after', value: upcoming.length, tone: 'blue', icon: 'calendar',
        bars: [...ahead].reverse(), current: 5, delta: { ...pct(ahead[0], ahead[1]), good: true }, link: '/planner', query: { status: 'Scheduled' } },
      { label: 'Awaiting approval', value: pending.length, tone: 'amber', icon: 'clock', ...trend(this.reviews().map((r) => r.submittedAtUtc), now, false),
        link: admin ? '/approvals' : '/planner', query: admin ? undefined : { status: 'In Review' } },
      { label: 'Drafts', value: drafts.length, tone: 'slate', icon: 'doc', ...trend(posts.map((p) => p.createdAtUtc), now, true),
        link: '/planner', query: { status: 'Draft' } },
      { label: 'Published', title: 'Published in the last 30 days', value: published.filter((t) => utcMs(t) >= now - 30 * DAY).length, tone: 'green',
        icon: 'bars', ...trend(published, now, true), link: '/planner', query: { status: 'Published' } },
      { label: 'Publish success', title: 'Published out of all publish attempts that finished (not scheduled)', unit: '%', tone: 'violet', icon: 'trend',
        value: success.rate, delta: success.delta, bars: success.bars, current: 5, link: '/planner', query: { status: 'Failed' } },
    ];
  });

  /**
   * Creator home: only their own posts, in the order a writer works through them: drafts, sent back, waiting for the
   * Admin, approved (the Admin schedules it) and published.
   */
  private readonly creatorKpis = computed<Kpi[]>(() => {
    const posts = this.mine();
    const now = Date.now();
    const status = (p: PostListItem) => customerStatus(p.editorialState, p.latestDeliveryState);
    const updated = (list: PostListItem[]) => list.map((p) => p.updatedAtUtc);
    const drafts = posts.filter((p) => p.editorialState === 'DRAFT');
    const changes = posts.filter((p) => p.editorialState === 'CHANGES_REQUESTED');
    const review = posts.filter((p) => p.editorialState === 'IN_REVIEW');
    const approved = posts.filter((p) => ['Approved', 'Scheduled'].includes(status(p)));
    const published = posts.filter((p) => p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc).map((p) => p.latestPublishAtUtc!);
    return [
      { label: 'My drafts', value: drafts.length, tone: 'slate', icon: 'doc', ...trend(drafts.map((p) => p.createdAtUtc), now, true),
        link: '/planner', query: { status: 'Draft' } },
      { label: 'Changes requested', title: 'Sent back by the Admin: edit and submit again', value: changes.length, tone: 'rose', icon: 'pen',
        ...trend(updated(changes), now, false), link: '/planner', query: { status: 'Changes Requested' } },
      { label: 'Waiting for review', value: review.length, tone: 'amber', icon: 'clock', ...trend(updated(review), now, false),
        link: '/planner', query: { status: 'In Review' } },
      { label: 'Approved', title: 'Approved; the Admin schedules or publishes them', value: approved.length, tone: 'blue', icon: 'calendar',
        ...trend(updated(approved), now, true), link: '/planner', query: { status: 'Scheduled' } },
      { label: 'Published', title: 'Your posts published in the last 30 days', value: published.filter((t) => utcMs(t) >= now - 30 * DAY).length,
        tone: 'green', icon: 'bars', ...trend(published, now, true), link: '/planner', query: { status: 'Published' } },
    ];
  });

  /** Creator home: posts the Admin sent back, newest first, with the reviewer's comment. */
  protected readonly changesRequested = computed<Row[]>(() => {
    const comments = new Map(this.reviews().filter((r) => r.status === 'CHANGES_REQUESTED')
      // Oldest first, so the latest decision per post wins in the map.
      .sort((a, b) => (a.decidedAtUtc ?? '').localeCompare(b.decidedAtUtc ?? ''))
      .map((r) => [r.postId, { note: r.decisionComment ?? '', by: r.decidedByName ?? 'Admin' }]));
    return this.mine().filter((p) => p.editorialState === 'CHANGES_REQUESTED')
      .sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)).slice(0, 3)
      .map((p) => ({ id: p.id, title: p.hook || p.bodyExcerpt, platform: p.platform, mediaId: p.mediaAssetId ?? null, art: art(p.id), chip: 'attn' as Chip,
        chipLabel: 'Edit', link: `/composer/${p.id}`, by: comments.get(p.id)?.by ?? 'Admin', note: comments.get(p.id)?.note, ago: this.ago(p.updatedAtUtc) }));
  });

  /** Creator home: the drafts touched most recently, to pick up where they left off. */
  protected readonly continueWriting = computed<Row[]>(() =>
    this.mine().filter((p) => p.editorialState === 'DRAFT').sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)).slice(0, 4)
      .map((p) => ({ id: p.id, title: p.hook || p.bodyExcerpt, platform: p.platform, mediaId: p.mediaAssetId ?? null, art: art(p.id), chip: 'draft' as Chip,
        chipLabel: 'Draft', link: `/composer/${p.id}`, ago: this.ago(p.updatedAtUtc) })));

  /** Creator home: their posts on the way out (in review, approved, scheduled), soonest first. */
  protected readonly myUpcoming = computed<Row[]>(() => {
    const order: Record<string, number> = { Scheduled: 0, Approved: 1, 'In Review': 2 };
    return this.mine()
      .map((p) => ({ p, s: customerStatus(p.editorialState, p.latestDeliveryState) }))
      .filter(({ s }) => s in order)
      .sort((a, b) => order[a.s] - order[b.s]
        || (a.s === 'Scheduled' ? a.p.latestPublishAtUtc!.localeCompare(b.p.latestPublishAtUtc!) : b.p.updatedAtUtc.localeCompare(a.p.updatedAtUtc)))
      .slice(0, 4)
      .map(({ p, s }) => {
        const at = s === 'Scheduled' && p.latestPublishAtUtc ? p.latestPublishAtUtc : p.updatedAtUtc;
        const [chip, chipLabel]: [Chip, string] = s === 'Scheduled' ? ['sched', 'Scheduled'] : s === 'Approved' ? ['ready', 'Approved'] : ['review', 'In review'];
        return { id: p.id, title: p.hook || p.bodyExcerpt, platform: p.platform, mediaId: p.mediaAssetId ?? null, art: art(p.id), chip, chipLabel,
          link: `/composer/${p.id}`, date: this.fmt(at, { month: 'short', day: 'numeric', year: 'numeric' }), time: this.fmt(at, { timeStyle: 'short' }) };
      });
  });

  /** Quick start: the idea goes to AI Studio as its prompt; empty opens a blank Composer. */
  protected start(): void {
    const idea = this.idea.trim();
    void (idea
      ? this.router.navigate([this.ws('/ai-studio')], { queryParams: { prompt: idea } })
      : this.router.navigateByUrl(this.ws('/composer')));
  }

  // ---- Content pipeline ----------------------------------------------------------------------------

  /** What is coming: scheduled posts by publish time, then approved-but-unscheduled, in review, changes and drafts. */
  protected readonly pipeline = computed<Row[]>(() => {
    const order: Record<string, number> = { Scheduled: 0, Approved: 1, 'In Review': 2, 'Changes Requested': 3, Draft: 4 };
    return this.posts()
      .map((p) => ({ p, s: customerStatus(p.editorialState, p.latestDeliveryState) }))
      .filter(({ s }) => s in order)
      .sort((a, b) => order[a.s] - order[b.s]
        || (a.s === 'Scheduled' ? a.p.latestPublishAtUtc!.localeCompare(b.p.latestPublishAtUtc!) : b.p.updatedAtUtc.localeCompare(a.p.updatedAtUtc)))
      .slice(0, 4)
      .map(({ p, s }) => {
        const at = s === 'Scheduled' && p.latestPublishAtUtc ? p.latestPublishAtUtc : p.updatedAtUtc;
        const [chip, chipLabel]: [Chip, string] = s === 'Scheduled' ? ['sched', 'Scheduled'] : s === 'Approved' ? ['ready', 'Ready']
          : s === 'In Review' ? ['review', 'Review'] : s === 'Changes Requested' ? ['attn', 'Changes'] : ['draft', 'Draft'];
        return { id: p.id, title: p.hook || p.bodyExcerpt, platform: p.platform, mediaId: p.mediaAssetId ?? null, art: art(p.id), chip, chipLabel,
          link: `/composer/${p.id}`, date: this.fmt(at, { month: 'short', day: 'numeric', year: 'numeric' }), time: this.fmt(at, { timeStyle: 'short' }) };
      });
  });

  // ---- Needs attention -----------------------------------------------------------------------------

  protected readonly attention = computed<Row[]>(() => {
    const admin = this.workspace.isAdmin();
    const posts = this.byId();
    const reviews: Row[] = admin
      ? this.reviews().filter((r) => r.status === 'PENDING').map((r) => ({
          id: r.id, title: r.excerpt, platform: r.platform, mediaId: posts.get(r.postId)?.mediaAssetId ?? null, art: art(r.postId), chip: 'attn' as Chip,
          chipLabel: 'Needs approval', link: `/approvals/${r.id}`, by: r.submittedByName, ago: this.ago(r.submittedAtUtc) }))
      : this.posts().filter((p) => p.editorialState === 'CHANGES_REQUESTED').map((p) => ({
          id: p.id, title: p.hook || p.bodyExcerpt, platform: p.platform, mediaId: p.mediaAssetId ?? null, art: art(p.id), chip: 'attn' as Chip,
          chipLabel: 'Changes requested', link: `/composer/${p.id}`, by: p.createdByName, ago: this.ago(p.updatedAtUtc) }));
    const failed: Row[] = this.posts()
      .filter((p) => p.latestDeliveryState && PROBLEM_DELIVERY.includes(p.latestDeliveryState))
      .map((p) => ({ id: p.id, title: p.hook || p.bodyExcerpt, platform: p.platform, mediaId: p.mediaAssetId ?? null, art: art(p.id), chip: 'fail' as Chip,
        chipLabel: 'Not published', link: admin ? '/approvals' : '/planner', query: admin ? undefined : { status: 'Failed' },
        by: p.createdByName, ago: this.ago(p.latestPublishAtUtc ?? p.updatedAtUtc) }));
    return [...failed, ...reviews].slice(0, 2);
  });

  // ---- Channels ------------------------------------------------------------------------------------

  /** Per platform: connection status and posts published (the app does not read follower counts). */
  protected readonly channels = computed(() => {
    const accounts = this.dashboard()?.destinations ?? [];
    const now = Date.now();
    return SOCIAL_PLATFORMS.map((platform) => {
      const account = accounts.find((a) => a.platform === platform && a.isEligibleDestination) ?? null;
      const times = this.posts().filter((p) => p.platform === platform && p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc)
        .map((p) => utcMs(p.latestPublishAtUtc!));
      const last30 = times.filter((t) => t >= now - 30 * DAY).length;
      const prev30 = times.filter((t) => t >= now - 60 * DAY && t < now - 30 * DAY).length;
      const weeks = Array.from({ length: 8 }, (_, i) => times.filter((t) => t >= now - (8 - i) * WEEK && t < now - (7 - i) * WEEK).length);
      return {
        platform,
        status: account ? MODE_LABELS[account.executionMode] ?? 'Connected' : 'Not connected',
        connected: !!account,
        published: last30,
        delta: pct(last30, prev30),
        spark: sparkline(weeks, 120, 32),
        color: SPARK[platform],
      };
    });
  });

  // ---- Publishing activity chart -------------------------------------------------------------------

  protected readonly chart = computed(() => {
    const range = this.range();
    const step = range === 7 ? DAY : range === 30 ? 2 * DAY : WEEK;
    const n = range === 7 ? 7 : range === 30 ? 15 : 13;
    const now = Date.now();
    const times = this.posts().filter((p) => p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc).map((p) => utcMs(p.latestPublishAtUtc!));
    // Rolling windows that end now; each point counts the posts published in its window.
    const keys = Array.from({ length: n }, (_, i) => now - (n - 1 - i) * step);
    const values = keys.map((end) => times.filter((t) => t > end - step && t <= end).length);
    const total = times.filter((t) => t > now - range * DAY).length;
    const before = times.filter((t) => t > now - 2 * range * DAY && t <= now - range * DAY).length;
    const top = Math.max(2, Math.ceil(Math.max(...values) / 2) * 2);
    const iw = CW - PL - PR, ih = CH - PT - PB;
    const x = (i: number) => PL + (i * iw) / (n - 1);
    const y = (v: number) => PT + ih - (v / top) * ih;
    const pts: [number, number][] = values.map((v, i) => [x(i), y(v)]);
    const line = smooth(pts, PT + ih);
    return {
      n, values, keys, step, total, delta: pct(total, before), x, y,
      line, area: `${line} L${x(n - 1)},${PT + ih} L${PL},${PT + ih} Z`,
      ticks: [0, top / 2, top].map((v) => ({ v, y: y(v) })),
      end: pts[n - 1], base: PT + ih,
    };
  });

  protected readonly tip = computed(() => {
    const i = this.hover();
    const c = this.chart();
    if (i === null) return null;
    const day = (t: number) => this.fmt(new Date(t).toISOString(), { month: 'short', day: 'numeric' });
    const end = c.keys[i];
    const label = c.step === DAY
      ? this.fmt(new Date(end).toISOString(), { weekday: 'short', month: 'short', day: 'numeric' })
      : `${day(end - c.step + DAY)} – ${day(end)}`;
    return { x: c.x(i), y: c.y(c.values[i]), left: (c.x(i) / CW) * 100, top: (c.y(c.values[i]) / CH) * 100, label, value: c.values[i] };
  });

  protected onChartMove(event: MouseEvent): void {
    const rect = (event.currentTarget as Element).getBoundingClientRect();
    const sx = ((event.clientX - rect.left) / rect.width) * CW;
    const c = this.chart();
    const step = (CW - PL - PR) / Math.max(1, c.n - 1);
    this.hover.set(Math.max(0, Math.min(c.n - 1, Math.round((sx - PL) / step))));
  }

  protected setRange(value: string): void {
    this.range.set(Number(value) as 7 | 30 | 90);
    this.hover.set(null);
  }

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const admin = this.workspace.isAdmin();
      if (orgId) void this.load(orgId, admin);
    });
  }

  protected ws(path: string): string {
    return `/${this.workspace.slug()}${path}`;
  }

  protected preview(mediaId: string): string {
    return this.mediaApi.previewUrl(this.workspace.orgId()!, mediaId);
  }

  protected kbar(values: number[], i: number): { y: number; h: number } {
    const max = Math.max(1, ...values);
    const h = 4 + (values[i] / max) * 22;
    return { y: 30 - h, h };
  }

  protected avatar(name: string): string {
    return ['r', 'b', 'g'][name.charCodeAt(0) % 3];
  }

  protected fresh(utc: string): boolean {
    return Date.now() - utcMs(utc) < 60 * 60 * 1000;
  }

  protected ago(utc: string): string {
    const minutes = Math.round((Date.now() - utcMs(utc)) / 60000);
    if (minutes < 1) return 'now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.round(hours / 24);
    if (days < 7) return `${days}d ago`;
    return this.fmt(utc, { month: 'short', day: 'numeric' });
  }

  private fmt(utc: string, options: Intl.DateTimeFormatOptions): string {
    return formatInZone(utc, this.zone(), options);
  }

  private async load(orgId: string, admin: boolean): Promise<void> {
    this.problem.set(null);
    try {
      const [dashboard, posts, reviews] = await Promise.all([
        firstValueFrom(this.dashboardApi.get(orgId)),
        firstValueFrom(this.postsApi.listAll(orgId)),
        firstValueFrom(this.reviewsApi.list(orgId)),
      ]);
      this.dashboard.set(dashboard);
      this.posts.set(posts);
      this.reviews.set(reviews);
      // Admins see the workspace's business events; others see their own notifications.
      this.activity.set(admin
        ? (await firstValueFrom(this.dashboardApi.audit(orgId))).map((e) => this.fromAudit(e)).filter((a): a is Activity => a !== null).slice(0, 3)
        : (await firstValueFrom(this.notificationsApi.list(orgId))).slice(0, 5).map(fromNotification));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loaded.set(true);
    }
  }

  /** Business events in plain language; technical events are left out. */
  private fromAudit(e: AuditEntry): Activity | null {
    const post = e.resourceType === 'Post' ? this.byId().get(e.resourceId) : null;
    const postName = post ? `a ${post.platform} post` : 'a post';
    const text = ((): string | null => {
      switch (e.action) {
        case 'WORKSPACE_CREATED': return 'created the workspace';
        case 'BRAND_PROFILE_SAVED': return 'updated the brand profile';
        case 'CAMPAIGN_CREATED': return 'created a campaign';
        case 'CAMPAIGN_UPDATED': return 'updated a campaign';
        case 'POST_VERSION_CREATED': return e.metadata['source']?.startsWith('Ai') ? `generated ${postName}` : `edited ${postName}`;
        case 'REVIEW_SUBMITTED': return 'submitted content for review';
        case 'CONTENT_APPROVED': return `approved ${postName}`;
        case 'CHANGES_REQUESTED': return `requested changes on ${postName}`;
        case 'PUBLICATION_SCHEDULED': return `scheduled ${postName}`;
        case 'PUBLICATION_PUBLISHED': return `published ${postName}`;
        case 'PUBLICATION_FAILED': return `could not publish ${postName}`;
        case 'SOCIAL_CONNECTION_CONNECTED': return 'connected a social account';
        case 'MEMBER_ADDED': return 'added a team member';
        default: return null;
      }
    })();
    return text ? { id: e.id, actor: e.actorName ?? 'Someone', text, at: e.createdAtUtc } : null;
  }
}

/** API timestamps are UTC, sometimes without a zone suffix. */
function utcMs(utc: string): number {
  return Date.parse(/(Z|[+-]\d{2}:\d{2})$/i.test(utc) ? utc : `${utc}Z`);
}

function fromNotification(n: AppNotification): Activity {
  return { id: n.id, actor: 'You', text: n.title, at: n.createdAtUtc };
}

/** One of the six design illustrations, picked from the id so a post always gets the same one. */
function art(id: string): number {
  let h = 0;
  for (const c of id) h = (h * 31 + c.charCodeAt(0)) | 0;
  return Math.abs(h) % 6;
}

/**
 * Share of finished publications that were published (failed ones count against it), over the last 30 days; weekly
 * rates for the bars. 100% when nothing failed, and 0 with no finished publications at all.
 */
function successRate(posts: PostListItem[], now: number): { rate: number; delta: Kpi['delta']; bars: number[] } {
  const done = posts.filter((p) => p.latestPublishAtUtc && (p.latestDeliveryState === 'PUBLISHED' || PROBLEM_DELIVERY.includes(p.latestDeliveryState ?? '')))
    .map((p) => ({ ok: p.latestDeliveryState === 'PUBLISHED', t: utcMs(p.latestPublishAtUtc!) }));
  const rate = (from: number, to: number) => {
    const inRange = done.filter((d) => d.t > from && d.t <= to);
    return inRange.length ? Math.round((inRange.filter((d) => d.ok).length / inRange.length) * 100) : 0;
  };
  const bars = Array.from({ length: 6 }, (_, i) => rate(now - (6 - i) * WEEK, now - (5 - i) * WEEK));
  const current = rate(now - 30 * DAY, now), before = rate(now - 60 * DAY, now - 30 * DAY);
  const diff = current - before;
  return { rate: current, bars, delta: { text: `${Math.abs(diff)}%`, dir: diff > 0 ? 'up' : diff < 0 ? 'down' : 'flat', good: diff >= 0 } };
}

/** Change from `before` to `now` as a percentage ("+3" when there was nothing before). */
function pct(now: number, before: number): { text: string; dir: 'up' | 'down' | 'flat' } {
  if (!before) return now ? { text: `+${now}`, dir: 'up' } : { text: '0', dir: 'flat' };
  const p = Math.round(((now - before) / before) * 100);
  return { text: `${Math.abs(p)}%`, dir: p > 0 ? 'up' : p < 0 ? 'down' : 'flat' };
}

/** Six weekly buckets ending now, and this week compared with last week. `upIsGood` colours the change. */
function trend(times: string[], now: number, upIsGood: boolean): Pick<Kpi, 'bars' | 'current' | 'delta'> {
  const ms = times.map(utcMs);
  const bars = Array.from({ length: 6 }, (_, i) => ms.filter((t) => t > now - (6 - i) * WEEK && t <= now - (5 - i) * WEEK).length);
  const d = pct(bars[5], bars[4]);
  return { bars, current: 5, delta: { ...d, good: d.dir === 'flat' || (d.dir === 'up') === upIsGood } };
}

function sparkline(values: number[], w: number, h: number): { line: string; area: string; end: [number, number] } {
  const max = Math.max(1, ...values);
  const pts: [number, number][] = values.map((v, i) => [(i * w) / (values.length - 1), h - 4 - (v / max) * (h - 8)]);
  const line = smooth(pts, h);
  return { line, area: `${line} L${w},${h} L0,${h} Z`, end: pts[pts.length - 1] };
}

/** Smooth path through the points (Catmull-Rom as cubic Béziers); control points never dip below the baseline. */
function smooth(pts: [number, number][], baseline: number): string {
  if (!pts.length) return '';
  let d = `M${pts[0][0].toFixed(1)},${pts[0][1].toFixed(1)}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] ?? pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] ?? p2;
    const c1 = [p1[0] + (p2[0] - p0[0]) / 6, Math.min(baseline, p1[1] + (p2[1] - p0[1]) / 6)];
    const c2 = [p2[0] - (p3[0] - p1[0]) / 6, Math.min(baseline, p2[1] - (p3[1] - p1[1]) / 6)];
    d += ` C${c1[0].toFixed(1)},${c1[1].toFixed(1)} ${c2[0].toFixed(1)},${c2[1].toFixed(1)} ${p2[0].toFixed(1)},${p2[1].toFixed(1)}`;
  }
  return d;
}
