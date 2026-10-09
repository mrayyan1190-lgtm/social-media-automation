import { Component, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { CUSTOMER_STATUSES, CustomerStatus, customerStatus, statusGroup } from '../../shared/models/states';
import { SOCIAL_PLATFORMS } from '../../shared/models/platform.model';
import { PostListItem } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { ScheduleDialogComponent } from '../publications/components/schedule-dialog.component';
import { LocalSchedule } from '../publications/models/publication.model';
import { PublicationApiService } from '../publications/services/publication-api.service';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';
import { ToastService } from '../../core/services/toast.service';

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
  imports: [FormsModule, NgTemplateOutlet, RouterLink, PlatformIconComponent, ProblemBannerComponent, ScheduleDialogComponent],
  templateUrl: './planner.page.html',
  styleUrl: './planner.page.css',
})
export class PlannerPage {
  private readonly postsApi = inject(PostApiService);
  private readonly auth = inject(AuthService);
  private readonly publications = inject(PublicationApiService);
  private readonly toast = inject(ToastService);
  private readonly dialog = viewChild.required(ScheduleDialogComponent);
  /** The scheduled job the open dialog moves. */
  private rescheduling: string | null = null;
  protected readonly workspace = inject(WorkspaceService);

  /** Query parameters from the sidebar shortcuts: ?status=Draft (a list filter) or ?view=list. */
  readonly statusQuery = input<string>(undefined, { alias: 'status' });
  readonly viewQuery = input<string>(undefined, { alias: 'view' });
  /** ?q= from the topbar search: matches the post text and author. */
  readonly searchQuery = input<string>(undefined, { alias: 'q' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly statuses = CUSTOMER_STATUSES;
  protected readonly thisMonth = new Date().toISOString().slice(0, 7);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly rows = signal<Row[]>([]);
  protected readonly layout = signal<'calendar' | 'list'>('calendar');

  protected readonly platform = signal('');
  protected readonly status = signal('');
  protected readonly range = signal<DateRange>('');
  protected readonly month = signal(this.thisMonth);

  protected readonly filtersActive = computed(() => !!(this.platform() || this.status() || this.range() || this.searchQuery()));

  protected readonly filtered = computed(() => {
    const zone = this.workspace.timeZone();
    const today = dayKeyInZone(new Date().toISOString(), zone);
    const shift = (days: number) => dayKeyInZone(new Date(Date.now() + days * 864e5).toISOString(), zone);
    const q = (this.searchQuery() ?? '').trim().toLowerCase();
    return this.rows().filter((r) => {
      if (q && ![r.hook, r.bodyExcerpt, r.createdByName].some((t) => t?.toLowerCase().includes(q))) return false;
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
    });
  });

  /** "Drafts", "Scheduled posts"… when opened from a Publisher shortcut (or filtered by status). */
  protected readonly listTitle = computed(() => {
    const status = this.status();
    if (this.searchQuery()?.trim()) return `Results for “${this.searchQuery()!.trim()}”`;
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
    this.status.set('');
    this.range.set('');
  }

  /** Moves the post's scheduled job; the server re-checks approval and that dispatch has not started. */
  protected async reschedule(r: Row): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !r.latestJobId) return;
    try {
      const job = await firstValueFrom(this.publications.get(orgId, r.latestJobId));
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
      this.toast.success(`Rescheduled for ${this.time(saved.publishAtUtc)}.`);
      await this.load(orgId);
      return null;
    } catch (error) {
      return toApiProblem(error);
    }
  };

  protected async cancelSchedule(r: Row): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !r.latestJobId || !confirm('Cancel this scheduled post? It has not been sent yet.')) return;
    try {
      const job = await firstValueFrom(this.publications.get(orgId, r.latestJobId));
      await firstValueFrom(this.publications.cancel(orgId, job));
      this.toast.success('Schedule canceled. Nothing was sent.');
      await this.load(orgId);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected async deleteDraft(r: Row): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !confirm('Delete this draft? It will be removed from all lists.')) return;
    try {
      await firstValueFrom(this.postsApi.archive(orgId, await firstValueFrom(this.postsApi.get(orgId, r.id))));
      this.toast.success('Draft deleted.');
      await this.load(orgId);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
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
