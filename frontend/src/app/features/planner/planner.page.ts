import { Component, DestroyRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { FILLER, PlannerRow, PostsListComponent } from './posts-list.component';
import { WorkspaceService } from '../../core/services/workspace.service';
import { CUSTOMER_STATUSES, CustomerStatus, customerStatus, statusGroup } from '../../shared/models/states';
import { SOCIAL_PLATFORMS } from '../../shared/models/platform.model';
import { PostListItem, postText } from '../content/models/post.model';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { ScheduleDialogComponent } from '../publications/components/schedule-dialog.component';
import { LocalSchedule } from '../publications/models/publication.model';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';
import { PublicationApiService } from '../publications/services/publication-api.service';
import { art } from '../home/home.utils';
import { ThemeSelectComponent } from '../../shared/components/theme-select.component';

type Row = PlannerRow;

/** Design colour per customer status (dot, pill edge, chip). A demo delivery is never styled as published. */
const STATUS_CLASS: Record<CustomerStatus, string> = {
  Draft: 'draft',
  'In Review': 'review',
  'Changes Requested': 'failed',
  Approved: 'published',
  Scheduled: 'scheduled',
  Published: 'published',
  'Demo (not sent)': 'draft',
  Failed: 'failed',
};

/** The design's poster colourways; posts without an image get one picked from their id. */
const ARTS = ['sky', 'violet', 'peach', ''];
const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const DAY = 864e5;

/** yyyy-MM-dd ↔ a UTC date, so calendar arithmetic never depends on the browser's zone. */
const toDate = (key: string) => new Date(`${key}T00:00:00Z`);
const toKey = (date: Date) => date.toISOString().slice(0, 10);
const fmt = (key: string, options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat('en-US', { ...options, timeZone: 'UTC' }).format(toDate(key));

/**
 * Planner: every post with one customer-facing status on a month calendar (a week strip on phones) or as this month's
 * list, with a details drawer per post or day. Posts sit on their publishing day, or (drafts, reviews) on their last
 * update. ?status= / ?view=list open the content library list instead (PostsListComponent).
 */
@Component({
  selector: 'app-planner-page',
  imports: [FormsModule, NgTemplateOutlet, RouterLink, PostsListComponent, ScheduleDialogComponent, ThemeSelectComponent],
  templateUrl: './planner.page.html',
  styleUrl: './planner.page.css',
  host: { '(document:keydown.escape)': 'escape()', '(window:resize)': 'measure()' },
})
export class PlannerPage {
  private readonly postsApi = inject(PostApiService);
  private readonly publications = inject(PublicationApiService);
  private readonly media = inject(MediaApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialog = viewChild(ScheduleDialogComponent);
  protected readonly workspace = inject(WorkspaceService);

  /** Query parameters from the sidebar shortcuts: ?status=Draft (a list filter) or ?view=list. */
  readonly statusQuery = input<string>(undefined, { alias: 'status' });
  readonly viewQuery = input<string>(undefined, { alias: 'view' });
  /** ?q= (older search links): opens the list with this search. */
  readonly searchQuery = input<string>(undefined, { alias: 'q' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly statuses = CUSTOMER_STATUSES;
  protected readonly weekdays = WEEKDAYS;
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly rows = signal<Row[]>([]);
  /** 'list' is the content library (Publisher shortcuts); the Planner itself switches calendar / month list. */
  protected readonly layout = signal<'calendar' | 'list'>('calendar');

  protected readonly platform = signal('');
  protected readonly status = signal('');
  protected readonly today = computed(() => dayKeyInZone(new Date().toISOString(), this.workspace.timeZone()));
  protected readonly month = signal(this.today().slice(0, 7));
  protected readonly selectedDay = signal(this.today());
  /** Two posts per day on tall wide screens, one otherwise (as the design). */
  protected readonly pillCount = signal(1);

  protected readonly drawer = signal<{ kind: 'post'; row: Row; text: string | null } | { kind: 'day'; day: string } | null>(null);
  protected readonly busy = signal(false);
  protected readonly toastText = signal<string | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;
  /** The scheduled job the open schedule dialog moves. */
  private rescheduling: string | null = null;

  protected readonly filtered = computed(() => this.rows().filter((r) =>
    (!this.platform() || r.platform === this.platform())
    // A filter may name several statuses ("Approved,Scheduled" from the Creator menu).
    && (!this.status() || this.status().split(',').includes(statusGroup(r.status)))));

  /** Filtered posts by day in the workspace time zone: on their publishing time, or (drafts, reviews) on their last update. */
  private readonly byDay = computed(() => {
    const zone = this.workspace.timeZone();
    const byDay = new Map<string, Row[]>();
    for (const r of this.filtered()) {
      const key = dayKeyInZone(r.publishAt ?? r.updatedAtUtc, zone);
      byDay.set(key, [...(byDay.get(key) ?? []), r]);
    }
    for (const posts of byDay.values()) posts.sort((a, b) => (a.publishAt ?? a.updatedAtUtc).localeCompare(b.publishAt ?? b.updatedAtUtc));
    return byDay;
  });

  /** Whole weeks (Monday first) covering the month: five or six rows, never a wasted extra week. */
  protected readonly cells = computed(() => {
    const first = toDate(`${this.month()}-01`);
    const start = (first.getUTCDay() + 6) % 7;
    const last = new Date(Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + 1, 0)).getUTCDate();
    const byDay = this.byDay();
    return Array.from({ length: Math.ceil((start + last) / 7) * 7 }, (_, i) => {
      const key = toKey(new Date(first.getTime() + (i - start) * DAY));
      return { key, day: Number(key.slice(8)), inMonth: key.startsWith(this.month()), weekend: i % 7 >= 5, posts: byDay.get(key) ?? [] };
    });
  });

  protected readonly monthTitle = computed(() => fmt(`${this.month()}-01`, { month: 'long', year: 'numeric' }));
  /** This month's posts in date order (the List tab and the count). */
  protected readonly monthPosts = computed(() => [...this.byDay().entries()]
    .filter(([day]) => day.startsWith(this.month()))
    .sort(([a], [b]) => a.localeCompare(b))
    .flatMap(([, posts]) => posts));

  /** Phones: the week around the selected day and that day's posts. */
  protected readonly week = computed(() => {
    const selected = toDate(this.selectedDay());
    const monday = selected.getTime() - ((selected.getUTCDay() + 6) % 7) * DAY;
    return WEEKDAYS.map((name, i) => {
      const key = toKey(new Date(monday + i * DAY));
      return { name, key, day: Number(key.slice(8)), has: !!this.byDay().get(key)?.length };
    });
  });
  protected readonly weekLabel = computed(() => {
    const days = this.week();
    return `${fmt(days[0].key, { month: 'short', day: 'numeric' })} - ${fmt(days[6].key, { month: 'short', day: 'numeric' })}`;
  });
  protected readonly dayPosts = computed(() => this.postsOn(this.selectedDay()));

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
    effect(() => {
      const status = this.statusQuery() ?? '';
      const list = !!status || this.viewQuery() === 'list' || !!this.searchQuery();
      untracked(() => {
        this.status.set(status);
        this.layout.set(list ? 'list' : 'calendar');
      });
    });
    this.measure();
    inject(DestroyRef).onDestroy(() => clearTimeout(this.toastTimer));
  }

  protected measure(): void {
    this.pillCount.set(innerHeight >= 860 && innerWidth >= 1100 ? 2 : 1);
  }

  // ---- Display helpers ------------------------------------------------------------------------------

  protected title(r: PostListItem): string {
    return r.hook || r.bodyExcerpt;
  }

  protected tone(status: CustomerStatus): string {
    return STATUS_CLASS[status];
  }

  protected artOf(r: PostListItem): string {
    return ARTS[art(r.id) % ARTS.length];
  }

  /** The poster's two-line words: the post's first two short words that are not filler (as the All posts thumbnails). */
  protected artText(r: PostListItem): string {
    const words = this.title(r).split(/\s+/).map((w) => w.replace(/[^\p{L}\p{N}'’]/gu, '')).filter(Boolean);
    const picked = words.filter((w) => w.length >= 3 && w.length <= 7 && !FILLER.has(w.toLowerCase()));
    return ((picked.length ? picked : words).slice(0, 2).join('\n') || 'YOUR\nIDEAS').toUpperCase();
  }

  protected imageUrl(r: PostListItem): string | null {
    return r.mediaAssetId ? this.media.previewUrl(this.workspace.orgId()!, r.mediaAssetId) : null;
  }

  protected postsOn(key: string): Row[] {
    return this.byDay().get(key) ?? [];
  }

  protected niceDate(key: string): string {
    return fmt(key, { weekday: 'long', month: 'short', day: 'numeric', year: 'numeric' });
  }

  protected dayLabel(key: string): string {
    return fmt(key, { weekday: 'long', month: 'short', day: 'numeric' });
  }

  protected shortDate(r: Row): string {
    return formatInZone(r.publishAt ?? r.updatedAtUtc, this.workspace.timeZone(), { month: 'short', day: 'numeric' });
  }

  /** What the post's day on the calendar means. */
  protected whenText(r: Row): string {
    const zone = this.workspace.timeZone();
    if (!r.publishAt) return `Last updated ${formatInZone(r.updatedAtUtc, zone, { weekday: 'long', month: 'short', day: 'numeric', year: 'numeric' })}`;
    const at = formatInZone(r.publishAt, zone, { weekday: 'long', month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
    return r.status === 'Published' ? `Published ${at}` : r.status === 'Failed' ? `Was due ${at}` : r.status === 'Demo (not sent)' ? `Demo run ${at}` : `Scheduled for ${at}`;
  }

  protected canChangeSchedule(r: Row): boolean {
    return r.status === 'Scheduled' && !!r.latestJobId && this.workspace.canManageContent();
  }

  // ---- Navigation -----------------------------------------------------------------------------------

  protected shiftMonth(delta: number): void {
    const [year, month] = this.month().split('-').map(Number);
    this.month.set(new Date(Date.UTC(year, month - 1 + delta, 1)).toISOString().slice(0, 7));
    this.selectedDay.set(`${this.month()}-01`);
  }

  protected goToday(): void {
    this.selectDay(this.today());
  }

  protected selectDay(key: string): void {
    this.selectedDay.set(key);
    this.month.set(key.slice(0, 7));
  }

  protected shiftWeek(delta: number): void {
    this.selectDay(toKey(new Date(toDate(this.selectedDay()).getTime() + delta * 7 * DAY)));
  }

  // ---- Drawer, modal, toast -------------------------------------------------------------------------

  /** Details show the full current text of the post. */
  protected async openPost(row: Row, event?: Event): Promise<void> {
    event?.stopPropagation();
    this.drawer.set({ kind: 'post', row, text: null });
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    try {
      const v = (await firstValueFrom(this.postsApi.get(orgId, row.id))).currentVersion;
      const drawer = this.drawer();
      if (drawer?.kind === 'post' && drawer.row === row) this.drawer.set({ kind: 'post', row, text: postText(v.hook ? { ...v, hook: null } : v) });
    } catch (error) {
      this.drawer.set(null);
      this.problem.set(toApiProblem(error));
    }
  }

  protected openDay(key: string, event: Event): void {
    event.stopPropagation();
    this.drawer.set({ kind: 'day', day: key });
  }

  protected escape(): void {
    this.drawer.set(null);
  }

  private toast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toastText.set(message);
    this.toastTimer = setTimeout(() => this.toastText.set(null), 2600);
  }

  protected edit(row: Row): void {
    this.drawer.set(null);
    void this.router.navigate(['/composer', row.id]);
  }

  /** Moves the post's scheduled job; the server re-checks approval and that dispatch has not started. */
  protected async reschedule(row: Row): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !row.latestJobId) return;
    try {
      const job = await firstValueFrom(this.publications.get(orgId, row.latestJobId));
      this.rescheduling = job.id;
      this.drawer.set(null);
      this.dialog()?.open('Change schedule', 'Save', this.workspace.timeZone(), job);
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
      this.toast(`Rescheduled for ${formatInZone(saved.publishAtUtc, this.workspace.timeZone())}`);
      await this.load(orgId);
      return null;
    } catch (error) {
      return toApiProblem(error);
    }
  };

  // ---- Data -----------------------------------------------------------------------------------------

  protected reload(): void {
    const orgId = this.workspace.orgId();
    if (orgId) void this.load(orgId);
  }

  private async load(orgId: string): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      const all = await firstValueFrom(this.postsApi.listAll(orgId));
      // Admins plan the whole workspace; a Creator's Planner (and its sidebar counts) covers their own posts.
      const me = this.auth.me()?.userId;
      const posts = this.workspace.isAdmin() ? all : all.filter((p) => p.createdByUserId === me);
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
