import { Component, DestroyRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { DELIVERY_LABELS, DEMO_MODE_NOTE, PROBLEM_DELIVERY, UPCOMING_DELIVERY, deliveryTone, publishingProblem } from '../../shared/models/states';
import { PostApiService } from '../content/services/post-api.service';
import { ScheduleDialogComponent } from '../publications/components/schedule-dialog.component';
import { LocalSchedule, PublicationJob, isActive } from '../publications/models/publication.model';
import { formatInZone } from '../publications/models/time-zones';
import { PublicationApiService } from '../publications/services/publication-api.service';

type Kind = 'scheduled' | 'published' | 'failed';

const PAGES: Record<Kind, { title: string; subtitle: string; states: string[]; empty: string; icon: string }> = {
  scheduled: { title: 'Scheduled', subtitle: 'Approved posts waiting for their publishing time.', states: UPCOMING_DELIVERY, empty: 'No posts scheduled.', icon: 'clock' },
  published: { title: 'Published', subtitle: 'Posts that were published to your social accounts.', states: ['PUBLISHED', 'SIMULATED'], empty: 'No published content yet.', icon: 'globe' },
  failed: { title: 'Failed', subtitle: 'Posts that could not be published, with what to do next.', states: PROBLEM_DELIVERY, empty: 'Nothing failed. All good.', icon: 'checkCircle' },
};

/**
 * Publishing outcomes. Scheduling and Publish Now only ever create publication jobs; this page shows them in customer
 * language (no worker, lease or provider internals) and offers the safe actions the server allows.
 */
@Component({
  selector: 'app-publications-page',
  imports: [RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent, ScheduleDialogComponent],
  template: `
    @let page = pages[kind()];
    <div class="page-header">
      <div>
        <h1>{{ page.title }}</h1>
        <p class="muted">{{ page.subtitle }} Times in {{ workspace.timeZone() }}.</p>
      </div>
    </div>
    @if (success(); as message) { <p class="banner success" role="status">{{ message }}</p> }
    <app-problem-banner [problem]="problem()" />

    @if (loading() && !jobs().length) {
      <p class="muted">Loading…</p>
    } @else if (!main().length && !demo().length) {
      <section class="empty-hero compact">
        <app-icon [name]="page.icon" [size]="26" />
        <h2>{{ page.empty }}</h2>
        @if (kind() !== 'failed') { <p class="muted">Approved posts can be scheduled from the post page.</p> <a routerLink="/publisher/posts">Open All Posts</a> }
      </section>
    } @else {
      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr>
              <th>Platform</th><th>Campaign</th>
              <th>{{ kind() === 'published' ? 'Published' : kind() === 'failed' ? 'Was due' : 'Date / Time' }}</th>
              @if (kind() === 'scheduled') { <th>Created By</th> }
              <th>{{ kind() === 'failed' ? 'What happened' : 'Destination' }}</th>
              <th>Status</th><th></th>
            </tr>
          </thead>
          <tbody>
            @for (j of main(); track j.id) {
              <tr>
                <td><app-platform-icon [platform]="j.platform" [label]="true" /></td>
                <td><a [routerLink]="['/posts', j.postId]">{{ j.campaignName }}</a> <span class="hint">v{{ j.versionNumber }}</span></td>
                <td>{{ time(j.publishAtUtc) }}</td>
                @if (kind() === 'scheduled') { <td>{{ creators()[j.postId] || '—' }}</td> }
                @if (kind() === 'failed') {
                  <td class="problem-text">{{ explain(j) }}</td>
                } @else {
                  <td>{{ j.destinationName }}@if (j.executionMode === 'SIMULATED') { <span class="badge mode simulated" [title]="demoNote">Demo Mode</span> }</td>
                }
                <td><span [class]="'badge delivery ' + tone(j.deliveryState)">{{ labels[j.deliveryState] }}</span></td>
                <td class="row-actions">
                  <a [routerLink]="['/posts', j.postId]">View</a>
                  @if (j.externalPostUrl) { <a [href]="j.externalPostUrl" target="_blank" rel="noopener">View on {{ j.platform }}</a> }
                  @if (canAct() && j.canReschedule) { <button type="button" class="link" [disabled]="busy()" (click)="openReschedule(j)">{{ kind() === 'failed' ? 'Schedule again' : 'Change Schedule' }}</button> }
                  @if (canAct() && j.canCancel) { <button type="button" class="link danger" [disabled]="busy()" (click)="cancel(j)">Cancel Schedule</button> }
                  @if (kind() === 'failed' && workspace.isAdmin()) { <a routerLink="/social-accounts">Social Accounts</a> }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (demo().length) {
        <h2 class="section-title">Demo publications <span class="hint">{{ demoNote }}</span></h2>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>Platform</th><th>Campaign</th><th>Completed</th><th>Destination</th><th>Status</th></tr></thead>
            <tbody>
              @for (j of demo(); track j.id) {
                <tr>
                  <td><app-platform-icon [platform]="j.platform" [label]="true" /></td>
                  <td><a [routerLink]="['/posts', j.postId]">{{ j.campaignName }}</a></td>
                  <td>{{ time(j.publishAtUtc) }}</td>
                  <td>{{ j.destinationName }}</td>
                  <td><span class="badge delivery simulated">Demo (not sent)</span></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    }
    <app-schedule-dialog [submit]="submitReschedule" />
  `,
})
export class PublicationsPage {
  private readonly api = inject(PublicationApiService);
  private readonly postsApi = inject(PostApiService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly dialog = viewChild.required(ScheduleDialogComponent);

  readonly kind = input<Kind>('scheduled');

  protected readonly pages = PAGES;
  protected readonly labels = DELIVERY_LABELS;
  protected readonly tone = deliveryTone;
  protected readonly demoNote = DEMO_MODE_NOTE;
  protected readonly jobs = signal<PublicationJob[]>([]);
  protected readonly creators = signal<Record<string, string>>({});
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly success = signal<string | null>(null);
  protected readonly canAct = computed(() => this.workspace.canManageCampaigns());
  private readonly rescheduling = signal<PublicationJob | null>(null);
  private poll?: ReturnType<typeof setTimeout>;

  /** A demo delivery is never listed as published; it gets its own clearly labelled section. */
  protected readonly main = computed(() => this.jobs().filter((j) => j.deliveryState !== 'SIMULATED'));
  protected readonly demo = computed(() => (this.kind() === 'published' ? this.jobs().filter((j) => j.deliveryState === 'SIMULATED') : []));

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const kind = this.kind();
      if (orgId) void this.load(orgId, kind);
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.poll));
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone());
  }

  protected explain(job: PublicationJob): string {
    return publishingProblem(job.deliveryState, job.lastErrorCode, job.platform);
  }

  protected openReschedule(job: PublicationJob): void {
    this.rescheduling.set(job);
    this.dialog().open(job.deliveryState === 'SCHEDULED' ? 'Change schedule' : 'Schedule again', 'Save', this.workspace.timeZone(), job);
  }

  protected readonly submitReschedule = async (when: LocalSchedule): Promise<ApiProblem | null> => {
    const orgId = this.workspace.orgId();
    const job = this.rescheduling();
    if (!orgId || !job) return null;
    try {
      const saved = await firstValueFrom(this.api.reschedule(orgId, job, when));
      this.success.set(`Scheduled for ${this.time(saved.publishAtUtc)}.`);
      await this.load(orgId, this.kind());
      return null;
    } catch (error) {
      return toApiProblem(error);
    }
  };

  protected async cancel(job: PublicationJob): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !confirm('Cancel this scheduled post? It has not been sent.')) return;
    this.busy.set(true);
    this.problem.set(null);
    this.success.set(null);
    try {
      await firstValueFrom(this.api.cancel(orgId, job));
      this.success.set('Schedule canceled. Nothing was sent.');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
      await this.load(orgId, this.kind());
    }
  }

  private async load(orgId: string, kind: Kind): Promise<void> {
    this.loading.set(true);
    try {
      const [jobs, posts] = await Promise.all([
        firstValueFrom(this.api.list(orgId, { states: PAGES[kind].states })),
        kind === 'scheduled' ? firstValueFrom(this.postsApi.listAll(orgId)) : Promise.resolve([]),
      ]);
      const sorted = [...jobs].sort((a, b) => (kind === 'scheduled' ? a.publishAtUtc.localeCompare(b.publishAtUtc) : b.publishAtUtc.localeCompare(a.publishAtUtc)));
      this.jobs.set(sorted);
      this.creators.set(Object.fromEntries(posts.map((p) => [p.id, p.createdByName])));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
    // Due or in-flight posts change soon; refresh until they settle.
    clearTimeout(this.poll);
    if (this.jobs().some((j) => isActive(j))) this.poll = setTimeout(() => void this.load(orgId, this.kind()), 5000);
  }
}
