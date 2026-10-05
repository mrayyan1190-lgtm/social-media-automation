import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { Member, OrganizationApiService } from '../../core/services/organization-api.service';
import { AppNotification, NotificationApiService } from '../../core/services/notification-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { DELIVERY_LABELS, PROBLEM_DELIVERY, UPCOMING_DELIVERY, customerStatus, deliveryTone } from '../../shared/models/states';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { CampaignSummary, SOCIAL_PLATFORMS, SocialPlatform } from '../campaigns/models/campaign.model';
import { CampaignApiService } from '../campaigns/services/campaign-api.service';
import { PostListItem } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { AuditEntry, Dashboard, DashboardApiService } from '../dashboard/dashboard-api.service';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';

type Range = 'today' | '7' | '30';

interface Step {
  key: string;
  label: string;
  done: boolean;
  cta?: { label: string; link: string[] };
}

interface TodoRow {
  label: string;
  count: number;
  link: string;
  query?: Record<string, string>;
  tone: string;
}

interface Activity {
  id: string;
  text: string;
  at: string;
}

/**
 * Publishing ring segments. Hues validated as a categorical set (dataviz validate_palette, light): all checks pass;
 * the amber's low contrast is relieved by the always-visible labelled counts beside the ring.
 */
const SEGMENTS = [
  { key: 'published', label: 'Published', color: '#1f9254' },
  { key: 'scheduled', label: 'Scheduled', color: '#2563eb' },
  { key: 'review', label: 'In Review', color: '#d99a00' },
  { key: 'changes', label: 'Changes Requested', color: '#8b5cf6' },
  { key: 'failed', label: 'Failed', color: '#d92d20' },
] as const;

const HIDE_KEY = 'sma.getStartedHidden.';

/**
 * Home: the workspace landing page. Quick creation actions, a compact overview grid (workspace, publishing, to-do),
 * recent work, channels and activity. Getting Started is a small pinned panel that never replaces the page.
 * Every number comes from existing workspace data; nothing is estimated.
 */
@Component({
  selector: 'app-home-page',
  imports: [RouterLink, FormsModule, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  templateUrl: './home.page.html',
})
export class HomePage {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly campaignsApi = inject(CampaignApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly notificationsApi = inject(NotificationApiService);
  private readonly organizations = inject(OrganizationApiService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly deliveryLabels = DELIVERY_LABELS;
  protected readonly deliveryTone = deliveryTone;
  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly posts = signal<PostListItem[]>([]);
  protected readonly campaigns = signal<CampaignSummary[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly members = signal<Member[]>([]);
  protected readonly activity = signal<Activity[]>([]);
  protected readonly range = signal<Range>('7');
  protected readonly getStartedOpen = signal(true);
  protected readonly getStartedHidden = signal(false);
  protected readonly createPostOpen = signal(false);
  protected postCampaignId = '';
  protected postPlatform: SocialPlatform | '' = '';

  protected readonly firstName = computed(() => (this.auth.me()?.displayName ?? '').trim().split(/\s+/)[0] || 'there');
  private readonly myId = computed(() => this.auth.me()?.userId ?? null);

  // ---- Workspace summary ----
  protected readonly connectedCount = computed(() =>
    (this.dashboard()?.destinations ?? []).filter((a) => a.connectionStatus === 'CONNECTED' && a.executionMode !== 'NOT_CONFIGURED').length);
  protected readonly activeMembers = computed(() => this.members().filter((m) => m.status === 'Active').length);

  // ---- Content publishing (real counts; publishing events filtered by the selected window) ----
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
      // Review states are current workload, not events in the window.
      if (p.editorialState === 'IN_REVIEW') counts.review++;
      if (p.editorialState === 'CHANGES_REQUESTED') counts.changes++;
    }
    const total = Object.values(counts).reduce((a, b) => a + b, 0);
    // Ring arcs on a circle of circumference 100 (r = 15.915), starting at 12 o'clock, with a 1-unit surface gap
    // between segments when more than one is non-zero.
    const gap = SEGMENTS.filter((s) => counts[s.key] > 0).length > 1 ? 1 : 0;
    let start = 0;
    const segments = SEGMENTS.map((s) => {
      const value = counts[s.key];
      const length = total ? (value / total) * 100 : 0;
      const visible = Math.max(length - gap, 0);
      const arc = { ...s, value, dash: `${visible} ${100 - visible}`, offset: 25 - start };
      start += length;
      return arc;
    });
    return { segments, total };
  });

  // ---- To do (only actions this person can take) ----
  protected readonly todo = computed<TodoRow[]>(() => {
    const posts = this.posts();
    const rows: TodoRow[] = [];
    if (this.workspace.canReview()) {
      const pending = this.reviews().filter((r) => r.status === 'PENDING' && r.submittedByUserId !== this.myId()).length;
      rows.push({ label: 'Pending my review', count: pending, link: '/publisher/approvals', tone: 'waiting' });
    }
    if (this.workspace.canManageCampaigns()) {
      rows.push({ label: 'Changes requested', count: posts.filter((p) => p.editorialState === 'CHANGES_REQUESTED').length, link: '/publisher/drafts', tone: 'danger' });
      rows.push({
        label: 'Approved but not scheduled',
        count: posts.filter((p) => p.editorialState === 'APPROVED' && (!p.latestDeliveryState || p.latestDeliveryState === 'CANCELED')).length,
        link: '/publisher/posts', query: { status: 'Approved' }, tone: 'success',
      });
    }
    rows.push({ label: 'Publishing needs attention', count: posts.filter((p) => !!p.latestDeliveryState && PROBLEM_DELIVERY.includes(p.latestDeliveryState)).length, link: '/publisher/failed', tone: 'danger' });
    if (this.workspace.isAdmin()) {
      const reconnect = this.channels().filter((c) => c.status === 'Needs Reconnection').length;
      if (reconnect) rows.push({ label: 'Social account needs reconnection', count: reconnect, link: '/social-accounts', tone: 'danger' });
    }
    return rows;
  });
  protected readonly allClear = computed(() => this.todo().every((r) => r.count === 0));

  // ---- Recent work ----
  protected readonly recentCampaigns = computed(() =>
    this.campaigns().slice(0, 4).map((c) => {
      const counts = new Map<string, number>();
      for (const p of this.posts().filter((x) => x.campaignId === c.id)) {
        const status = customerStatus(p.editorialState, p.latestDeliveryState);
        counts.set(status, (counts.get(status) ?? 0) + 1);
      }
      const summary = [...counts.entries()].map(([status, n]) => (counts.size === 1 && n === 1 ? status : `${n} ${status}`)).join(' · ');
      return { ...c, summary: summary || 'No content yet' };
    }));
  protected readonly recentlyReviewed = computed(() =>
    this.reviews().filter((r) => r.decidedByUserId === this.myId() && r.decidedAtUtc)
      .sort((a, b) => b.decidedAtUtc!.localeCompare(a.decidedAtUtc!)).slice(0, 4));
  protected readonly upcoming = computed(() => (this.dashboard()?.upcoming ?? []).slice(0, 5));

  protected readonly channels = computed(() =>
    SOCIAL_PLATFORMS.map((platform) => {
      const accounts = (this.dashboard()?.destinations ?? []).filter((a) => a.platform === platform && a.executionMode !== 'NOT_CONFIGURED');
      const real = accounts.filter((a) => a.executionMode !== 'SIMULATED');
      const connected = accounts.filter((a) => a.connectionStatus === 'CONNECTED');
      const liveConnected = real.some((a) => a.connectionStatus === 'CONNECTED');
      const status = liveConnected || connected.length ? 'Connected' : real.length ? 'Needs Reconnection' : 'Not Connected';
      return { platform, status, demo: !liveConnected && connected.length > 0, name: (connected[0] ?? real[0])?.displayName ?? null };
    }));

  // ---- Get Started (small, dismissible) ----
  protected readonly steps = computed<Step[]>(() => {
    const o = this.auth.me()?.onboarding;
    const posts = this.posts();
    const firstCampaign = this.campaigns()[0];
    return [
      { key: 'workspace', label: 'Workspace created', done: !!o?.workspaceCreated },
      { key: 'profile', label: 'Profile completed', done: !!o?.profileCompleted },
      { key: 'brand', label: 'Brand configured', done: !!o?.brandProfileCompleted, cta: { label: 'Start', link: ['/brand'] } },
      { key: 'social', label: 'Social account connected', done: !!o?.socialAccountsConnected, cta: { label: 'Start', link: ['/social-accounts'] } },
      { key: 'campaign', label: 'Create your first campaign', done: this.campaigns().length > 0, cta: { label: 'Start', link: ['/campaigns/new'] } },
      { key: 'generate', label: 'Generate your first content', done: posts.length > 0, cta: { label: 'Start', link: firstCampaign ? ['/campaigns', firstCampaign.id, 'content'] : ['/campaigns/new'] } },
      { key: 'review', label: 'Submit content for review', done: posts.some((p) => p.editorialState !== 'DRAFT') || this.reviews().length > 0, cta: { label: 'Start', link: ['/publisher/drafts'] } },
      { key: 'schedule', label: 'Schedule your first post', done: posts.some((p) => !!p.latestJobId), cta: { label: 'Start', link: ['/publisher/posts'] } },
    ];
  });
  protected readonly doneCount = computed(() => this.steps().filter((s) => s.done).length);
  protected readonly nextStep = computed(() => this.steps().find((s) => !s.done) ?? null);
  protected readonly showGetStarted = computed(() => !this.loading() && !!this.nextStep() && !this.getStartedHidden() && this.workspace.canManageCampaigns());

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const admin = this.workspace.isAdmin();
      if (!orgId) return;
      this.getStartedHidden.set(readFlag(HIDE_KEY + orgId));
      void this.load(orgId, admin);
    });
  }

  protected hideGetStarted(): void {
    this.getStartedHidden.set(true);
    const orgId = this.workspace.orgId();
    if (orgId) writeFlag(HIDE_KEY + orgId);
  }

  protected platformsOf(campaignId: string): SocialPlatform[] {
    return this.campaigns().find((c) => c.id === campaignId)?.platforms ?? [];
  }

  /** Posts always belong to a campaign (one per platform); this opens the editor for a manual post in the chosen one. */
  protected createPost(): void {
    if (!this.postCampaignId || !this.postPlatform) return;
    void this.router.navigate(['/posts/new'], { queryParams: { campaignId: this.postCampaignId, platform: this.postPlatform } });
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
    const minutes = Math.round((Date.now() - Date.parse(/(Z|[+-]\d{2}:\d{2})$/i.test(utc) ? utc : `${utc}Z`)) / 60000);
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
    this.loading.set(true);
    this.problem.set(null);
    try {
      const [dashboard, posts, campaigns, reviews, members] = await Promise.all([
        firstValueFrom(this.dashboardApi.get(orgId)),
        firstValueFrom(this.postsApi.listAll(orgId)),
        firstValueFrom(this.campaignsApi.list(orgId)),
        firstValueFrom(this.reviewsApi.list(orgId)),
        firstValueFrom(this.organizations.members(orgId)).catch(() => []),
      ]);
      this.dashboard.set(dashboard);
      this.posts.set(posts);
      this.campaigns.set([...campaigns].sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)));
      this.reviews.set(reviews);
      this.members.set(members);
      // Admins see the workspace's business events; others see their own notifications.
      this.activity.set(admin
        ? (await firstValueFrom(this.dashboardApi.audit(orgId))).map((e) => this.fromAudit(e)).filter((a): a is Activity => a !== null).slice(0, 5)
        : (await firstValueFrom(this.notificationsApi.list(orgId))).slice(0, 5).map(fromNotification));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Business events in plain language; technical events are left out. */
  private fromAudit(e: AuditEntry): Activity | null {
    const actor = e.actorName ?? 'Someone';
    const campaign = e.resourceType === 'Campaign' ? this.campaigns().find((c) => c.id === e.resourceId)?.name : null;
    const post = e.resourceType === 'Post' ? this.posts().find((p) => p.id === e.resourceId) : null;
    const postName = post ? `${post.platform} post` : 'a post';
    const text = ((): string | null => {
      switch (e.action) {
        case 'WORKSPACE_CREATED': return `${actor} created the workspace`;
        case 'BRAND_PROFILE_SAVED': return `${actor} updated the brand`;
        case 'CAMPAIGN_CREATED': return campaign ? `Campaign “${campaign}” created by ${actor}` : `${actor} created a campaign`;
        case 'CAMPAIGN_UPDATED': return campaign ? `${actor} updated “${campaign}”` : `${actor} updated a campaign`;
        case 'POST_VERSION_CREATED':
          return e.metadata['source']?.startsWith('Ai') ? `${post ? post.platform + ' post' : 'Post'} generated` : `${actor} edited ${postName}`;
        case 'REVIEW_SUBMITTED': return `${actor} submitted ${postName} for review`;
        case 'CONTENT_APPROVED': return `${actor} approved ${postName}`;
        case 'CHANGES_REQUESTED': return `Changes requested on ${postName}`;
        case 'PUBLICATION_SCHEDULED': return `${post ? post.platform + ' post' : 'A post'} scheduled`;
        case 'PUBLICATION_PUBLISHED': return `${post ? post.platform + ' post' : 'A post'} published`;
        case 'PUBLICATION_FAILED': return `${post ? post.platform + ' post' : 'A post'} failed to publish`;
        case 'SOCIAL_CONNECTION_CONNECTED': return `${actor} connected a social account`;
        case 'MEMBER_ADDED': return `${actor} added a team member`;
        default: return null;
      }
    })();
    return text ? { id: e.id, text, at: e.createdAtUtc } : null;
  }
}

function fromNotification(n: AppNotification): Activity {
  return { id: n.id, text: `${n.title}: ${n.message}`, at: n.createdAtUtc };
}

// A dismissed panel is a per-browser convenience only; storage may be unavailable.
function readFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}

function writeFlag(key: string): void {
  try {
    localStorage.setItem(key, '1');
  } catch {
    // Not persisted; the panel stays hidden for this visit.
  }
}
