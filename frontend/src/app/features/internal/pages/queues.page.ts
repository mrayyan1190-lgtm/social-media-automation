import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { AiQueue, InternalApiService, PublishingQueue, ago, tone, when, words } from '../internal-api.service';

/** Durable work queues. Read-only: there is deliberately no "Retry All". */
@Component({
  selector: 'app-internal-queues-page',
  imports: [RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>Queues</h1><p class="muted">Active work and failures from the last 24 hours.</p></div><button type="button" (click)="load()">Refresh</button></div>
    <nav class="tabs">
      <button type="button" [class.active]="tab() === 'ai'" (click)="tab.set('ai')">AI Queue</button>
      <button type="button" [class.active]="tab() === 'publishing'" (click)="tab.set('publishing')">Publishing Queue</button>
    </nav>
    <app-problem-banner [problem]="problem()" />

    @if (tab() === 'ai') {
      @if (ai(); as q) {
        <div class="stat-cards small">
          <div class="stat"><span class="value">{{ q.summary.pending }}</span><span class="label">Pending</span></div>
          <div class="stat"><span class="value">{{ q.summary.processing }}</span><span class="label">Processing</span></div>
          <div class="stat"><span class="value">{{ q.summary.retryPending }}</span><span class="label">Retry Pending</span></div>
          <div class="stat" [class.danger]="q.summary.failedLast24Hours > 0"><span class="value">{{ q.summary.failedLast24Hours }}</span><span class="label">Failed (24 h)</span></div>
          <div class="stat"><span class="value small">{{ ago(q.summary.oldestPendingAtUtc) }}</span><span class="label">Oldest Pending</span></div>
        </div>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>Request ID</th><th>Workspace</th><th>Operation</th><th>Status</th><th>Attempts</th><th>Created</th><th>Started</th><th>Duration</th><th>Error</th></tr></thead>
            <tbody>
              @for (r of q.items; track r.id) {
                <tr>
                  <td><code>{{ r.id.slice(0, 8) }}</code></td>
                  <td><a [routerLink]="['/internal/workspaces', r.organizationId]">{{ r.workspaceName }}</a></td>
                  <td>{{ r.operation }}</td>
                  <td><span [class]="'badge delivery ' + tone(r.status)">{{ words(r.status) }}</span></td>
                  <td>{{ r.attempts }}/{{ r.maxAttempts }}</td><td>{{ when(r.createdAtUtc) }}</td><td>{{ when(r.startedAtUtc) }}</td>
                  <td>{{ r.durationMs !== null ? (r.durationMs / 1000).toFixed(1) + ' s' : '—' }}</td>
                  <td class="problem-text">{{ r.error ?? '' }}</td>
                </tr>
              } @empty { <tr><td colspan="9" class="empty">The AI queue is empty.</td></tr> }
            </tbody>
          </table>
        </div>
      }
    } @else {
      @if (publishing(); as q) {
        <div class="stat-cards small">
          <div class="stat"><span class="value">{{ q.summary.scheduled }}</span><span class="label">Scheduled</span></div>
          <div class="stat"><span class="value">{{ q.summary.processing }}</span><span class="label">Processing</span></div>
          <div class="stat"><span class="value">{{ q.summary.waitingMedia }}</span><span class="label">Waiting Media</span></div>
          <div class="stat"><span class="value">{{ q.summary.retryPending }}</span><span class="label">Retry Pending</span></div>
          <div class="stat"><span class="value">{{ q.summary.actionRequired }}</span><span class="label">Action Required</span></div>
          <div class="stat" [class.danger]="q.summary.reconciliationRequired > 0"><span class="value">{{ q.summary.reconciliationRequired }}</span><span class="label">Reconciliation Required</span></div>
          <div class="stat" [class.danger]="q.summary.failedLast24Hours > 0"><span class="value">{{ q.summary.failedLast24Hours }}</span><span class="label">Failed (24 h)</span></div>
        </div>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>Job ID</th><th>Workspace</th><th>Platform</th><th>Post</th><th>Scheduled Time</th><th>Status</th><th>Attempts</th><th>Worker</th><th>Last Error</th></tr></thead>
            <tbody>
              @for (j of q.items; track j.id) {
                <tr>
                  <td><a [routerLink]="['/internal/publications', j.id]"><code>{{ j.id.slice(0, 8) }}</code></a></td>
                  <td>{{ j.workspaceName }}</td><td>{{ j.platform }}</td><td>{{ j.postId.slice(0, 8) }}</td>
                  <td>{{ when(j.publishAtUtc) }}</td>
                  <td><span [class]="'badge delivery ' + tone(j.state)">{{ words(j.state) }}</span></td>
                  <td>{{ j.attempts }}/{{ j.maxAttempts }}</td><td>{{ j.worker ?? '—' }}</td>
                  <td class="problem-text">{{ j.lastErrorCode ?? '' }}</td>
                </tr>
              } @empty { <tr><td colspan="9" class="empty">The publishing queue is empty.</td></tr> }
            </tbody>
          </table>
        </div>
      }
    }
  `,
})
export class QueuesPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly tab = signal<'ai' | 'publishing'>(inject(ActivatedRoute).snapshot.queryParamMap.get('tab') === 'publishing' ? 'publishing' : 'ai');
  protected readonly ai = signal<AiQueue | null>(null);
  protected readonly publishing = signal<PublishingQueue | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly tone = tone;
  protected readonly when = when;
  protected readonly ago = ago;
  protected readonly words = words;

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    try {
      const [ai, publishing] = await Promise.all([firstValueFrom(this.api.aiQueue()), firstValueFrom(this.api.publishingQueue())]);
      this.ai.set(ai);
      this.publishing.set(publishing);
      this.problem.set(null);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }
}
