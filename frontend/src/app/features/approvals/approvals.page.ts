import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DateTimePickerComponent } from '../../shared/components/date-time-picker.component';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { DELIVERY_LABELS, PROBLEM_DELIVERY } from '../../shared/models/states';
import { PostListItem, postText } from '../content/models/post.model';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { art } from '../home/home.utils';
import { formatInZone } from '../publications/models/time-zones';
import { PublicationApiService } from '../publications/services/publication-api.service';
import { ReviewDetail, ReviewSummary } from './models/review.model';
import { ReviewApiService } from './services/review-api.service';

type Tab = 'pending' | 'mine' | 'changes' | 'approved';
type Tone = 'review' | 'changes' | 'approved' | 'scheduled' | 'published';
type Modal = 'review' | 'open' | 'schedule' | 'publish';

const TABS: { id: Tab; label: string; sub: string }[] = [
  { id: 'pending', label: 'Pending my review', sub: 'Review submitted posts and make a decision' },
  { id: 'mine', label: 'Submitted by me', sub: 'Posts you have submitted and their current status' },
  { id: 'changes', label: 'Changes requested', sub: 'Feedback from your reviewer, ready to address' },
  { id: 'approved', label: 'Approved', sub: 'Approved content ready for scheduling' },
];

const REVIEW_TONE: Record<string, [Tone, string]> = {
  PENDING: ['review', 'In review'],
  CHANGES_REQUESTED: ['changes', 'Changes requested'],
  APPROVED: ['approved', 'Approved'],
};

/** The design's four poster colourways; posts without an image get one picked from their id. */
const ARTS = ['sky', 'violet', 'mint', 'rose'];

/** One list row: the reviews of one post group (one per platform), with their posts. */
interface Row {
  key: string;
  review: ReviewSummary;
  reviews: ReviewSummary[];
  posts: PostListItem[];
  tone: Tone;
  label: string;
  /** Approved tab: what happened after approval, when Schedule / Publish now no longer apply. */
  note: string | null;
}

/** Approved-tab status from the latest publication intent. Simulation is never shown as published. */
function delivery(state: string | null): [Tone, string, string | null] {
  if (!state || state === 'CANCELED' || state === 'FAILED') return ['approved', 'Approved', null];
  if (state === 'PUBLISHED') return ['published', 'Published', 'Publishing confirmed'];
  if (state === 'SIMULATED') return ['scheduled', DELIVERY_LABELS[state], 'Demo only, nothing was sent'];
  if (PROBLEM_DELIVERY.includes(state)) return ['changes', DELIVERY_LABELS[state], 'See Planner'];
  return ['scheduled', DELIVERY_LABELS[state] ?? 'Scheduled', state === 'SCHEDULED' ? 'Schedule saved' : 'Publishing started'];
}

/**
 * Human review. Each review covers one exact version: a reviewer approves or requests changes on what was submitted;
 * edits happen in Composer (a new version, reviewed again). Approved current versions are scheduled or published from
 * the Approved tab through publication jobs. Buttons are hidden when the API would refuse, but the API decides.
 */
@Component({
  selector: 'app-approvals-page',
  imports: [DateTimePickerComponent],
  templateUrl: './approvals.page.html',
  styleUrl: './approvals.page.css',
  host: { '(document:keydown.escape)': 'closeModal()' },
})
export class ApprovalsPage {
  private readonly api = inject(ReviewApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly publications = inject(PublicationApiService);
  private readonly media = inject(MediaApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly titles: Record<Modal, string> = { review: 'Review this post', open: 'Post details', schedule: 'Schedule approved post', publish: 'Confirm publishing' };
  /** ?tab=approved opens the Approved tab (used right after approving). */
  readonly tabQuery = input<string>(undefined, { alias: 'tab' });
  protected readonly tabs = computed(() => TABS.filter((t) => t.id !== 'pending' || this.workspace.canReview()));
  protected readonly tab = signal<Tab>(this.workspace.canReview() ? 'pending' : 'mine');
  protected readonly current = computed(() => TABS.find((t) => t.id === this.tab())!);
  protected readonly search = signal('');

  private readonly reviews = signal<ReviewSummary[]>([]);
  private readonly posts = signal(new Map<string, PostListItem>());
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);

  protected readonly modal = signal<{ type: Modal; row: Row } | null>(null);
  protected readonly detail = signal<ReviewDetail | null>(null);
  protected readonly showFeedback = signal(false);
  protected readonly feedback = signal('');
  protected readonly scheduleAt = signal('');
  protected readonly busy = signal(false);
  protected readonly modalProblem = signal<ApiProblem | null>(null);
  protected readonly toastText = signal<string | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;

  private readonly me = computed(() => this.auth.me()?.userId);
  /** The newest review of each post: older change requests are already answered by a newer version. */
  private readonly latest = computed(() => {
    const seen = new Set<string>();
    return this.reviews().filter((r) => {
      if (seen.has(r.postId)) return false;
      seen.add(r.postId);
      return true;
    });
  });
  private readonly all = computed(() => Object.fromEntries(TABS.map((t) => [t.id, this.select(t.id)])) as Record<Tab, Row[]>);
  protected readonly rows = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.all()[this.tab()].filter((r) => r.review.excerpt.toLowerCase().includes(term));
  });

  constructor() {
    effect(() => {
      if (this.tabQuery() === 'approved') this.tab.set('approved');
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.toastTimer);
      document.body.style.overflow = '';
    });
  }

  protected count(tab: Tab): number {
    return this.all()[tab].length;
  }

  protected activate(tab: Tab): void {
    this.tab.set(tab);
    this.search.set('');
  }

  /** Arrow keys move between the tabs, as in the design. */
  protected arrow(event: KeyboardEvent): void {
    const ids = this.tabs().map((t) => t.id);
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? ids.length - 1 : 0;
    if (!step) return;
    event.preventDefault();
    const next = ids[(ids.indexOf(this.tab()) + step) % ids.length];
    this.activate(next);
    document.getElementById(`tab-${next}`)?.focus();
  }

  protected when(r: Row): string {
    const utc = this.tab() === 'approved' ? r.review.decidedAtUtc ?? r.review.submittedAtUtc : r.review.submittedAtUtc;
    const zone = this.workspace.timeZone();
    return `${formatInZone(utc, zone, { day: 'numeric', month: 'short', year: 'numeric' })} · ${formatInZone(utc, zone, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })}`;
  }

  protected platforms(r: Row): string {
    return r.reviews.map((x) => x.platform).join(', ');
  }

  protected thumb(r: Row): string | null {
    const id = r.posts.find((p) => p.mediaAssetId)?.mediaAssetId;
    return id ? this.imageUrl(id) : null;
  }

  protected artOf(r: Row): string {
    return ARTS[art(r.review.postId) % ARTS.length];
  }

  /** The poster's two-line title: the first two short words of the post (long words would break mid-word). */
  protected miniTitle(r: Row): string {
    const words = r.review.excerpt.split(/\s+/).filter(Boolean);
    const short = words.filter((w) => w.length <= 8);
    return (short.length ? short : words).slice(0, 2).join('\n').toUpperCase();
  }

  protected imageUrl(mediaAssetId: string): string {
    return this.media.previewUrl(this.workspace.orgId()!, mediaAssetId);
  }

  protected text(d: ReviewDetail): string {
    return postText(d.version);
  }

  // ---- Modal ----------------------------------------------------------------------------------------

  protected async open(type: Modal, row: Row): Promise<void> {
    this.modal.set({ type, row });
    this.detail.set(null);
    this.showFeedback.set(false);
    this.feedback.set('');
    this.scheduleAt.set('');
    this.modalProblem.set(null);
    document.body.style.overflow = 'hidden';
    const orgId = this.workspace.orgId();
    if (!orgId || (type !== 'review' && type !== 'open')) return;
    try {
      const detail = await firstValueFrom(this.api.get(orgId, row.review.id));
      if (this.modal()?.row === row) this.detail.set(detail);
    } catch (error) {
      this.modalProblem.set(toApiProblem(error));
    }
  }

  protected closeModal(): void {
    if (!this.modal()) return;
    this.modal.set(null);
    document.body.style.overflow = '';
  }

  protected edit(row: Row): void {
    void this.router.navigate(['/composer', row.review.postId]);
  }

  /** One decision for every platform of the post group; each platform's exact version is still decided on its own. */
  protected approve(): Promise<void> {
    return this.act((orgId, row) => this.each(row.reviews, (r) => firstValueFrom(this.api.approve(orgId, r))),
      'approved', 'Approved. It can now be scheduled or published.');
  }

  protected requestChanges(): Promise<void> {
    const explanation = this.feedback().trim();
    if (!explanation) {
      document.getElementById('feedback')?.focus();
      return Promise.resolve(this.toast('Please add reviewer feedback.'));
    }
    return this.act((orgId, row) => this.each(row.reviews, (r) => firstValueFrom(this.api.requestChanges(orgId, r, explanation))),
      'changes', 'Changes requested. The creator has been notified.');
  }

  /** Schedule and Publish now only create publication jobs for the exact approved versions; the worker publishes. */
  protected schedule(): Promise<void> {
    const [localDate, time] = this.scheduleAt().split('T');
    if (!localDate || !time) {
      document.querySelector<HTMLElement>('#scheduleAt button')?.focus();
      return Promise.resolve(this.toast('Choose a schedule first.'));
    }
    const when = { localDate, localTime: `${time.slice(0, 5)}:00`, timeZone: this.workspace.timeZone(), utcOffsetMinutes: null };
    return this.act((orgId, row) => this.each(row.reviews, async (r) => {
      const post = await firstValueFrom(this.postsApi.get(orgId, r.postId));
      await firstValueFrom(this.publications.schedule(orgId, r.postId, r.postVersionId, when, post.rowVersion));
    }), 'approved', `Scheduled for ${localDate} ${time.slice(0, 5)} (${this.workspace.timeZone()}).`);
  }

  protected publishNow(): Promise<void> {
    return this.act((orgId, row) => this.each(row.reviews, async (r) => {
      const post = await firstValueFrom(this.postsApi.get(orgId, r.postId));
      await firstValueFrom(this.publications.publishNow(orgId, r.postId, r.postVersionId, post.rowVersion));
    }), 'approved', 'Publishing now. Planner shows the outcome.');
  }

  private async act(action: (orgId: string, row: Row) => Promise<void>, then: Tab, message: string): Promise<void> {
    const orgId = this.workspace.orgId();
    const row = this.modal()?.row;
    if (!orgId || !row || this.busy()) return;
    this.busy.set(true);
    this.modalProblem.set(null);
    try {
      await action(orgId, row);
      this.closeModal();
      this.activate(then);
      this.toast(message);
      await this.load(orgId);
    } catch (error) {
      this.modalProblem.set(toApiProblem(error));
      await this.load(orgId);
    } finally {
      this.busy.set(false);
    }
  }

  /** Runs the call for each platform's review, naming the platform when a later one fails. */
  private async each(reviews: ReviewSummary[], call: (r: ReviewSummary) => Promise<unknown>): Promise<void> {
    for (const r of reviews) {
      try {
        await call(r);
      } catch (error) {
        throw r === reviews[0] ? error : new Error(`${r.platform}: ${toApiProblem(error).detail}`);
      }
    }
  }

  private toast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toastText.set(message);
    this.toastTimer = setTimeout(() => this.toastText.set(null), 2700);
  }

  // ---- Data -----------------------------------------------------------------------------------------

  private select(tab: Tab): Row[] {
    const me = this.me();
    const posts = this.posts();
    switch (tab) {
      // Single-login MVP: every pending review, including my own submissions. Oldest first.
      case 'pending':
        return this.rowsOf(this.reviews().filter((r) => r.status === 'PENDING').reverse());
      case 'mine':
        return this.rowsOf(this.reviews().filter((r) => r.submittedByUserId === me));
      case 'changes':
        return this.rowsOf(this.latest().filter((r) => r.status === 'CHANGES_REQUESTED'));
      default:
        // Approved and still the post's current, approved version (an edit after approval needs review again).
        return this.rowsOf(this.latest().filter((r) => {
          const post = posts.get(r.postId);
          return r.status === 'APPROVED' && post?.editorialState === 'APPROVED' && post.currentVersionNumber === r.versionNumber;
        }), (r) => delivery(posts.get(r.postId)?.latestDeliveryState ?? null));
    }
  }

  /** One row per post group and status: a post made for several platforms is listed once. */
  private rowsOf(reviews: ReviewSummary[], status = (r: ReviewSummary): [Tone, string, string | null] => [...REVIEW_TONE[r.status], null]): Row[] {
    const rows = new Map<string, Row>();
    for (const r of reviews) {
      const [tone, label, note] = status(r);
      const key = `${r.groupId}|${r.status}|${label}`;
      const row = rows.get(key);
      if (row?.reviews.some((x) => x.postId === r.postId)) continue;
      const post = this.posts().get(r.postId);
      if (row) {
        row.reviews.push(r);
        if (post) row.posts.push(post);
      } else {
        rows.set(key, { key, review: r, reviews: [r], posts: post ? [post] : [], tone, label, note });
      }
    }
    return [...rows.values()];
  }

  private async load(orgId: string): Promise<void> {
    this.problem.set(null);
    try {
      const [reviews, posts] = await Promise.all([firstValueFrom(this.api.list(orgId)), firstValueFrom(this.postsApi.listAll(orgId))]);
      this.posts.set(new Map(posts.map((p) => [p.id, p])));
      this.reviews.set(reviews);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
