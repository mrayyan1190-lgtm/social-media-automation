import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ThemeOption, ThemeSelectComponent } from '../../shared/components/theme-select.component';
import { SOCIAL_PLATFORMS } from '../../shared/models/platform.model';
import { CUSTOMER_STATUSES, CustomerStatus, statusGroup } from '../../shared/models/states';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { PostListItem, PostVersion, postText } from '../content/models/post.model';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { art } from '../home/home.utils';
import { ScheduleDialogComponent } from '../publications/components/schedule-dialog.component';
import { LocalSchedule } from '../publications/models/publication.model';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';
import { PublicationApiService } from '../publications/services/publication-api.service';

/** A post with its one customer-facing status (built by the Planner). */
export interface PlannerRow extends PostListItem {
  status: CustomerStatus;
  /** Scheduled or published time; null when the post has no live publication intent. */
  publishAt: string | null;
}

type DateRange = '' | 'today' | 'next7' | 'last7' | 'month';

const STATUS_CLASS: Record<CustomerStatus, string> = {
  Draft: 'draft',
  'In Review': 'review',
  'Changes Requested': 'changes',
  Approved: 'approved',
  Scheduled: 'scheduled',
  Published: 'published',
  // A demo delivery is listed with Published but never styled or labelled as published.
  'Demo (not sent)': 'draft',
  Failed: 'failed',
};

/** The design's thumbnail colourways; posts without an image get one picked from their id. */
const ARTS = ['', 'rose', 'lavender', 'mint', 'peach'];

/**
 * Per page (the status shortcut it was opened with): the library subtitle and the date column's label. Pages whose posts
 * have no publishing date (drafts, reviews) have no date column.
 */
const PAGES: Record<string, { sub: string; date: string | null }> = {
  '': { sub: 'Drafts, reviews and published posts', date: 'Scheduled / published' },
  Draft: { sub: 'Posts you are still working on', date: null },
  'In Review': { sub: 'Posts waiting for a reviewer', date: null },
  'Changes Requested': { sub: 'Posts with reviewer feedback to address', date: null },
  Approved: { sub: 'Approved posts ready to schedule', date: null },
  Scheduled: { sub: 'Approved posts waiting to publish', date: 'Scheduled for' },
  'Approved,Scheduled': { sub: 'Approved posts and their schedules', date: 'Scheduled for' },
  Published: { sub: 'Posts sent to your social accounts (demo runs are marked)', date: 'Published' },
  Failed: { sub: 'Posts that could not be published', date: 'Was due' },
};

/** What a post's publishing time means, by its status. */
const DATE_LABELS: Partial<Record<CustomerStatus, string>> = { Scheduled: 'Scheduled for', Published: 'Published', 'Demo (not sent)': 'Demo run', Failed: 'Was due' };

const MENU_ITEM_HEIGHT = 37;

/** Words that make a meaningless thumbnail label. */
export const FILLER = new Set(['the', 'and', 'for', 'our', 'you', 'your', 'are', 'with', 'from', 'this', 'that', 'what', 'when', 'how', 'why', 'who', 'its', 'has', 'have', 'was', 'can', 'will', 'into', 'like', 'just', 'more']);

/**
 * All posts / Drafts / Scheduled / Published / Failed: the content library list with search, filters, row actions and
 * post details. New posts start in AI Studio. Actions only call the existing commands: Delete archives a draft, Duplicate creates a new draft
 * (Version 1, never approved), schedule changes go through the publication job.
 */
@Component({
  selector: 'app-posts-list',
  imports: [NgTemplateOutlet, RouterLink, ThemeSelectComponent, ScheduleDialogComponent],
  templateUrl: './posts-list.component.html',
  styleUrl: './posts-list.component.css',
  host: { '(document:keydown.escape)': 'escape()', '(document:click)': 'outsideMenu($event)', '(window:resize)': 'menu.set(null)' },
})
export class PostsListComponent {
  private readonly postsApi = inject(PostApiService);
  private readonly publications = inject(PublicationApiService);
  private readonly reviews = inject(ReviewApiService);
  private readonly media = inject(MediaApiService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly dialog = viewChild.required(ScheduleDialogComponent);

  readonly rows = input.required<PlannerRow[]>();
  readonly loading = input(false);
  /** Status from the Publisher shortcut (?status=), applied whenever it changes. */
  readonly initialStatus = input('');
  readonly initialSearch = input('');
  /** Something changed on the server; the Planner reloads its posts. */
  readonly changed = output<void>();

  protected readonly search = signal('');
  protected readonly platform = signal('');
  protected readonly status = signal('');
  protected readonly range = signal<DateRange>('');

  protected readonly platformOptions: ThemeOption[] = [{ value: '', label: 'All platforms' }, ...SOCIAL_PLATFORMS.map((p) => ({ value: p, label: p }))];
  protected readonly statusOptions: ThemeOption[] = [{ value: '', label: 'All statuses' }, ...CUSTOMER_STATUSES.map((s) => ({ value: s, label: s })),
    { value: 'Approved,Scheduled', label: 'Approved & scheduled' }];
  protected readonly dateOptions: ThemeOption[] = [{ value: '', label: 'Any date' }, { value: 'today', label: 'Today' }, { value: 'next7', label: 'Next 7 days' },
    { value: 'last7', label: 'Last 7 days' }, { value: 'month', label: 'This month' }];

  protected readonly menu = signal<{ row: PlannerRow; left: number; top: number } | null>(null);
  protected readonly details = signal<{ row: PlannerRow; text: string | null; feedback: string | null } | null>(null);
  protected readonly confirming = signal<{ kind: 'delete' | 'cancel'; row: PlannerRow } | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly toastText = signal<string | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;
  /** The scheduled job the open schedule dialog moves. */
  private rescheduling: string | null = null;

  protected readonly heading = computed(() => {
    const status = this.status();
    if (!status) return 'All posts';
    return status === 'Draft' ? 'Drafts' : status === 'Failed' ? 'Failed posts' : status === 'Approved,Scheduled' ? 'Approved & scheduled' : `${status} posts`;
  });

  protected readonly page = computed(() => PAGES[this.initialStatus()] ?? PAGES['']);

  protected readonly filtersActive = computed(() => !!(this.search().trim() || this.platform() || this.status() || this.range()));

  /** Filtered, most recently updated first. */
  protected readonly shown = computed(() => {
    const zone = this.workspace.timeZone();
    const today = dayKeyInZone(new Date().toISOString(), zone);
    const shift = (days: number) => dayKeyInZone(new Date(Date.now() + days * 864e5).toISOString(), zone);
    const q = this.search().trim().toLowerCase();
    return this.rows().filter((r) => {
      if (q && ![r.hook, r.bodyExcerpt, r.platform, r.status, r.createdByName].some((t) => t?.toLowerCase().includes(q))) return false;
      if (this.platform() && r.platform !== this.platform()) return false;
      // A filter may name several statuses ("Approved,Scheduled" from the Creator menu).
      if (this.status() && !this.status().split(',').includes(statusGroup(r.status))) return false;
      if (this.range()) {
        const day = dayKeyInZone(r.publishAt ?? r.updatedAtUtc, zone);
        if (this.range() === 'today' && day !== today) return false;
        if (this.range() === 'next7' && (day < today || day > shift(7))) return false;
        if (this.range() === 'last7' && (day > today || day < shift(-7))) return false;
        if (this.range() === 'month' && day.slice(0, 7) !== today.slice(0, 7)) return false;
      }
      return true;
    }).sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc));
  });

  constructor() {
    effect(() => {
      const status = this.initialStatus();
      untracked(() => this.status.set(status));
    });
    effect(() => {
      const q = this.initialSearch();
      untracked(() => this.search.set(q));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.toastTimer));
  }

  // ---- Row display ----------------------------------------------------------------------------------

  protected title(r: PostListItem): string {
    return r.hook || r.bodyExcerpt;
  }

  protected subtitle(r: PostListItem): string {
    return r.hook ? r.bodyExcerpt : 'Social media post';
  }

  protected dateLabel(status: CustomerStatus): string {
    return DATE_LABELS[status] ?? 'Scheduled / published';
  }

  protected statusClass(status: CustomerStatus): string {
    return 'status status-' + STATUS_CLASS[status];
  }

  protected artOf(r: PostListItem): string {
    return ARTS[art(r.id) % ARTS.length];
  }

  /** The thumbnail's word, as in the design (NEW, IDEA, NEXT…): the post's first 3–6 letter word that is not filler. */
  protected mark(r: PostListItem): string {
    const words = this.title(r).split(/\s+/).map((w) => w.replace(/[^\p{L}\p{N}]/gu, '')).filter(Boolean);
    const word = words.find((w) => w.length >= 3 && w.length <= 6 && !FILLER.has(w.toLowerCase())) ?? words[0]?.slice(0, 4) ?? 'POST';
    return word.toUpperCase();
  }

  protected thumbUrl(r: PostListItem): string | null {
    return r.mediaAssetId ? this.media.previewUrl(this.workspace.orgId()!, r.mediaAssetId) : null;
  }

  protected when(utc: string | null): string {
    if (!utc) return '—';
    const zone = this.workspace.timeZone();
    return `${formatInZone(utc, zone, { day: '2-digit', month: 'short', year: 'numeric' })}, ${formatInZone(utc, zone, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })}`;
  }

  protected day(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone(), { day: '2-digit', month: 'short', year: 'numeric' });
  }

  protected canDelete(r: PlannerRow): boolean {
    return (r.editorialState === 'DRAFT' || r.editorialState === 'CHANGES_REQUESTED') && this.workspace.canManageContent();
  }

  protected canChangeSchedule(r: PlannerRow): boolean {
    return r.status === 'Scheduled' && !!r.latestJobId && this.workspace.canManageContent();
  }

  protected clear(): void {
    this.search.set('');
    this.platform.set('');
    this.status.set('');
    this.range.set('');
  }

  // ---- Menu, modals, toast --------------------------------------------------------------------------

  protected openMenu(event: MouseEvent, row: PlannerRow): void {
    if (this.menu()?.row === row) return this.menu.set(null);
    const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const items = 2 + (this.workspace.canManageContent() ? 1 : 0) + (this.canChangeSchedule(row) ? 2 : 0)
      + (row.status === 'In Review' && this.workspace.isAdmin() ? 1 : 0) + (this.canDelete(row) ? 1 : 0);
    const height = items * MENU_ITEM_HEIGHT + 20;
    const width = 168;
    const left = Math.max(10, Math.min(innerWidth - width - 10, box.right - width));
    const top = box.bottom + height + 8 > innerHeight ? box.top - height - 7 : box.bottom + 7;
    this.menu.set({ row, left, top: Math.max(8, top) });
  }

  protected outsideMenu(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (this.menu() && !target.closest('[role=menu]') && !target.closest('[data-more]')) this.menu.set(null);
  }

  protected escape(): void {
    this.menu.set(null);
    this.details.set(null);
    this.confirming.set(null);
  }

  private toast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toastText.set(message);
    this.toastTimer = setTimeout(() => this.toastText.set(null), 2200);
  }

  /** Details show the full current text and, for changes requested, the reviewer's feedback. */
  protected async openDetails(row: PlannerRow): Promise<void> {
    this.menu.set(null);
    this.details.set({ row, text: null, feedback: null });
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    try {
      const [post, reviews] = await Promise.all([
        firstValueFrom(this.postsApi.get(orgId, row.id)),
        row.status === 'Changes Requested' ? firstValueFrom(this.reviews.list(orgId, { postId: row.id })) : Promise.resolve([]),
      ]);
      const v = post.currentVersion;
      const feedback = reviews.find((r) => r.status === 'CHANGES_REQUESTED')?.decisionComment ?? null;
      if (this.details()?.row === row) this.details.set({ row, text: postText(v.hook ? { ...v, hook: null } : v) || null, feedback });
    } catch (error) {
      this.details.set(null);
      this.problem.set(toApiProblem(error));
    }
  }

  // ---- Commands -------------------------------------------------------------------------------------

  /** Copies the current version into a brand-new draft; nothing of its review or schedule carries over. */
  protected duplicate(row: PlannerRow): Promise<void> {
    this.menu.set(null);
    this.details.set(null);
    return this.run(async (orgId) => {
      const v: PostVersion = (await firstValueFrom(this.postsApi.get(orgId, row.id))).currentVersion;
      await firstValueFrom(this.postsApi.create(orgId, row.platform, {
        hook: v.hook, body: v.body, callToAction: v.callToAction, destinationUrl: v.destinationUrl, hashtags: v.hashtags,
        images: v.images.map((i) => ({ mediaAssetId: i.mediaAssetId, altText: i.altText })), socialAccountId: v.socialAccountId,
      }));
    }, 'Draft duplicated');
  }

  protected confirm(kind: 'delete' | 'cancel', row: PlannerRow): void {
    this.menu.set(null);
    this.confirming.set({ kind, row });
  }

  protected confirmed(): Promise<void> {
    const c = this.confirming();
    if (!c) return Promise.resolve();
    if (c.kind === 'delete') {
      return this.run(async (orgId) => {
        await firstValueFrom(this.postsApi.archive(orgId, await firstValueFrom(this.postsApi.get(orgId, c.row.id))));
        this.confirming.set(null);
      }, 'Draft deleted');
    }
    return this.run(async (orgId) => {
      const job = await firstValueFrom(this.publications.get(orgId, c.row.latestJobId!));
      await firstValueFrom(this.publications.cancel(orgId, job));
      this.confirming.set(null);
    }, 'Schedule canceled. Nothing was sent.');
  }

  /** Moves the post's scheduled job; the server re-checks approval and that dispatch has not started. */
  protected async reschedule(row: PlannerRow): Promise<void> {
    this.menu.set(null);
    const orgId = this.workspace.orgId();
    if (!orgId || !row.latestJobId) return;
    try {
      const job = await firstValueFrom(this.publications.get(orgId, row.latestJobId));
      this.rescheduling = job.id;
      this.dialog().open('Change schedule', 'Save', this.workspace.timeZone(), job);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected readonly submitReschedule = async (when: LocalSchedule): Promise<ApiProblem | null> => {
    const orgId = this.workspace.orgId();
    if (!orgId || !this.rescheduling) return null;
    try {
      // Fetched again so the If-Match ETag is current at the moment of saving.
      const job = await firstValueFrom(this.publications.get(orgId, this.rescheduling));
      const saved = await firstValueFrom(this.publications.reschedule(orgId, job, when));
      this.toast(`Rescheduled for ${this.when(saved.publishAtUtc)}`);
      this.changed.emit();
      return null;
    } catch (error) {
      return toApiProblem(error);
    }
  };

  private async run(action: (orgId: string) => Promise<void>, message: string): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || this.busy()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await action(orgId);
      this.toast(message);
      this.changed.emit();
    } catch (error) {
      this.problem.set(toApiProblem(error));
      this.confirming.set(null);
    } finally {
      this.busy.set(false);
    }
  }
}
