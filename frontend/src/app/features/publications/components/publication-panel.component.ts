import { Component, DestroyRef, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { Post } from '../../content/models/post.model';
import { LocalSchedule, PublicationJob, isActive } from '../models/publication.model';
import { formatInZone } from '../models/time-zones';
import { PublicationApiService } from '../services/publication-api.service';
import { PublicationJobComponent } from './publication-job.component';
import { ScheduleDialogComponent } from './schedule-dialog.component';
import { NoticeComponent } from '../../../shared/components/notice.component';

/**
 * Schedule / Publish Now for the approved current versions of one post on one or several platforms (a post group),
 * plus their publication jobs. Each platform's post still gets its own job; both actions only create durable jobs and
 * the outcome appears later as each job's delivery state. Styled by the Approvals page (ap-*).
 */
@Component({
  selector: 'app-publication-panel',
  imports: [NoticeComponent, ProblemBannerComponent, PublicationJobComponent, ScheduleDialogComponent],
  template: `
    @if (canSchedule()) {
      <div class="ap-actions">
        <button class="ap-btn" type="button" [disabled]="busy()" (click)="openSchedule()">Schedule</button>
        <button class="ap-btn ghost" type="button" [disabled]="busy()" (click)="publishNow()">{{ busy() ? 'Working…' : 'Publish Now' }}</button>
      </div>
    } @else if (!allApproved()) {
      <p class="ap-note">Only approved content can be scheduled or published. Submit this post for review first.</p>
    } @else {
      <p class="ap-note">This post is approved. The Admin schedules or publishes it.</p>
    }
    <app-notice [message]="success()" />
    <app-problem-banner [problem]="problem()" (reload)="reload()" />
    @if (!loading()) {
      @for (j of jobs(); track j.id) {
        <app-publication-job [job]="j" [busy]="busy()" (cancel)="cancel($event)" />
      }
    }
    <app-schedule-dialog [submit]="submitSchedule" />
  `,
})
export class PublicationPanelComponent {
  /** The approved posts scheduled together: one post, or every platform's post of a group. */
  readonly posts = input.required<Post[]>();
  /** The posts' publication jobs after every (re)load, so the page can show the delivery status. */
  readonly jobsChange = output<PublicationJob[]>();

  private readonly api = inject(PublicationApiService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly dialog = viewChild.required(ScheduleDialogComponent);

  protected readonly jobs = signal<PublicationJob[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly success = signal<string | null>(null);

  protected readonly allApproved = computed(() => this.posts().every((p) => p.editorialState === 'APPROVED'));
  /** Admins schedule and publish; Creators only see the status (the API enforces it too). */
  protected readonly canSchedule = computed(() => this.workspace.isAdmin() && this.allApproved());

  private poll?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      this.posts();
      if (orgId) void this.load(orgId);
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.poll));
  }

  protected openSchedule(): void {
    this.dialog().open('Schedule post', 'Schedule', this.workspace.timeZone());
  }

  /** Called by the dialog; returns a problem to show there, or null when every platform's job was saved. */
  protected readonly submitSchedule = async (when: LocalSchedule): Promise<ApiProblem | null> => {
    const orgId = this.workspace.orgId();
    if (!orgId) return null;
    try {
      let saved: PublicationJob | null = null;
      for (const post of this.unscheduled()) saved = await this.forPlatform(post, firstValueFrom(this.api.schedule(orgId, post.id, post.currentVersionId, when, post.rowVersion)));
      if (saved) this.success.set(`Scheduled for ${formatInZone(saved.publishAtUtc, this.workspace.timeZone())} (${this.workspace.timeZone()}).`);
      this.problem.set(null);
      return null;
    } catch (error) {
      return toApiProblem(error);
    } finally {
      await this.load(orgId);
    }
  };

  protected async publishNow(): Promise<void> {
    const many = this.posts().length > 1;
    if (!confirm(`Publish this post now${many ? ` on ${this.posts().map((p) => p.platform).join(', ')}` : ''}? It will be sent to the selected account${many ? 's' : ''} within moments.`)) return;
    await this.run(async (orgId) => {
      for (const post of this.unscheduled()) await this.forPlatform(post, firstValueFrom(this.api.publishNow(orgId, post.id, post.currentVersionId, post.rowVersion)));
    }, 'Publishing now. The status below updates automatically.');
  }

  protected async cancel(job: PublicationJob): Promise<void> {
    if (!confirm('Cancel this scheduled post? It has not been sent yet.')) return;
    await this.run((orgId) => firstValueFrom(this.api.cancel(orgId, job)), 'Schedule canceled. Nothing was sent.');
  }

  protected reload(): void {
    const orgId = this.workspace.orgId();
    this.problem.set(null);
    if (orgId) void this.load(orgId);
  }

  /**
   * Posts of the group that have no live job for their current version yet, so a second click after a partial failure
   * only schedules the platforms that are still missing (the server refuses duplicates anyway).
   */
  private unscheduled(): Post[] {
    return this.posts().filter((p) => !this.jobs().some((j) => j.postId === p.id && j.postVersionId === p.currentVersionId
      && j.deliveryState !== 'CANCELED' && j.deliveryState !== 'FAILED'));
  }

  /** Names the platform in a failure when several are scheduled together. */
  private async forPlatform<T>(post: Post, call: Promise<T>): Promise<T> {
    try {
      return await call;
    } catch (error) {
      throw this.posts().length > 1 ? new Error(`${post.platform}: ${toApiProblem(error).detail}`) : error;
    }
  }

  private async run(action: (orgId: string) => Promise<unknown>, successMessage: string): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.busy.set(true);
    this.problem.set(null);
    this.success.set(null);
    try {
      await action(orgId);
      this.success.set(successMessage);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
      await this.load(orgId);
    }
  }

  private async load(orgId: string): Promise<void> {
    try {
      const lists = await Promise.all(this.posts().map((p) => firstValueFrom(this.api.list(orgId, { postId: p.id }))));
      this.jobs.set(lists.flat());
      this.jobsChange.emit(this.jobs());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
    // The worker runs independently; follow due or in-flight jobs until they settle.
    clearTimeout(this.poll);
    if (this.jobs().some((j) => isActive(j))) this.poll = setTimeout(() => void this.load(orgId), 3000);
  }
}
