import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { PlatformIconComponent } from '../../../shared/components/platform-icon.component';
import { DELIVERY_LABELS, DEMO_MODE_NOTE, PROBLEM_DELIVERY, deliveryTone, publishingProblem } from '../../../shared/models/states';

/** Chip colour per delivery tone (Approvals design). */
const CHIP: Record<string, string> = { success: 'ap-approved', danger: 'ap-attn', waiting: 'ap-scheduled', simulated: 'ap-neutral', neutral: 'ap-neutral' };
import { PublicationJob } from '../models/publication.model';
import { formatInZone } from '../models/time-zones';

/**
 * One publication of a post in customer language: status, version, destination, time, the result link and what to do
 * when it failed. Worker, lease, payload and provider internals are never shown here.
 */
@Component({
  selector: 'app-publication-job',
  imports: [RouterLink, PlatformIconComponent],
  template: `
    @let j = job();
    <div class="ap-sched">
      <div class="ap-sched-top">
        <span [class]="'ap-chip ' + chip(j.deliveryState)" [title]="problem() ?? ''"><span class="dot"></span>{{ labels[j.deliveryState] }}</span>
        <span class="ap-acct"><app-platform-icon [platform]="j.platform" />{{ j.destinationName }}</span>
        @if (j.executionMode === 'SIMULATED') { <span class="ap-chip ap-neutral" [title]="demoNote">Demo Mode</span> }
        @if (canAct() && j.canCancel) {
          <span class="right"><button type="button" class="ap-small-btn cancel" [disabled]="busy()" (click)="cancel.emit(j)">Cancel Schedule</button></span>
        }
      </div>
      <div class="ap-sched-sub">
        <span>{{ j.localPublishAt ? 'Scheduled' : 'Publishing' }} {{ time(j.publishAtUtc) }} · {{ workspace.timeZone() }}</span>
        @if (j.externalPostUrl) {
          <a class="ap-view" [href]="j.externalPostUrl" target="_blank" rel="noopener">View on {{ j.platform }}</a>
        } @else if (needsAccount() && canAct()) {
          <a class="ap-fix" routerLink="/social-accounts">Fix {{ j.platform }} connection
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="M13 6l6 6-6 6"/></svg></a>
        }
      </div>
    </div>
  `,
})
export class PublicationJobComponent {
  readonly job = input.required<PublicationJob>();
  readonly busy = input(false);
  readonly cancel = output<PublicationJob>();

  protected readonly workspace = inject(WorkspaceService);
  protected readonly labels = DELIVERY_LABELS;
  protected readonly demoNote = DEMO_MODE_NOTE;
  protected readonly canAct = computed(() => this.workspace.isAdmin());
  protected readonly problem = computed(() => {
    const j = this.job();
    return PROBLEM_DELIVERY.includes(j.deliveryState) ? publishingProblem(j.deliveryState, j.lastErrorCode, j.platform) : null;
  });

  /** The account must be reconnected before this can go out (the problem text says the same in full). */
  protected readonly needsAccount = computed(() => {
    const j = this.job();
    return j.deliveryState === 'ACTION_REQUIRED' && /REAUTH|TOKEN|AUTH|NOT_READY|PERMISSION/i.test(j.lastErrorCode ?? 'AUTH');
  });

  protected chip(state: string): string {
    return CHIP[deliveryTone(state)];
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone());
  }
}
