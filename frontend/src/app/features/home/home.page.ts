import { Component, ViewEncapsulation, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { AppNotification, NotificationApiService } from '../../core/services/notification-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { PROBLEM_DELIVERY, UPCOMING_DELIVERY } from '../../shared/models/states';
import { ReviewSummary } from '../approvals/models/review.model';
import { SOCIAL_PLATFORMS } from '../campaigns/models/campaign.model';
import { PostListItem } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { AuditEntry, Dashboard, DashboardApiService } from '../dashboard/dashboard-api.service';
import { PublicationJob } from '../publications/models/publication.model';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';

const DAY = 864e5;

interface Kpi {
  label: string;
  value: number;
  unit: string;
  accent: 'green' | 'blue' | 'amber' | 'rose';
  icon: 'check' | 'calendar' | 'clock' | 'alert';
  delta: string;
  dir: 'up' | 'down' | 'flat';
  sub: string;
  link: string;
  query?: Record<string, string>;
}

interface TodoRow {
  label: string;
  count: number;
  link: string;
  query?: Record<string, string>;
  tile: 'amber' | 'blue' | 'orange' | 'rose';
  icon: 'clock' | 'check' | 'alert' | 'reject';
}

interface Activity {
  id: string;
  actor: string | null;
  text: string;
  at: string;
}

/** Chart geometry (SVG user units). */
const CW = 780, CH = 250, PL = 40, PR = 16, PT = 16, PB = 30;

const SEGMENTS = [
  { key: 'published', label: 'Published', color: '#22c55e' },
  { key: 'scheduled', label: 'Scheduled', color: '#3b82f6' },
  { key: 'review', label: 'In Review', color: '#f59e0b' },
  { key: 'changes', label: 'Changes', color: '#8b5cf6' },
  { key: 'failed', label: 'Failed', color: '#ef4444' },
] as const;

/**
 * Home: KPI cards, a posting-activity chart, publishing breakdown, to-do, upcoming publications and recent activity.
 * Every number comes from the workspace's posts, reviews and publication jobs; nothing is estimated or invented.
 */
@Component({
  selector: 'app-home-page',
  imports: [ProblemBannerComponent, RouterLink],
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
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly posts = signal<PostListItem[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly activity = signal<Activity[]>([]);
  protected readonly chartRange = signal<7 | 30 | 90>(30);
  protected readonly pubRange = signal<7 | 30>(7);
  /** Index of the hovered day in the chart, or null. */
  protected readonly hover = signal<number | null>(null);
  protected readonly cw = CW;
  protected readonly ch = CH;

  private readonly zone = computed(() => this.workspace.timeZone());
  private readonly today = computed(() => dayKeyInZone(new Date().toISOString(), this.zone()));

  // ---- KPI cards ----------------------------------------------------------------------------------

  protected readonly kpis = computed<Kpi[]>(() => {
    const posts = this.posts();
    const now = Date.now();
    const publishedBetween = (from: number, to: number) => posts.filter((p) => p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc
      && utcMs(p.latestPublishAtUtc) >= from && utcMs(p.latestPublishAtUtc) < to).length;
    const published = publishedBetween(now - 30 * DAY, now + 1);
    const before = publishedBetween(now - 60 * DAY, now - 30 * DAY);
    const scheduled = posts.filter((p) => p.latestDeliveryState && UPCOMING_DELIVERY.includes(p.latestDeliveryState)).length;
    const thisWeek = posts.filter((p) => p.latestDeliveryState && UPCOMING_DELIVERY.includes(p.latestDeliveryState) && p.latestPublishAtUtc
      && utcMs(p.latestPublishAtUtc) < now + 7 * DAY).length;
    const pending = this.reviews().filter((r) => r.status === 'PENDING');
    const fresh = pending.filter((r) => utcMs(r.submittedAtUtc) >= now - DAY).length;
    const failed = posts.filter((p) => p.latestDeliveryState && PROBLEM_DELIVERY.includes(p.latestDeliveryState)).length;
    const pct = before ? Math.round(((published - before) / before) * 1000) / 10 : null;
    return [
      { label: 'Published', value: published, unit: 'posts', accent: 'green', icon: 'check',
        delta: pct === null ? (published ? `+${published}` : '0') : `${pct > 0 ? '+' : ''}${pct}%`,
        dir: pct === null ? (published ? 'up' : 'flat') : pct > 0 ? 'up' : pct < 0 ? 'down' : 'flat',
        sub: 'last 30 days', link: '/planner', query: { status: 'Published' } },
      { label: 'Scheduled', value: scheduled, unit: 'queued', accent: 'blue', icon: 'calendar',
        delta: `${thisWeek} this week`, dir: 'flat', sub: 'waiting to go out', link: '/planner', query: { status: 'Scheduled' } },
      { label: 'In review', value: pending.length, unit: 'waiting', accent: 'amber', icon: 'clock',
        delta: `${fresh} new`, dir: fresh ? 'up' : 'flat', sub: 'needs an approver',
        link: this.workspace.isAdmin() ? '/approvals' : '/planner', query: this.workspace.isAdmin() ? undefined : { status: 'In Review' } },
      { label: 'Failed', value: failed, unit: failed === 1 ? 'error' : 'errors', accent: 'rose', icon: 'alert',
        delta: failed ? 'needs action' : 'all clear', dir: failed ? 'down' : 'flat',
        sub: this.workspace.isAdmin() ? 'check Approvals or Settings' : 'your Admin is notified', link: '/planner', query: { status: 'Failed' } },
    ];
  });

  // ---- Posting activity chart ---------------------------------------------------------------------

  /** Per day in the workspace zone: posts published and posts drafted (created). */
  protected readonly chart = computed(() => {
    const days = this.chartRange();
    const zone = this.zone();
    const keys = Array.from({ length: days }, (_, i) => dayKeyInZone(new Date(Date.now() - (days - 1 - i) * DAY).toISOString(), zone));
    const index = new Map(keys.map((k, i) => [k, i]));
    const published = keys.map(() => 0);
    const drafted = keys.map(() => 0);
    for (const p of this.posts()) {
      const created = index.get(dayKeyInZone(p.createdAtUtc, zone));
      if (created !== undefined) drafted[created]++;
      if (p.latestDeliveryState === 'PUBLISHED' && p.latestPublishAtUtc) {
        const done = index.get(dayKeyInZone(p.latestPublishAtUtc, zone));
        if (done !== undefined) published[done]++;
      }
    }
    const max = Math.max(4, ...published, ...drafted);
    const top = Math.ceil(max / 4) * 4;
    const innerW = CW - PL - PR, innerH = CH - PT - PB;
    const x = (i: number) => PL + (i * innerW) / (days - 1);
    const y = (v: number) => PT + innerH - (v / top) * innerH;
    const line = (values: number[]) => smooth(values.map((v, i) => [x(i), y(v)]), PT + innerH);
    const area = (d: string) => `${d} L${CW - PR},${PT + innerH} L${PL},${PT + innerH} Z`;
    const pubLine = line(published), draftLine = line(drafted);
    const every = Math.max(1, Math.round(days / 6));
    return {
      keys, published, drafted, x, y, innerW, innerH,
      pubLine, draftLine, pubArea: area(pubLine), draftArea: area(draftLine),
      grid: Array.from({ length: 5 }, (_, t) => ({ y: PT + (innerH / 4) * t, label: top - (top / 4) * t })),
      labels: keys.map((k, i) => ({ i, k })).filter(({ i }) => i % every === 0).map(({ i, k }) => ({ x: x(i), text: shortDay(k) })),
      totalPublished: published.reduce((a, b) => a + b, 0),
      totalDrafted: drafted.reduce((a, b) => a + b, 0),
    };
  });

  protected readonly tip = computed(() => {
    const i = this.hover();
    const c = this.chart();
    if (i === null) return null;
    return { x: c.x(i), left: (c.x(i) / CW) * 100, date: shortDay(c.keys[i]), published: c.published[i], drafted: c.drafted[i],
      yPub: c.y(c.published[i]), yDraft: c.y(c.drafted[i]) };
  });

  protected onChartMove(event: MouseEvent): void {
    const svg = (event.currentTarget as SVGElement).ownerSVGElement ?? (event.currentTarget as SVGSVGElement);
    const rect = svg.getBoundingClientRect();
    const sx = ((event.clientX - rect.left) / rect.width) * CW;
    const days = this.chartRange();
    const step = (CW - PL - PR) / Math.max(1, days - 1);
    this.hover.set(Math.max(0, Math.min(days - 1, Math.round((sx - PL) / step))));
  }

  // ---- Publishing card ----------------------------------------------------------------------------

  protected readonly publishing = computed(() => {
    const zone = this.zone();
    const days = this.pubRange();
    const today = this.today();
    const shift = (n: number) => dayKeyInZone(new Date(Date.now() + n * DAY).toISOString(), zone);
    const pastFrom = shift(-days), futureTo = shift(days);
    const counts = { published: 0, scheduled: 0, review: 0, changes: 0, failed: 0 };
    const perPlatform = new Map<string, number>();
    for (const p of this.posts()) {
      const state = p.latestDeliveryState;
      const day = p.latestPublishAtUtc ? dayKeyInZone(p.latestPublishAtUtc, zone) : null;
      let counted = true;
      if (state === 'PUBLISHED' && day && day >= pastFrom && day <= today) counts.published++;
      else if (state && UPCOMING_DELIVERY.includes(state) && day && day <= futureTo) counts.scheduled++;
      else if (state && PROBLEM_DELIVERY.includes(state) && day && day >= pastFrom) counts.failed++;
      else if (p.editorialState === 'IN_REVIEW') counts.review++;
      else if (p.editorialState === 'CHANGES_REQUESTED') counts.changes++;
      else counted = false;
      if (counted) perPlatform.set(p.platform, (perPlatform.get(p.platform) ?? 0) + 1);
    }
    const total = Object.values(counts).reduce((a, b) => a + b, 0);
    return {
      total,
      segments: SEGMENTS.map((s) => ({ ...s, value: counts[s.key], width: total ? (counts[s.key] / total) * 100 : 0 })),
      platforms: SOCIAL_PLATFORMS.map((platform) => ({ platform, count: perPlatform.get(platform) ?? 0 })),
    };
  });

  /** This month: published out of everything planned for the month (published + still scheduled). */
  protected readonly month = computed(() => {
    const zone = this.zone();
    const month = this.today().slice(0, 7);
    let done = 0, planned = 0;
    for (const p of this.posts()) {
      if (!p.latestPublishAtUtc || dayKeyInZone(p.latestPublishAtUtc, zone).slice(0, 7) !== month) continue;
      if (p.latestDeliveryState === 'PUBLISHED') { done++; planned++; }
      else if (p.latestDeliveryState && UPCOMING_DELIVERY.includes(p.latestDeliveryState)) planned++;
    }
    return { done, planned, pct: planned ? Math.round((done / planned) * 100) : 0 };
  });

  // ---- To do, upcoming, activity ------------------------------------------------------------------

  protected readonly todo = computed<TodoRow[]>(() => {
    const pending = this.reviews().filter((r) => r.status === 'PENDING');
    const overdue = Date.now() - DAY;
    const admin = this.workspace.isAdmin();
    return [
      { label: 'Drafts', count: this.posts().filter((p) => p.editorialState === 'DRAFT').length, link: '/planner', query: { status: 'Draft' }, tile: 'amber', icon: 'clock' },
      admin
        ? { label: 'To approve', count: pending.length, link: '/approvals', tile: 'blue', icon: 'check' }
        // Creators do not open Approvals; they follow their submitted posts in the Planner.
        : { label: 'Waiting for approval', count: pending.length, link: '/planner', query: { status: 'In Review' }, tile: 'blue', icon: 'check' },
      // Waiting more than a day for a decision.
      { label: 'Missed', count: pending.filter((r) => utcMs(r.submittedAtUtc) < overdue).length,
        link: admin ? '/approvals' : '/planner', query: admin ? undefined : { status: 'In Review' }, tile: 'orange', icon: 'alert' },
      { label: 'Rejected', count: this.posts().filter((p) => p.editorialState === 'CHANGES_REQUESTED').length,
        link: '/planner', query: { status: 'Changes Requested' }, tile: 'rose', icon: 'reject' },
    ];
  });

  /** Problems first (flagged), then the next publications. */
  protected readonly upcoming = computed(() => {
    const d = this.dashboard();
    const attention = (d?.needsAttention ?? []).map((job) => ({ job, flag: true }));
    const next = (d?.upcoming ?? []).filter((j) => !attention.some((a) => a.job.id === j.id)).map((job) => ({ job, flag: false }));
    return [...attention, ...next].slice(0, 4);
  });

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const admin = this.workspace.isAdmin();
      if (orgId) void this.load(orgId, admin);
    });
  }

  protected jobTitle(job: PublicationJob): string {
    const post = this.posts().find((p) => p.id === job.postId);
    return post ? post.hook || post.bodyExcerpt : `${job.platform} post`;
  }

  protected when(utc: string): string {
    const zone = this.zone();
    const day = dayKeyInZone(utc, zone);
    const tomorrow = dayKeyInZone(new Date(Date.now() + DAY).toISOString(), zone);
    const date = day === this.today() ? 'Today' : day === tomorrow ? 'Tomorrow' : formatInZone(utc, zone, { month: 'short', day: 'numeric' });
    return `${date} ${formatInZone(utc, zone, { timeStyle: 'short' })}`;
  }

  protected ago(utc: string): string {
    const minutes = Math.round((Date.now() - utcMs(utc)) / 60000);
    if (minutes < 1) return 'now';
    if (minutes < 60) return `${minutes}m`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h`;
    const days = Math.round(hours / 24);
    if (days < 7) return `${days}d`;
    return formatInZone(utc, this.zone(), { month: 'short', day: 'numeric' });
  }

  protected initial(name: string | null): string {
    return (name ?? '•').trim().charAt(0).toUpperCase() || '•';
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
        ? (await firstValueFrom(this.dashboardApi.audit(orgId))).map((e) => this.fromAudit(e)).filter((a): a is Activity => a !== null).slice(0, 5)
        : (await firstValueFrom(this.notificationsApi.list(orgId))).slice(0, 5).map(fromNotification));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  /** Business events in plain language; technical events are left out. */
  private fromAudit(e: AuditEntry): Activity | null {
    const post = e.resourceType === 'Post' ? this.posts().find((p) => p.id === e.resourceId) : null;
    const postName = post ? `${post.platform} post` : 'a post';
    const text = ((): string | null => {
      switch (e.action) {
        case 'WORKSPACE_CREATED': return 'created the workspace';
        case 'BRAND_PROFILE_SAVED': return 'updated the brand profile';
        case 'CAMPAIGN_CREATED': return 'created a campaign';
        case 'CAMPAIGN_UPDATED': return 'updated a campaign';
        case 'POST_VERSION_CREATED': return e.metadata['source']?.startsWith('Ai') ? `generated ${postName}` : `edited ${postName}`;
        case 'REVIEW_SUBMITTED': return 'submitted for review';
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
  return { id: n.id, actor: null, text: n.title, at: n.createdAtUtc };
}

/** "2026-10-06" → "Oct 6". */
function shortDay(key: string): string {
  const [y, m, d] = key.split('-').map(Number);
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, d)));
}

/** Smooth path through the points (Catmull-Rom as cubic Béziers); control points never dip below the baseline (zero). */
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
