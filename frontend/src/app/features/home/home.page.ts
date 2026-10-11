import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { AppNotification, NotificationApiService } from '../../core/services/notification-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { customerStatus } from '../../shared/models/states';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../shared/models/platform.model';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { PostListItem } from '../content/models/post.model';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { formatInZone } from '../publications/models/time-zones';
import { BRAND_TONES } from '../settings/models/brand-profile.model';
import { utcMs } from './home.utils';

type FlowStatus = 'draft' | 'revision' | 'review' | 'approved' | 'published';

/** The content-flow donut and legend, in the design's order and colours. */
const FLOW: { key: FlowStatus; label: string; color: string; planner: string }[] = [
  { key: 'draft', label: 'My drafts', color: '#7777D6', planner: 'Draft' },
  { key: 'revision', label: 'Changes', color: '#E9A09F', planner: 'Changes Requested' },
  { key: 'review', label: 'In review', color: '#E1B66F', planner: 'In Review' },
  { key: 'approved', label: 'Approved', color: '#9B85DC', planner: 'Scheduled' },
  { key: 'published', label: 'Published', color: '#5BB88E', planner: 'Published' },
];
/** Donut circumference (r = 72). */
const RING = 2 * Math.PI * 72;

/**
 * Creator home: the AI studio entry, the creator's own content flow, drafts to continue, changes requested by a reviewer,
 * their posts on the way out and their notifications. Admins get AdminHomePage instead (see app.routes).
 */
@Component({
  selector: 'app-home-page',
  imports: [ProblemBannerComponent, RouterLink, FormsModule],
  templateUrl: './home.page.html',
  styleUrl: './home.page.css',
  host: { '(document:keydown.escape)': 'createOpen.set(false)' },
})
export class HomePage {
  private readonly postsApi = inject(PostApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly notificationsApi = inject(NotificationApiService);
  private readonly mediaApi = inject(MediaApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly posts = signal<PostListItem[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly notifications = signal<AppNotification[]>([]);

  protected readonly createOpen = signal(false);
  protected readonly flowLegend = FLOW;
  protected readonly tones = BRAND_TONES;
  protected readonly platforms = SOCIAL_PLATFORMS;
  protected idea = '';
  protected platform: SocialPlatform = 'LinkedIn';
  protected tone = BRAND_TONES[0] as string;
  private readonly ideaInput = viewChild<ElementRef<HTMLTextAreaElement>>('ideaInput');

  private readonly zone = computed(() => this.workspace.timeZone());
  /** Creator home works on the signed-in person's own posts only. */
  private readonly mine = computed(() => {
    const me = this.auth.me()?.userId;
    return this.posts().filter((p) => p.createdByUserId === me);
  });

  private flowStatus(p: PostListItem): FlowStatus | null {
    if (p.latestDeliveryState === 'PUBLISHED') return 'published';
    const status = customerStatus(p.editorialState, p.latestDeliveryState);
    if (status === 'Approved' || status === 'Scheduled') return 'approved';
    return p.editorialState === 'DRAFT' ? 'draft' : p.editorialState === 'CHANGES_REQUESTED' ? 'revision'
      : p.editorialState === 'IN_REVIEW' ? 'review' : null;
  }

  // ---- Your content flow ---------------------------------------------------------------------------

  protected readonly flow = computed(() => {
    const counts: Record<FlowStatus, number> = { draft: 0, revision: 0, review: 0, approved: 0, published: 0 };
    for (const p of this.mine()) {
      const s = this.flowStatus(p);
      if (s) counts[s]++;
    }
    const total = Object.values(counts).reduce((a, b) => a + b, 0);
    // One arc per status, with a small gap between arcs (as in the design).
    let offset = 0;
    const arcs = FLOW.filter((f) => counts[f.key]).map((f) => {
      const seg = (RING * counts[f.key]) / total;
      const gap = Math.min(7, seg * 0.1);
      const arc = { key: f.key, color: f.color, dash: `${Math.max(seg - gap, 1)} ${RING}`, offset: -offset };
      offset += seg;
      return arc;
    });
    return { counts, total, arcs };
  });

  // ---- Continue creating / changes requested / upcoming ---------------------------------------------

  protected readonly drafts = computed(() => this.mine().filter((p) => p.editorialState === 'DRAFT')
    .sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)));

  /** Posts a reviewer sent back, newest first, with the reviewer's comment. */
  protected readonly changes = computed(() => {
    const comments = new Map(this.reviews().filter((r) => r.status === 'CHANGES_REQUESTED')
      // Oldest first, so the latest decision per post wins in the map.
      .sort((a, b) => (a.decidedAtUtc ?? '').localeCompare(b.decidedAtUtc ?? ''))
      .map((r) => [r.postId, { note: r.decisionComment ?? '', by: r.decidedByName ?? 'Reviewer' }]));
    return this.mine().filter((p) => p.editorialState === 'CHANGES_REQUESTED')
      .sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc))
      .map((p) => ({ post: p, note: comments.get(p.id)?.note ?? '', by: comments.get(p.id)?.by ?? 'Reviewer' }));
  });

  /** Waiting for review, approved and scheduled: scheduled soonest first, then the most recently updated. */
  protected readonly upcoming = computed(() => {
    const order: Record<string, number> = { Scheduled: 0, Approved: 1, 'In Review': 2 };
    return this.mine()
      .map((p) => ({ p, s: customerStatus(p.editorialState, p.latestDeliveryState) }))
      .filter(({ s }) => s in order)
      .sort((a, b) => order[a.s] - order[b.s]
        || (a.s === 'Scheduled' ? a.p.latestPublishAtUtc!.localeCompare(b.p.latestPublishAtUtc!) : b.p.updatedAtUtc.localeCompare(a.p.updatedAtUtc)))
      .map(({ p, s }) => ({
        post: p,
        label: s === 'In Review' ? 'Waiting for review' : s,
        when: s === 'Scheduled' && p.latestPublishAtUtc
          ? `${this.fmt(p.latestPublishAtUtc, { month: 'short', day: 'numeric' })}, ${this.fmt(p.latestPublishAtUtc, { timeStyle: 'short' })}`
          : this.ago(p.updatedAtUtc),
      }));
  });

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
  }

  protected ws(path: string): string {
    return `/${this.workspace.slug()}${path}`;
  }

  protected title(p: PostListItem): string {
    return p.hook || p.bodyExcerpt;
  }

  protected preview(mediaId: string): string {
    return this.mediaApi.previewUrl(this.workspace.orgId()!, mediaId);
  }

  protected openCreate(): void {
    this.createOpen.set(true);
    setTimeout(() => this.ideaInput()?.nativeElement.focus());
  }

  /** The idea, platform and tone go to AI Studio, which generates the post and saves it as a draft. */
  protected generate(formEl: HTMLFormElement): void {
    if (!formEl.reportValidity()) return;
    void this.router.navigate([this.ws('/ai-studio')], { queryParams: { prompt: this.idea.trim(), platform: this.platform, tone: this.tone } });
  }

  protected scrollTo(target: HTMLElement): void {
    target.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'center' });
  }

  protected ago(utc: string): string {
    const minutes = Math.round((Date.now() - utcMs(utc)) / 60000);
    if (minutes < 1) return 'Now';
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
      const [posts, reviews, notifications] = await Promise.all([
        firstValueFrom(this.postsApi.listAll(orgId)),
        firstValueFrom(this.reviewsApi.list(orgId)),
        firstValueFrom(this.notificationsApi.list(orgId)),
      ]);
      this.posts.set(posts);
      this.reviews.set(reviews);
      this.notifications.set(notifications);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }
}
