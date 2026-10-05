import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { HEALTH_LABELS, InternalApiService, Overview, ago, tone, words } from '../internal-api.service';

/** Compact platform overview. Details (workers, queues, logs) are one click deeper. */
@Component({
  selector: 'app-internal-overview-page',
  imports: [RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Overview</h1>
        @if (o(); as d) { <p class="muted"><span class="badge" [class.danger]="d.environment === 'PRODUCTION'">{{ d.environment }}</span> Updated {{ ago(d.generatedAtUtc) }}</p> }
      </div>
      <button type="button" (click)="load()" [disabled]="loading()">{{ loading() ? 'Refreshing…' : 'Refresh' }}</button>
    </div>
    <app-problem-banner [problem]="problem()" (reload)="load()" />

    @if (o(); as d) {
      <div class="stat-cards">
        <a class="stat" routerLink="/internal/workspaces"><span class="value">{{ d.summary.workspaces }}</span><span class="label">Workspaces</span></a>
        <a class="stat" routerLink="/internal/users"><span class="value">{{ d.summary.activeUsers }}</span><span class="label">Users</span></a>
        <a class="stat" routerLink="/internal/queues"><span class="value">{{ d.summary.aiRequestsToday }}</span><span class="label">AI Requests Today</span></a>
        <a class="stat" routerLink="/internal/publications" [queryParams]="{ status: 'PUBLISHED' }"><span class="value">{{ d.summary.publishedToday }}</span><span class="label">Published Today</span></a>
        <a class="stat" [class.danger]="d.summary.failedPublicationsToday > 0" routerLink="/internal/publications" [queryParams]="{ status: 'FAILED' }"><span class="value">{{ d.summary.failedPublicationsToday }}</span><span class="label">Failed Today</span></a>
        <a class="stat" [class.danger]="d.summary.reconciliationRequired > 0" routerLink="/internal/reconciliation"><span class="value">{{ d.summary.reconciliationRequired }}</span><span class="label">Needs Reconciliation</span></a>
      </div>

      <div class="admin-grid">
        <section class="card">
          <h2>Requires Attention</h2>
          @for (a of d.attention; track $index) {
            <div class="alert-row" [class]="'alert-row ' + a.severity.toLowerCase()">
              <div class="grow"><strong>{{ a.title }}</strong>@if (a.detail) { <span class="hint"> · {{ a.detail }}</span> }</div>
              <a [routerLink]="path(a.actionRoute)" [queryParams]="query(a.actionRoute)">{{ a.actionLabel }}</a>
            </div>
          } @empty {
            <p class="all-ok">All systems operational</p>
          }
        </section>

        <section class="card">
          <div class="card-head"><h2>System Status</h2><a routerLink="/internal/system-health">Details</a></div>
          <ul class="kv">
            @for (s of d.status; track s.name) {
              <li [title]="s.detail ?? ''"><span>{{ s.name }}</span><span [class]="'badge delivery ' + tone(s.status)">{{ labels[s.status] }}</span></li>
            }
          </ul>
        </section>
      </div>

      <div class="admin-grid">
        <section class="card">
          <div class="card-head"><h2>Operations</h2><a routerLink="/internal/queues">Queues</a></div>
          <div class="ops">
            <div>
              <h3>AI</h3>
              <ul class="kv">
                <li><span>Pending</span><strong>{{ d.operations.aiPending }}</strong></li>
                <li><span>Processing</span><strong>{{ d.operations.aiProcessing }}</strong></li>
                <li><span>Failed today</span><strong [class.text-danger]="d.operations.aiFailedToday > 0">{{ d.operations.aiFailedToday }}</strong></li>
              </ul>
            </div>
            <div>
              <h3>Publishing</h3>
              <ul class="kv">
                <li><span>Scheduled</span><strong>{{ d.operations.publishingScheduled }}</strong></li>
                <li><span>Processing</span><strong>{{ d.operations.publishingProcessing }}</strong></li>
                <li><span>Failed today</span><strong [class.text-danger]="d.operations.publishingFailedToday > 0">{{ d.operations.publishingFailedToday }}</strong></li>
                <li><span>Needs attention</span><strong [class.text-danger]="d.operations.publishingNeedsAttention > 0">{{ d.operations.publishingNeedsAttention }}</strong></li>
              </ul>
            </div>
          </div>
        </section>

        <section class="card">
          <div class="card-head"><h2>Recent Admin Activity</h2><a routerLink="/internal/logs">Logs</a></div>
          <ul class="kv">
            @for (a of d.recentAdminActivity; track a.id) {
              <li><span>{{ words(a.action) }} · {{ a.target }}</span><span class="hint">{{ a.actor }} · {{ ago(a.atUtc) }}</span></li>
            } @empty { <li class="hint">No admin activity yet.</li> }
          </ul>
        </section>
      </div>
    } @else if (loading()) {
      <p class="muted">Loading…</p>
    }
  `,
})
export class OverviewPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly o = signal<Overview | null>(null);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly labels = HEALTH_LABELS;
  protected readonly tone = tone;
  protected readonly ago = ago;
  protected readonly words = words;

  ngOnInit(): void {
    void this.load();
  }

  protected path(route: string): string {
    return route.split('?')[0];
  }

  protected query(route: string): Record<string, string> {
    return Object.fromEntries(new URLSearchParams(route.split('?')[1] ?? ''));
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.o.set(await firstValueFrom(this.api.overview()));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
