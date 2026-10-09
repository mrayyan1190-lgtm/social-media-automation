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

/**
 * Schedule / Publish Now for the approved current version of one post, plus that post's publication jobs. Both actions
 * only create a durable job; the outcome appears later as the job's delivery state. Styled by the Approvals page (ap-*).
 */
@Component({
  selector: 'app-publication-panel',
  imports: [ProblemBannerComponent, PublicationJobComponent, ScheduleDialogComponent],
  template: `
    @if (canSchedule()) {
      <div class="ap-actions">
        <button class="ap-btn" type="button" [disabled]="busy()" (click)="openSchedule()">Schedule</button>
        <button class="ap-btn ghost" type="button" [disabled]="busy()" (click)="publishNow()">{{ busy() ? 'Working…' : 'Publish Now' }}</button>
      </div>
    } @else if (post().editorialState !== 'APPROVED') {
      <p class="ap-note">Only approved content can be scheduled or published. Submit this post for review first.</p>
    } @else {
      <p class="ap-note">This post is approved. The Admin schedules or publishes it.</p>
    }
    @if (success(); as message) { <p class="ap-banner ok" role="status">{{ message }}</p> }
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
  readonly post = input.required<Post>();
  /** The post's publication jobs after every (re)load, so the page can show the delivery status. */
  readonly jobsChange = output<PublicationJob[]>();

  private readonly api = inject(PublicationApiService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly dialog = viewChild.required(ScheduleDialogComponent);

  protected readonly jobs = signal<PublicationJob[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly success = signal<string | null>(null);

  /** Admins schedule and publish; Creators only see the status (the API enforces it too). */
  protected readonly canSchedule = computed(() => this.workspace.isAdmin() && this.post().editorialState === 'APPROVED');

  private poll?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      const post = this.post();
      if (orgId) void this.load(orgId, post.id);
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.poll));
  }

  protected openSchedule(): void {
    this.dialog().open('Schedule post', 'Schedule', this.workspace.timeZone());
  }

  /** Called by the dialog; returns a problem to show there, or null when the job was saved. */
  protected readonly submitSchedule = async (when: LocalSchedule): Promise<ApiProblem | null> => {
    const orgId = this.workspace.orgId();
    if (!orgId) return null;
    const post = this.post();
    try {
      const saved = await firstValueFrom(this.api.schedule(orgId, post.id, post.currentVersionId, when, post.rowVersion));
      this.success.set(`Scheduled for ${formatInZone(saved.publishAtUtc, this.workspace.timeZone())} (${this.workspace.timeZone()}).`);
      this.problem.set(null);
      await this.load(orgId, post.id);
      return null;
    } catch (error) {
      return toApiProblem(error);
    }
  };

  protected async publishNow(): Promise<void> {
    const post = this.post();
    if (!confirm(`Publish this post now? It will be sent to the selected account within moments.`)) return;
    await this.run(
      (orgId) => firstValueFrom(this.api.publishNow(orgId, post.id, post.currentVersionId, post.rowVersion)),
      'Publishing now. The status below updates automatically.',
    );
  }

  protected async cancel(job: PublicationJob): Promise<void> {
    if (!confirm('Cancel this scheduled post? It has not been sent yet.')) return;
    await this.run((orgId) => firstValueFrom(this.api.cancel(orgId, job)), 'Schedule canceled. Nothing was sent.');
  }

  protected reload(): void {
    const orgId = this.workspace.orgId();
    this.problem.set(null);
    if (orgId) void this.load(orgId, this.post().id);
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
      await this.load(orgId, this.post().id);
    }
  }

  private async load(orgId: string, postId: string): Promise<void> {
    try {
      this.jobs.set(await firstValueFrom(this.api.list(orgId, { postId })));
      this.jobsChange.emit(this.jobs());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
    // The worker runs independently; follow due or in-flight jobs until they settle.
    clearTimeout(this.poll);
    if (this.jobs().some((j) => isActive(j))) this.poll = setTimeout(() => void this.load(orgId, postId), 3000);
  }
}
