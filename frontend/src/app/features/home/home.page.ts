import { Component, ViewEncapsulation, computed, effect, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { Member, OrganizationApiService } from '../../core/services/organization-api.service';
import { AppNotification, NotificationApiService } from '../../core/services/notification-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { DELIVERY_LABELS, PROBLEM_DELIVERY, UPCOMING_DELIVERY, customerStatus } from '../../shared/models/states';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { CampaignSummary, SOCIAL_PLATFORMS } from '../campaigns/models/campaign.model';
import { CampaignApiService } from '../campaigns/services/campaign-api.service';
import { PostListItem } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { AuditEntry, Dashboard, DashboardApiService } from '../dashboard/dashboard-api.service';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';

type Range = 'today' | '7' | '30';

interface TodoRow {
  label: string;
  count: number;
  link: string;
  query?: Record<string, string>;
  tone: 'green' | 'amber' | 'red';
}

interface Activity {
  id: string;
  actor: string | null;
  text: string;
  at: string;
}

const SEGMENTS = [
  { key: 'published', label: 'Published', color: '#16a34a' },
  { key: 'scheduled', label: 'Scheduled', color: '#2563eb' },
  { key: 'review', label: 'In Review', color: '#f59e0b' },
  { key: 'changes', label: 'Changes Requested', color: '#8b5cf6' },
  { key: 'failed', label: 'Failed', color: '#ef4444' },
] as const;

/** Home: workspace landing page (quick create, workspace, publishing, to-do, campaigns, accounts, activity). */
@Component({
  selector: 'app-home-page',
  imports: [RouterLink, FormsModule, NgTemplateOutlet],
  templateUrl: './home.page.html',
  styleUrl: './home.page.css',
  // The page is a standalone design; Shadow DOM keeps the app's global element styles out of it.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class HomePage {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly campaignsApi = inject(CampaignApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly notificationsApi = inject(NotificationApiService);
  private readonly organizations = inject(OrganizationApiService);
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly deliveryLabels = DELIVERY_LABELS;
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly posts = signal<PostListItem[]>([]);
  protected readonly campaigns = signal<CampaignSummary[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly members = signal<Member[]>([]);
  protected readonly activity = signal<Activity[]>([]);
  protected readonly range = signal<Range>('7');
  protected readonly todoRange = signal<'today' | 'week'>('today');
  protected readonly unread = signal(0);

  private readonly myId = computed(() => this.auth.me()?.userId ?? null);
  protected readonly firstName = computed(() => (this.auth.me()?.displayName ?? '').trim().split(/\s+/)[0]);
  protected readonly initials = computed(() =>
    (this.auth.me()?.displayName ?? '?').trim().split(/\s+/).slice(0, 2).map((w) => w.charAt(0).toUpperCase()).join(''));

  protected readonly connectedCount = computed(() =>
    (this.dashboard()?.destinations ?? []).filter((a) => a.connectionStatus === 'CONNECTED' && a.executionMode !== 'NOT_CONFIGURED').length);
  protected readonly activeMembers = computed(() => this.members().filter((m) => m.status === 'Active').length);

  protected readonly publishing = computed(() => {
    const zone = this.workspace.timeZone();
    const days = this.range() === 'today' ? 0 : Number(this.range());
    const today = dayKeyInZone(new Date().toISOString(), zone);
    const shift = (n: number) => dayKeyInZone(new Date(Date.now() + n * 864e5).toISOString(), zone);
    const pastFrom = shift(-days);
    const futureTo = shift(days);
    const counts = { published: 0, scheduled: 0, review: 0, changes: 0, failed: 0 };
    for (const p of this.posts()) {
      const state = p.latestDeliveryState;
      const day = p.latestPublishAtUtc ? dayKeyInZone(p.latestPublishAtUtc, zone) : null;
      if (state === 'PUBLISHED' && day && day >= pastFrom && day <= today) counts.published++;
      else if (state && UPCOMING_DELIVERY.includes(state) && day && day <= futureTo) counts.scheduled++;
      else if (state && PROBLEM_DELIVERY.includes(state) && day && day >= pastFrom) counts.failed++;
      if (p.editorialState === 'IN_REVIEW') counts.review++;
      if (p.editorialState === 'CHANGES_REQUESTED') counts.changes++;
    }
    const total = Object.values(counts).reduce((a, b) => a + b, 0);
    // Arcs on a path length of 100 (the ring is rotated so 0 is 12 o'clock), with a small gap between segments.
    const gap = SEGMENTS.filter((s) => counts[s.key] > 0).length > 1 ? 1 : 0;
    let start = 0;
    const segments = SEGMENTS.map((s) => {
      const value = counts[s.key];
      const length = total ? (value / total) * 100 : 0;
      const visible = Math.max(length - gap, 0);
      const arc = { ...s, value, dash: `${visible} ${100 - visible}`, offset: -start };
      start += length;
      return arc;
    });
    return { segments, total };
  });

  protected readonly todo = computed<TodoRow[]>(() => {
    const me = this.myId();
    const since = Date.now() - (this.todoRange() === 'today' ? 1 : 7) * 864e5;
    const recent = (utc: string) => utcMs(utc) >= since;
    const pending = this.reviews().filter((r) => r.status === 'PENDING'
      && (this.workspace.canReview() ? r.assignedReviewerUserId === me || (!r.assignedReviewerUserId && r.submittedByUserId !== me) : r.submittedByUserId === me));
    const overdue = Date.now() - 864e5;
    return [
      { label: 'Need Approval', count: pending.filter((r) => recent(r.submittedAtUtc)).length, link: '/approvals', tone: 'green' },
      // Waiting more than a day for a decision.
      { label: 'Missed Approval', count: pending.filter((r) => utcMs(r.submittedAtUtc) < overdue).length, link: '/approvals', tone: 'amber' },
      { label: 'Rejected Posts', count: this.posts().filter((p) => p.editorialState === 'CHANGES_REQUESTED' && recent(p.updatedAtUtc)).length,
        link: '/planner', query: { status: 'Changes Requested' }, tone: 'red' },
    ];
  });

  protected readonly recentCampaigns = computed(() =>
    this.campaigns().slice(0, 4).map((c) => {
      const counts = new Map<string, number>();
      for (const p of this.posts().filter((x) => x.campaignId === c.id)) {
        const status = customerStatus(p.editorialState, p.latestDeliveryState);
        counts.set(status, (counts.get(status) ?? 0) + 1);
      }
      const summary = [...counts.entries()].map(([status, n]) => `${n} ${status}`).join(' · ');
      return { ...c, summary: summary || 'No posts yet' };
    }));
  protected readonly upcoming = computed(() => (this.dashboard()?.upcoming ?? []).slice(0, 4));

  protected readonly channels = computed(() =>
    SOCIAL_PLATFORMS.map((platform) => {
      const accounts = (this.dashboard()?.destinations ?? []).filter((a) => a.platform === platform && a.executionMode !== 'NOT_CONFIGURED');
      const real = accounts.filter((a) => a.executionMode !== 'SIMULATED');
      const connected = accounts.filter((a) => a.connectionStatus === 'CONNECTED');
      const liveConnected = real.some((a) => a.connectionStatus === 'CONNECTED');
      const status = liveConnected || connected.length ? 'Connected' : real.length ? 'Needs Reconnection' : 'Not Connected';
      return { platform, status, demo: !liveConnected && connected.length > 0, name: (connected[0] ?? real[0])?.displayName ?? null };
    }));

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const admin = this.workspace.isAdmin();
      if (orgId) void this.load(orgId, admin);
    });
  }

  protected when(utc: string): string {
    const zone = this.workspace.timeZone();
    const day = dayKeyInZone(utc, zone);
    const today = dayKeyInZone(new Date().toISOString(), zone);
    const tomorrow = dayKeyInZone(new Date(Date.now() + 864e5).toISOString(), zone);
    const date = day === today ? 'Today' : day === tomorrow ? 'Tomorrow' : formatInZone(utc, zone, { month: 'short', day: 'numeric' });
    return `${date} · ${formatInZone(utc, zone, { timeStyle: 'short' })}`;
  }

  protected ago(utc: string): string {
    const minutes = Math.round((Date.now() - utcMs(utc)) / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes} min ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours} h ago`;
    const days = Math.round(hours / 24);
    if (days === 1) return 'yesterday';
    if (days < 7) return `${days} days ago`;
    return formatInZone(utc, this.workspace.timeZone(), { dateStyle: 'medium' });
  }

  private async load(orgId: string, admin: boolean): Promise<void> {
    this.problem.set(null);
    try {
      const [dashboard, posts, campaigns, reviews, members] = await Promise.all([
        firstValueFrom(this.dashboardApi.get(orgId)),
        firstValueFrom(this.postsApi.listAll(orgId)),
        firstValueFrom(this.campaignsApi.list(orgId)),
        firstValueFrom(this.reviewsApi.list(orgId)),
        firstValueFrom(this.organizations.members(orgId)).catch(() => []),
      ]);
      void firstValueFrom(this.notificationsApi.unreadCount(orgId)).then((r) => this.unread.set(r.count), () => this.unread.set(0));
      this.dashboard.set(dashboard);
      this.posts.set(posts);
      this.campaigns.set(campaigns);
      this.reviews.set(reviews);
      this.members.set(members);
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
    const postName = post ? `a ${post.platform} post` : 'a post';
    const campaign = e.resourceType === 'Campaign' ? this.campaigns().find((c) => c.id === e.resourceId)?.name : null;
    const text = ((): string | null => {
      switch (e.action) {
        case 'WORKSPACE_CREATED': return 'created the workspace';
        case 'BRAND_PROFILE_SAVED': return 'updated the brand profile';
        case 'CAMPAIGN_CREATED': return campaign ? `created campaign “${campaign}”` : 'created a campaign';
        case 'CAMPAIGN_UPDATED': return campaign ? `updated “${campaign}”` : 'updated a campaign';
        case 'POST_VERSION_CREATED': return e.metadata['source']?.startsWith('Ai') ? `generated ${postName} with AI` : `edited ${postName}`;
        case 'REVIEW_SUBMITTED': return `submitted ${postName} for review`;
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
  return { id: n.id, actor: null, text: `${n.title}: ${n.message}`, at: n.createdAtUtc };
}
