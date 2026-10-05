import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { InternalApiService, Publication, PublicationDetail, ReconciliationCheck, WorkspaceListItem, tone, when, words } from '../internal-api.service';

const STATES = [
  { value: 'PUBLISHED', label: 'Published' },
  { value: 'FAILED', label: 'Failed' },
  { value: 'PROCESSING', label: 'Processing' },
  { value: 'ACTION_REQUIRED', label: 'Action Required' },
  { value: 'RECONCILIATION_REQUIRED', label: 'Reconciliation Required' },
];

/** All publications (or, in reconciliation mode, only unresolved uncertain outcomes). */
@Component({
  selector: 'app-internal-publications-page',
  imports: [FormsModule, RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ reconciliation() ? 'Reconciliation' : 'Publications' }}</h1>
        <p class="muted">{{ reconciliation()
          ? 'Publications whose outcome is unknown: the request may or may not have created the post. Nothing here is sent again automatically.'
          : 'Every publication intent across workspaces.' }}</p>
      </div>
      <button type="button" (click)="load()">Refresh</button>
    </div>
    @if (!reconciliation()) {
      <div class="filter-bar">
        <select [(ngModel)]="workspaceId" (ngModelChange)="load()" aria-label="Workspace">
          <option value="">All workspaces</option>
          @for (w of workspaces(); track w.id) { <option [value]="w.id">{{ w.name }}</option> }
        </select>
        <select [(ngModel)]="platform" (ngModelChange)="load()" aria-label="Platform">
          <option value="">All platforms</option><option>LinkedIn</option><option>Facebook</option><option>Instagram</option>
        </select>
        <label class="date">From <input type="date" [(ngModel)]="from" (ngModelChange)="load()" /></label>
        <label class="date">To <input type="date" [(ngModel)]="to" (ngModelChange)="load()" /></label>
      </div>
      <div class="chips">
        @for (s of states; track s.value) {
          <button type="button" class="chip" [class.on]="selected().includes(s.value)" (click)="toggle(s.value)">{{ s.label }}</button>
        }
      </div>
    }
    <app-problem-banner [problem]="problem()" />
    <div class="table-wrap">
      <table class="table">
        <thead>
          @if (reconciliation()) {
            <tr><th>Workspace</th><th>Platform</th><th>Publication</th><th>Reason</th><th>Last Attempt</th><th>Provider Result</th><th>Created</th></tr>
          } @else {
            <tr><th>Publication ID</th><th>Workspace</th><th>Campaign / Post</th><th>Platform</th><th>Destination</th><th>Scheduled At</th><th>Status</th><th>Attempts</th><th>Last Result</th></tr>
          }
        </thead>
        <tbody>
          @for (p of rows(); track p.id) {
            @if (reconciliation()) {
              <tr>
                <td>{{ p.workspaceName }}</td><td>{{ p.platform }}</td>
                <td><a [routerLink]="['/internal/reconciliation', p.id]"><code>{{ p.id.slice(0, 8) }}</code></a> · {{ p.campaignName }}</td>
                <td>{{ words(p.lastErrorCode) }}</td><td>{{ p.attempts }}/{{ p.maxAttempts }}</td>
                <td class="problem-text">{{ p.lastErrorMessage ?? '—' }}</td><td>{{ when(p.createdAtUtc) }}</td>
              </tr>
            } @else {
              <tr>
                <td><a [routerLink]="['/internal/publications', p.id]"><code>{{ p.id.slice(0, 8) }}</code></a></td>
                <td>{{ p.workspaceName }}</td><td>{{ p.campaignName }} · v{{ p.versionNumber }}</td><td>{{ p.platform }}</td>
                <td>{{ p.destination }}@if (p.executionMode === 'SIMULATED') { <span class="badge mode simulated">Demo</span> }</td>
                <td>{{ when(p.publishAtUtc) }}</td>
                <td><span [class]="'badge delivery ' + tone(p.state)">{{ words(p.state) }}</span></td>
                <td>{{ p.attempts }}/{{ p.maxAttempts }}</td>
                <td class="problem-text">{{ p.lastErrorCode ?? '—' }}</td>
              </tr>
            }
          } @empty {
            <tr><td [attr.colspan]="reconciliation() ? 7 : 9" class="empty">{{ loading() ? 'Loading…' : reconciliation() ? 'Nothing needs reconciliation.' : 'No publications match.' }}</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class PublicationsPage implements OnInit {
  private readonly api = inject(InternalApiService);
  readonly mode = input<'all' | 'reconciliation'>('all');

  protected readonly states = STATES;
  protected readonly rows = signal<Publication[]>([]);
  protected readonly workspaces = signal<WorkspaceListItem[]>([]);
  protected readonly selected = signal<string[]>(inject(ActivatedRoute).snapshot.queryParamMap.getAll('status'));
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly tone = tone;
  protected readonly when = when;
  protected readonly words = words;
  protected workspaceId = '';
  protected platform = '';
  protected from = '';
  protected to = '';

  protected reconciliation(): boolean {
    return this.mode() === 'reconciliation';
  }

  ngOnInit(): void {
    void this.load();
    if (!this.reconciliation()) void firstValueFrom(this.api.workspaces({})).then((w) => this.workspaces.set(w)).catch(() => undefined);
  }

  protected toggle(state: string): void {
    this.selected.update((all) => (all.includes(state) ? all.filter((s) => s !== state) : [...all, state]));
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.rows.set(await firstValueFrom(this.reconciliation()
        ? this.api.reconciliation()
        : this.api.publications({ workspaceId: this.workspaceId, platform: this.platform, from: this.from, to: this.to ? `${this.to}T23:59:59` : '', status: this.selected() })));
      this.problem.set(null);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}

/** One publication with its timeline. In reconciliation mode: evidence check and manual resolution (always audited). */
@Component({
  selector: 'app-internal-publication-detail-page',
  imports: [FormsModule, RouterLink, ProblemBannerComponent],
  template: `
    <a class="back" [routerLink]="reconciliation() ? '/internal/reconciliation' : '/internal/publications'">← {{ reconciliation() ? 'Reconciliation' : 'Publications' }}</a>
    <app-problem-banner [problem]="problem()" />
    @if (notice(); as n) { <p class="banner success" role="status">{{ n }}</p> }
    @if (d(); as detail) {
      @let p = detail.publication;
      <div class="page-header">
        <div>
          <h1>{{ p.platform }} publication</h1>
          <span [class]="'badge delivery ' + tone(p.state)">{{ words(p.state) }}</span>
          @if (p.executionMode === 'SIMULATED') { <span class="badge mode simulated">Demo (never sent)</span> }
        </div>
      </div>

      @if (reconciliation() && p.state === 'RECONCILIATION_REQUIRED') {
        <section class="card reconcile">
          <h2>Resolve</h2>
          <p class="hint">Check the destination first. Confirming records the outcome; nothing is re-sent. Every action is audited.</p>
          <div class="actions">
            <button type="button" (click)="check()" [disabled]="busy()">{{ busy() === 'check' ? 'Checking…' : 'Check Provider Status' }}</button>
          </div>
          @if (checkResult(); as c) {
            <p [class]="'banner ' + (c.verdict === 'LIKELY_PUBLISHED' ? 'success' : c.verdict === 'NOT_SENT' ? 'info' : 'error')">{{ c.summary }}</p>
          }
          <div class="resolve-grid">
            <div class="form-grid">
              <label>External post ID <span class="hint">optional</span><input [(ngModel)]="externalId" [placeholder]="detail.externalPostId ?? ''" /></label>
              <label>External post URL <span class="hint">optional</span><input type="url" [(ngModel)]="externalUrl" placeholder="https://" /></label>
              <button type="button" class="primary" (click)="confirmPublished()" [disabled]="busy() || p.executionMode === 'SIMULATED'">Confirm Published</button>
            </div>
            <div class="form-grid">
              <label>Reason * <textarea rows="3" [(ngModel)]="reason" placeholder="e.g. Checked the LinkedIn page: the post does not exist."></textarea></label>
              <button type="button" class="danger" (click)="confirmFailed()" [disabled]="busy() || !reason.trim()">Confirm Failed</button>
            </div>
          </div>
        </section>
      }

      <div class="admin-grid wide-left">
        <section class="card">
          <h2>Timeline</h2>
          <ol class="timeline">
            @for (e of detail.timeline; track $index) {
              <li [class]="e.tone"><span><strong>{{ e.title }}</strong>@if (e.detail) { <span class="hint"> · {{ e.detail }}</span> }</span><span class="hint">{{ when(e.atUtc) }}</span></li>
            }
          </ol>
        </section>
        <section class="card">
          <h2>Details</h2>
          <ul class="kv">
            <li><span>Publication ID</span><code>{{ p.id }}</code></li>
            <li><span>Workspace</span><a [routerLink]="['/internal/workspaces', p.organizationId]">{{ p.workspaceName }}</a></li>
            <li><span>Post</span><span>{{ p.campaignName }} · v{{ p.versionNumber }}</span></li>
            <li><span>Destination</span><span>{{ p.destination }}</span></li>
            <li><span>Provider external ID</span><span>{{ detail.externalPostId ?? '—' }}</span></li>
            @if (detail.externalPostUrl) { <li><span>Post link</span><a [href]="detail.externalPostUrl" target="_blank" rel="noopener">Open</a></li> }
            <li><span>Attempts</span><span>{{ p.attempts }}/{{ p.maxAttempts }}</span></li>
            <li><span>Payload hash</span><code>{{ detail.payloadHash.slice(0, 16) }}…</code></li>
            <li><span>Normalized error</span><span>{{ p.lastErrorCode ?? '—' }}</span></li>
            <li><span>Created</span><span>{{ when(p.createdAtUtc) }}</span></li>
            <li><span>Scheduled</span><span>{{ when(p.publishAtUtc) }}</span></li>
            <li><span>Published / completed</span><span>{{ when(detail.completedAtUtc) }}</span></li>
            <li><span>Correlation ID</span><code>{{ p.correlationId ?? '—' }}</code></li>
          </ul>
          @if (p.lastErrorMessage) { <p class="hint">{{ p.lastErrorMessage }}</p> }
        </section>
      </div>

      <section class="card">
        <h2>Attempts</h2>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>#</th><th>Step</th><th>Worker</th><th>Started</th><th>Finished</th><th>Result</th><th>Error</th><th>Provider Request</th></tr></thead>
            <tbody>
              @for (a of detail.attempts; track a.id) {
                <tr>
                  <td>{{ a.attemptNumber }}</td><td>{{ words(a.step) }}</td><td><code>{{ a.workerId }}</code></td>
                  <td>{{ when(a.startedAtUtc) }}</td><td>{{ when(a.finishedAtUtc) }}</td>
                  <td>@if (a.resultState) { <span [class]="'badge delivery ' + tone(a.resultState)">{{ words(a.resultState) }}</span> }</td>
                  <td class="problem-text">{{ a.errorCategory ? a.errorCategory + ' · ' : '' }}{{ a.errorCode ?? '' }}</td>
                  <td>{{ a.providerRequestId ?? '—' }}</td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty">No attempts yet.</td></tr> }
            </tbody>
          </table>
        </div>
      </section>
    }
  `,
})
export class PublicationDetailPage implements OnInit {
  private readonly api = inject(InternalApiService);
  readonly id = input.required<string>();
  readonly mode = input<'all' | 'reconciliation'>('all');

  protected readonly d = signal<PublicationDetail | null>(null);
  protected readonly checkResult = signal<ReconciliationCheck | null>(null);
  protected readonly busy = signal<'check' | 'resolve' | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly tone = tone;
  protected readonly when = when;
  protected readonly words = words;
  protected externalId = '';
  protected externalUrl = '';
  protected reason = '';

  protected reconciliation(): boolean {
    return this.mode() === 'reconciliation';
  }

  async ngOnInit(): Promise<void> {
    try {
      this.d.set(await firstValueFrom(this.api.publication(this.id())));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected async check(): Promise<void> {
    this.busy.set('check');
    this.problem.set(null);
    try {
      this.checkResult.set(await firstValueFrom(this.api.checkReconciliation(this.id())));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }

  protected confirmPublished(): Promise<void> {
    if (!confirm('Confirm this post IS live on the provider? It will be recorded as Published.')) return Promise.resolve();
    return this.resolve(() => firstValueFrom(this.api.confirmPublished(this.id(), this.externalId.trim() || null, this.externalUrl.trim() || null)), 'Recorded as published.');
  }

  protected confirmFailed(): Promise<void> {
    if (!confirm('Confirm this post was NOT published? It will be recorded as Failed and the customer can schedule it again.')) return Promise.resolve();
    return this.resolve(() => firstValueFrom(this.api.confirmFailed(this.id(), this.reason.trim())), 'Recorded as failed.');
  }

  private async resolve(action: () => Promise<PublicationDetail>, message: string): Promise<void> {
    this.busy.set('resolve');
    this.problem.set(null);
    try {
      this.d.set(await action());
      this.notice.set(message);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }
}
