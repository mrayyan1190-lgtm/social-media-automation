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
 * Schedule / Publish Now for the approved current version of one post, plus that post's publication jobs.
 * Both actions only create a durable job; the outcome appears later as the job's delivery state.
 */
@Component({
  selector: 'app-publication-panel',
  imports: [ProblemBannerComponent, PublicationJobComponent, ScheduleDialogComponent],
  template: `
    <section class="card">
      <h2>Publishing</h2>
      @if (success(); as message) { <p class="banner success" role="status">{{ message }}</p> }
      <app-problem-banner [problem]="problem()" (reload)="reload()" />
      @if (canSchedule()) {
        <p class="hint">Version {{ post().currentVersion.versionNumber }} is approved. Schedule it or publish it now; it goes out even if you close the browser.</p>
        <div class="actions">
          <button class="primary" type="button" [disabled]="busy()" (click)="openSchedule()">Schedule</button>
          <button type="button" [disabled]="busy()" (click)="publishNow()">{{ busy() ? 'Working…' : 'Publish Now' }}</button>
        </div>
      } @else if (post().editorialState !== 'APPROVED') {
        <p class="hint">Only approved content can be scheduled or published. Submit this post for review first.</p>
      }
      @if (loading()) {
        <p class="hint">Loading…</p>
      } @else {
        @for (j of jobs(); track j.id) {
          <app-publication-job [job]="j" [busy]="busy()" (reschedule)="openReschedule($event)" (cancel)="cancel($event)" />
        } @empty { <p class="hint">Not scheduled yet.</p> }
      }
    </section>
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
  private readonly rescheduling = signal<PublicationJob | null>(null);

  protected readonly canSchedule = computed(() => this.workspace.canManageCampaigns() && this.post().editorialState === 'APPROVED');

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
    this.rescheduling.set(null);
    this.dialog().open(`Schedule version ${this.post().currentVersion.versionNumber}`, 'Schedule', this.workspace.timeZone());
  }

  protected openReschedule(job: PublicationJob): void {
    this.rescheduling.set(job);
    this.dialog().open(job.deliveryState === 'SCHEDULED' ? 'Change schedule' : 'Schedule again', 'Save', this.workspace.timeZone(), job);
  }

  /** Called by the dialog; returns a problem to show there, or null when the job was saved. */
  protected readonly submitSchedule = async (when: LocalSchedule): Promise<ApiProblem | null> => {
    const orgId = this.workspace.orgId();
    if (!orgId) return null;
    const job = this.rescheduling();
    const post = this.post();
    try {
      const saved = await firstValueFrom(job ? this.api.reschedule(orgId, job, when) : this.api.schedule(orgId, post.id, post.currentVersionId, when, post.rowVersion));
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
    if (!confirm(`Publish version ${post.currentVersion.versionNumber} now? It will be sent to the selected account within moments.`)) return;
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
