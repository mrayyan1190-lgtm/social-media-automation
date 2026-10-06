import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { CUSTOMER_STATUSES, CustomerStatus, customerStatus, statusGroup, statusTone } from '../../shared/models/states';
import { SOCIAL_PLATFORMS } from '../campaigns/models/campaign.model';
import { PostListItem } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { dayKeyInZone, formatInZone } from '../publications/models/time-zones';

type DateRange = '' | 'today' | 'next7' | 'last7' | 'month';

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
  imports: [FormsModule, RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Planner</h1>
        <p class="muted">Track every post from draft to published. Times in {{ workspace.timeZone() }}.</p>
      </div>
      <div class="actions">
        <div class="segmented" role="group" aria-label="View">
          <button type="button" [class.on]="layout() === 'calendar'" (click)="layout.set('calendar')"><app-icon name="calendar" [size]="15" />Calendar</button>
          <button type="button" [class.on]="layout() === 'list'" (click)="layout.set('list')"><app-icon name="list" [size]="15" />List</button>
        </div>
        @if (workspace.canManageCampaigns()) {
          <a class="button primary" routerLink="/composer"><app-icon name="plus" [size]="16" />Create Post</a>
        }
      </div>
    </div>

    <div class="filter-bar">
      <select aria-label="Platform" [ngModel]="platform()" (ngModelChange)="platform.set($event)">
        <option value="">All platforms</option>
        @for (p of platforms; track p) { <option [value]="p">{{ p }}</option> }
      </select>
      <select aria-label="Campaign" [ngModel]="campaign()" (ngModelChange)="campaign.set($event)">
        <option value="">All campaigns</option>
        <option value="none">No campaign</option>
        @for (c of campaigns(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
      </select>
      <select aria-label="Status" [ngModel]="status()" (ngModelChange)="status.set($event)">
        <option value="">All statuses</option>
        @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
      </select>
      @if (layout() === 'list') {
        <select aria-label="Date" [ngModel]="range()" (ngModelChange)="range.set($event)">
          <option value="">Any date</option>
          <option value="today">Today</option>
          <option value="next7">Next 7 days</option>
          <option value="last7">Last 7 days</option>
          <option value="month">This month</option>
        </select>
      }
      @if (filtersActive()) { <button type="button" class="link" (click)="clear()">Clear</button> }
    </div>

    <app-problem-banner [problem]="problem()" />

    @if (loading() && !rows().length) {
      <p class="muted">Loading…</p>
    } @else if (!rows().length) {
      <section class="empty-state">
        <app-icon name="calendar" [size]="26" />
        <h2>No posts yet</h2>
        <p class="muted">Write a post in Composer or let AI Studio draft one.</p>
        @if (workspace.canManageCampaigns()) {
          <div class="actions">
            <a class="button primary" routerLink="/composer">Create Post</a>
            <a class="button" routerLink="/ai-studio">Create with AI</a>
          </div>
        }
      </section>
    } @else if (layout() === 'calendar') {
      <section class="calendar card flush-top">
        <div class="calendar-head">
          <button type="button" aria-label="Previous month" (click)="shiftMonth(-1)">‹</button>
          <h2>{{ monthLabel() }}</h2>
          <button type="button" aria-label="Next month" (click)="shiftMonth(1)">›</button>
          <button type="button" class="small" (click)="month.set(thisMonth)">Today</button>
        </div>
        <div class="calendar-grid">
          @for (d of ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']; track d) { <div class="dow">{{ d }}</div> }
          @for (week of weeks(); track $index) {
            @for (day of week; track day.date) {
              <div class="day-cell" [class.out]="!day.inMonth" [class.today]="day.today">
                <span class="day-num">{{ day.day }}</span>
                @for (p of day.posts.slice(0, 3); track p.id) {
                  <a [class]="'cal-item ' + tone(p.status)" [routerLink]="['/composer', p.id]" [title]="title(p) + ' · ' + p.status">
                    <app-platform-icon [platform]="p.platform" />
                    <span class="cal-time">{{ time(p.publishAt, { timeStyle: 'short' }) }}</span>
                    <span>{{ p.campaignName || title(p) }}</span>
                  </a>
                }
                @if (day.posts.length > 3) { <span class="hint more">+{{ day.posts.length - 3 }} more</span> }
              </div>
            }
          }
        </div>
        <p class="hint tight">Scheduled, published and failed posts appear on their date. Drafts and posts in review are in the List view.</p>
      </section>
    } @else {
      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr><th>Post</th><th>Platform</th><th>Campaign</th><th>Status</th><th>Scheduled / Published</th><th>Updated</th></tr>
          </thead>
          <tbody>
            @for (r of filtered(); track r.id) {
              <tr class="clickable" [routerLink]="['/composer', r.id]">
                <td class="post-cell"><a [routerLink]="['/composer', r.id]">{{ title(r) }}</a></td>
                <td><app-platform-icon [platform]="r.platform" [label]="true" /></td>
                <td>
                  @if (r.campaignName) { <span class="nowrap"><span class="color-dot sm" [style.background]="r.campaignColor"></span>{{ r.campaignName }}</span> }
                  @else { <span class="hint">—</span> }
                </td>
                <td><span [class]="'badge delivery ' + tone(r.status)">{{ r.status }}</span></td>
                <td class="nowrap">{{ time(r.publishAt) }}</td>
                <td class="nowrap hint">{{ time(r.updatedAtUtc, { dateStyle: 'medium' }) }}</td>
              </tr>
            } @empty {
              <tr><td colspan="6" class="empty">No posts match these filters.</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class PlannerPage {
  private readonly postsApi = inject(PostApiService);
  protected readonly workspace = inject(WorkspaceService);

  /** Query parameters from the sidebar shortcuts: ?status=Draft (a list filter) or ?view=list. */
  readonly statusQuery = input<string>(undefined, { alias: 'status' });
  readonly viewQuery = input<string>(undefined, { alias: 'view' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly statuses = CUSTOMER_STATUSES;
  protected readonly tone = statusTone;
  protected readonly thisMonth = new Date().toISOString().slice(0, 7);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly rows = signal<Row[]>([]);
  protected readonly layout = signal<'calendar' | 'list'>('calendar');

  protected readonly platform = signal('');
  protected readonly campaign = signal('');
  protected readonly status = signal('');
  protected readonly range = signal<DateRange>('');
  protected readonly month = signal(this.thisMonth);

  protected readonly campaigns = computed(() =>
    [...new Map(this.rows().filter((r) => r.campaignId).map((r) => [r.campaignId!, r.campaignName!])).entries()]
      .map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name)));

  protected readonly filtersActive = computed(() => !!(this.platform() || this.campaign() || this.status() || this.range()));

  protected readonly filtered = computed(() => {
    const zone = this.workspace.timeZone();
    const today = dayKeyInZone(new Date().toISOString(), zone);
    const shift = (days: number) => dayKeyInZone(new Date(Date.now() + days * 864e5).toISOString(), zone);
    return this.rows().filter((r) => {
      if (this.platform() && r.platform !== this.platform()) return false;
      if (this.campaign() === 'none' ? !!r.campaignId : this.campaign() && r.campaignId !== this.campaign()) return false;
      if (this.status() && statusGroup(r.status) !== this.status()) return false;
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

  /** Month grid (weeks of yyyy-MM-dd days, Monday first) with the posts publishing on each day. */
  protected readonly weeks = computed(() => {
    const [year, month] = this.month().split('-').map(Number);
    const first = new Date(Date.UTC(year, month - 1, 1));
    const start = new Date(first.getTime() - ((first.getUTCDay() + 6) % 7) * 864e5);
    const zone = this.workspace.timeZone();
    const byDay = new Map<string, Row[]>();
    for (const r of this.filtered()) {
      if (!r.publishAt) continue;
      const key = dayKeyInZone(r.publishAt, zone);
      byDay.set(key, [...(byDay.get(key) ?? []), r]);
    }
    for (const posts of byDay.values()) posts.sort((a, b) => a.publishAt!.localeCompare(b.publishAt!));
    const today = dayKeyInZone(new Date().toISOString(), zone);
    return Array.from({ length: 6 }, (_, w) => Array.from({ length: 7 }, (_, d) => {
      const date = new Date(start.getTime() + (w * 7 + d) * 864e5).toISOString().slice(0, 10);
      return { date, day: Number(date.slice(8)), inMonth: date.slice(0, 7) === this.month(), today: date === today, posts: byDay.get(date) ?? [] };
    })).filter((week) => week.some((day) => day.inMonth));
  });

  protected readonly monthLabel = computed(() => {
    const [year, month] = this.month().split('-').map(Number);
    return new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)));
  });

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
    effect(() => {
      const status = this.statusQuery() ?? '';
      const list = !!status || this.viewQuery() === 'list';
      untracked(() => {
        this.status.set(status);
        this.layout.set(list ? 'list' : 'calendar');
      });
    });
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
    this.campaign.set('');
    this.status.set('');
    this.range.set('');
  }

  private async load(orgId: string): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      const posts = await firstValueFrom(this.postsApi.listAll(orgId));
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
