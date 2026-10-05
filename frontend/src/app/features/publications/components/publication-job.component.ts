import { Component, computed, inject, input, output } from '@angular/core';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { DELIVERY_LABELS, DEMO_MODE_NOTE, PROBLEM_DELIVERY, deliveryTone, publishingProblem } from '../../../shared/models/states';
import { PublicationJob } from '../models/publication.model';
import { formatInZone } from '../models/time-zones';

/**
 * One publication of a post in customer language: status, version, destination, time, the result link and what to do
 * when it failed. Worker, lease, payload and provider internals are never shown here.
 */
@Component({
  selector: 'app-publication-job',
  template: `
    @let j = job();
    <div class="job">
      <div class="job-head">
        <span [class]="'badge delivery ' + tone(j.deliveryState)">{{ labels[j.deliveryState] }}</span>
        <span class="badge version">Version {{ j.versionNumber }}</span>
        <span class="muted">→ {{ j.destinationName }}</span>
        @if (j.executionMode === 'SIMULATED') { <span class="badge mode simulated" [title]="demoNote">Demo Mode</span> }
      </div>
      <span class="hint">
        {{ j.localPublishAt ? 'Scheduled for' : 'Publish now ·' }} {{ time(j.publishAtUtc) }} ({{ workspace.timeZone() }})
      </span>
      @if (j.deliveryState === 'SIMULATED') { <span class="hint">Demo Mode: nothing was sent externally.</span> }
      @if (j.externalPostUrl) { <a [href]="j.externalPostUrl" target="_blank" rel="noopener">View on {{ j.platform }}</a> }
      @if (problem()) { <span class="banner error">{{ problem() }}</span> }
      @if (canAct() && (j.canReschedule || j.canCancel)) {
        <div class="actions">
          @if (j.canReschedule) {
            <button type="button" [disabled]="busy()" (click)="reschedule.emit(j)">{{ j.deliveryState === 'SCHEDULED' ? 'Change Schedule' : 'Schedule again' }}</button>
          }
          @if (j.canCancel) { <button type="button" class="danger" [disabled]="busy()" (click)="cancel.emit(j)">Cancel Schedule</button> }
        </div>
      }
    </div>
  `,
})
export class PublicationJobComponent {
  readonly job = input.required<PublicationJob>();
  readonly busy = input(false);
  readonly reschedule = output<PublicationJob>();
  readonly cancel = output<PublicationJob>();

  protected readonly workspace = inject(WorkspaceService);
  protected readonly labels = DELIVERY_LABELS;
  protected readonly tone = deliveryTone;
  protected readonly demoNote = DEMO_MODE_NOTE;
  protected readonly canAct = computed(() => this.workspace.canManageCampaigns());
  protected readonly problem = computed(() => {
    const j = this.job();
    return PROBLEM_DELIVERY.includes(j.deliveryState) ? publishingProblem(j.deliveryState, j.lastErrorCode, j.platform) : null;
  });

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone());
  }
}
