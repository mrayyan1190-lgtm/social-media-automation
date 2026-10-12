import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { ActivityLogItem, AiLogItem, AuditLogItem, InternalApiService, PublishingLogItem, SignInLogItem, SystemLogItem, tone, when, words } from '../internal-api.service';
import { ThemeSelectComponent } from '../../../shared/components/theme-select.component';

type Tab = 'all' | 'audit' | 'signins' | 'ai' | 'publishing' | 'system';

/**
 * All (every log in one timeline), audit, sign-in, AI, publishing and system logs. Failed API requests are in the System log. Secrets are never logged; AI logs show no prompts or model output;
 * every row carries its correlation id so one operation can be traced across tabs.
 */
@Component({
  selector: 'app-internal-logs-page',
  imports: [ThemeSelectComponent, FormsModule, RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>Logs</h1><p class="muted">Newest first. Click a correlation id to follow one operation across every log.</p></div></div>
    <nav class="tabs">
      @for (t of tabs; track t.key) { <button type="button" [class.active]="tab() === t.key" (click)="select(t.key)">{{ t.label }}</button> }
    </nav>
    <div class="filter-bar">
      <label class="search"><input type="search" [placeholder]="tab() === 'signins' ? 'Search email' : 'Search'" [(ngModel)]="search" (keyup.enter)="load()" aria-label="Search" /></label>
      <label class="date">From <input type="date" [(ngModel)]="from" (ngModelChange)="load()" /></label>
      <label class="date">To <input type="date" [(ngModel)]="to" (ngModelChange)="load()" /></label>
      <input class="corr" placeholder="Correlation ID" [(ngModel)]="correlationId" (keyup.enter)="load()" aria-label="Correlation ID" />
      @if (tab() === 'all') {
        <app-theme-select [(ngModel)]="outcome" (ngModelChange)="load()" label="Outcome">
          <option value="">Everything</option><option value="errors">Errors &amp; warnings</option><option value="success">Successes</option>
        </app-theme-select>
      }
      @if (tab() === 'signins') {
        <app-theme-select [(ngModel)]="result" (ngModelChange)="load()" label="Result"><option value="">All results</option><option value="success">Success</option><option value="failed">Failed</option></app-theme-select>
        <input placeholder="IP address" [(ngModel)]="ip" (keyup.enter)="load()" aria-label="IP address" />
      }
      @if (tab() === 'system') {
        <app-theme-select [(ngModel)]="level" (ngModelChange)="load()" label="Severity">
          <option value="">All severities</option><option value="INFO">Info</option><option value="WARNING">Warning</option><option value="ERROR">Error</option><option value="CRITICAL">Critical</option>
        </app-theme-select>
        <app-theme-select [(ngModel)]="service" (ngModelChange)="load()" label="Service">
          <option value="">All services</option>
          @for (s of services; track s) { <option [value]="s">{{ s }}</option> }
        </app-theme-select>
      }
      <button type="button" (click)="load()">Apply</button>
      @if (search || from || to || correlationId || result || ip || level || service || outcome) { <button type="button" class="link" (click)="clear()">Clear</button> }
    </div>
    <app-problem-banner [problem]="problem()" />

    <div class="table-wrap">
      <table class="table compact">
        @switch (tab()) {
          @case ('all') {
            <thead><tr><th>Timestamp</th><th>Source</th><th>Result</th><th>Actor</th><th>Workspace</th><th>Event</th><th>Details</th><th>Correlation ID</th></tr></thead>
            <tbody>
              @for (r of all(); track r.source + r.id) {
                <tr>
                  <td>{{ when(r.atUtc) }}</td><td>{{ r.source }}</td>
                  <td><span [class]="'badge delivery ' + tone(r.outcome)">{{ words(r.outcome) }}</span></td>
                  <td>{{ r.actor ?? '—' }}</td><td>{{ r.workspace ?? '—' }}</td><td class="ev">{{ words(r.event) }}</td>
                  <td class="details" [class.problem-text]="r.outcome === 'FAILED'">{{ r.message }}</td>
                  <td><button type="button" class="link" (click)="trace(r.correlationId)">{{ r.correlationId ?? '' }}</button></td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty">{{ empty() }}</td></tr> }
            </tbody>
          }
          @case ('audit') {
            <thead><tr><th>Timestamp</th><th>Actor</th><th>Action</th><th>Target Type</th><th>Target</th><th>Workspace</th><th>Result</th><th>Correlation ID</th></tr></thead>
            <tbody>
              @for (r of audit(); track r.id) {
                <tr>
                  <td>{{ when(r.atUtc) }}</td><td>{{ r.actor ?? 'System' }}</td>
                  <td>{{ words(r.action) }} @if (r.scope === 'PLATFORM') { <span class="badge">Operator</span> }</td>
                  <td>{{ r.targetType }}</td><td class="ellipsis">{{ r.target }}</td><td>{{ r.workspace ?? '—' }}</td>
                  <td><span [class]="'badge delivery ' + tone(r.result)">{{ words(r.result) }}</span></td>
                  <td><button type="button" class="link" (click)="trace(r.correlationId)">{{ r.correlationId ?? '' }}</button></td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty">{{ empty() }}</td></tr> }
            </tbody>
          }
          @case ('signins') {
            <thead><tr><th>Timestamp</th><th>User</th><th>Email</th><th>Result</th><th>IP Address</th><th>Client / Browser</th><th>Failure Reason</th><th>Correlation ID</th></tr></thead>
            <tbody>
              @for (r of signIns(); track r.id) {
                <tr>
                  <td>{{ when(r.atUtc) }}</td>
                  <td>@if (r.userId) { <a [routerLink]="['/internal/users', r.userId]">{{ r.userName || '—' }}</a> } @else { — }</td>
                  <td>{{ r.email }}</td>
                  <td><span [class]="'badge delivery ' + (r.succeeded ? 'success' : 'danger')">{{ eventLabel(r.eventType) }}</span> <span class="hint">{{ r.area === 'INTERNAL' ? 'Internal' : 'Customer' }}</span></td>
                  <td>{{ r.ipAddress ?? '—' }}</td><td class="ellipsis" [title]="r.userAgent ?? ''">{{ browser(r.userAgent) }}</td>
                  <td>{{ r.succeeded ? '—' : eventLabel(r.eventType) }}</td>
                  <td><button type="button" class="link" (click)="trace(r.correlationId)">{{ r.correlationId ?? '' }}</button></td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty">{{ empty() }}</td></tr> }
            </tbody>
          }
          @case ('ai') {
            <thead><tr><th>Request ID</th><th>Workspace</th><th>Operation</th><th>Model</th><th>Status</th><th>Started</th><th>Duration</th><th>Input Tokens</th><th>Output Tokens</th><th>Attempts</th><th>Error</th><th>Correlation ID</th></tr></thead>
            <tbody>
              @for (r of ai(); track r.id) {
                <tr>
                  <td><code>{{ r.requestId.slice(0, 8) }}</code></td><td>{{ r.workspace }}</td><td>{{ r.operation }}</td><td>{{ r.model }}</td>
                  <td><span [class]="'badge delivery ' + (r.succeeded ? 'success' : 'danger')">{{ r.succeeded ? 'Succeeded' : 'Failed' }}</span></td>
                  <td>{{ when(r.startedAtUtc) }}</td><td>{{ (r.durationMs / 1000).toFixed(1) }} s</td>
                  <td>{{ r.inputTokens ?? '—' }}</td><td>{{ r.outputTokens ?? '—' }}</td><td>{{ r.attempts }}</td>
                  <td class="problem-text">{{ r.error ?? '' }}</td>
                  <td><button type="button" class="link" (click)="trace(r.correlationId)">{{ r.correlationId ?? '' }}</button></td>
                </tr>
              } @empty { <tr><td colspan="12" class="empty">{{ empty() }}</td></tr> }
            </tbody>
          }
          @case ('publishing') {
            <thead><tr><th>Timestamp</th><th>Publication ID</th><th>Workspace</th><th>Platform</th><th>Operation</th><th>Status</th><th>Provider Request ID</th><th>Duration</th><th>Attempt</th><th>Error Category</th><th>Correlation ID</th></tr></thead>
            <tbody>
              @for (r of publishing(); track r.id + r.operation) {
                <tr>
                  <td>{{ when(r.atUtc) }}</td>
                  <td><a [routerLink]="['/internal/publications', r.publicationId]"><code>{{ r.publicationId.slice(0, 8) }}</code></a></td>
                  <td>{{ r.workspace }}</td><td>{{ r.platform }}</td><td>{{ r.operation }}</td>
                  <td><span [class]="'badge delivery ' + tone(r.status)">{{ words(r.status) }}</span></td>
                  <td>{{ r.providerRequestId ?? '—' }}</td><td>{{ r.durationMs !== null ? r.durationMs + ' ms' : '—' }}</td><td>{{ r.attempt ?? '—' }}</td>
                  <td class="problem-text">{{ r.errorCategory ? r.errorCategory + (r.error ? ' · ' + r.error : '') : '—' }}</td>
                  <td><button type="button" class="link" (click)="trace(r.correlationId)">{{ r.correlationId ?? '' }}</button></td>
                </tr>
              } @empty { <tr><td colspan="11" class="empty">{{ empty() }}</td></tr> }
            </tbody>
          }
          @default {
            <thead><tr><th>Timestamp</th><th>Level</th><th>Service</th><th>Event</th><th>Message</th><th>Correlation ID</th></tr></thead>
            <tbody>
              @for (r of system(); track r.id) {
                <tr>
                  <td>{{ when(r.atUtc) }}</td><td><span [class]="'badge delivery ' + tone(r.level)">{{ words(r.level) }}</span></td>
                  <td>{{ r.service }}</td><td>{{ words(r.event) }}</td><td class="problem-text">{{ r.message }}</td>
                  <td><button type="button" class="link" (click)="trace(r.correlationId)">{{ r.correlationId ?? '' }}</button></td>
                </tr>
              } @empty { <tr><td colspan="6" class="empty">{{ empty() }}</td></tr> }
            </tbody>
          }
        }
      </table>
    </div>
  `,
})
export class LogsPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly tabs: { key: Tab; label: string }[] = [
    { key: 'all', label: 'All' }, { key: 'audit', label: 'Audit' }, { key: 'signins', label: 'Sign-ins' }, { key: 'ai', label: 'AI' },
    { key: 'publishing', label: 'Publishing' }, { key: 'system', label: 'System' },
  ];
  protected readonly services = ['API', 'AI Worker', 'Publishing Worker', 'Database', 'Storage', 'OpenAI', 'LinkedIn', 'Meta'];
  protected readonly tab = signal<Tab>('all');
  protected readonly all = signal<ActivityLogItem[]>([]);
  protected readonly audit = signal<AuditLogItem[]>([]);
  protected readonly signIns = signal<SignInLogItem[]>([]);
  protected readonly ai = signal<AiLogItem[]>([]);
  protected readonly publishing = signal<PublishingLogItem[]>([]);
  protected readonly system = signal<SystemLogItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly tone = tone;
  protected readonly when = when;
  protected readonly words = words;
  protected search = '';
  protected from = '';
  protected to = '';
  protected correlationId = '';
  protected result = '';
  protected ip = '';
  protected level = '';
  protected service = '';
  protected outcome = '';

  ngOnInit(): void {
    void this.load();
  }

  protected select(tab: Tab): void {
    this.tab.set(tab);
    void this.load();
  }

  protected trace(correlationId: string | null): void {
    if (!correlationId) return;
    this.correlationId = correlationId;
    void this.load();
  }

  protected clear(): void {
    this.search = this.from = this.to = this.correlationId = this.result = this.ip = this.level = this.service = this.outcome = '';
    void this.load();
  }

  protected empty(): string {
    return this.loading() ? 'Loading…' : 'No entries.';
  }

  protected eventLabel(type: string): string {
    return ({
      LOGIN_SUCCEEDED: 'Successful Login', INVALID_CREDENTIALS: 'Invalid Credentials', ACCOUNT_DISABLED: 'Disabled Account',
      INTERNAL_ACCESS_FORBIDDEN: 'Forbidden Internal Access', LOGOUT: 'Logout',
    } as Record<string, string>)[type] ?? words(type);
  }

  /** A readable browser name from the user agent (the full string is in the tooltip). */
  protected browser(ua: string | null): string {
    if (!ua) return '—';
    const match = /(Edg|Chrome|Firefox|Safari)\/[\d.]+/.exec(ua);
    return match ? match[0].replace('Edg', 'Edge').split('.')[0] : ua.slice(0, 40);
  }

  async load(): Promise<void> {
    const q: Record<string, string> = { search: this.search, from: this.from, to: this.to ? `${this.to}T23:59:59` : '', correlationId: this.correlationId };
    this.loading.set(true);
    this.problem.set(null);
    try {
      switch (this.tab()) {
        case 'all': this.all.set(await firstValueFrom(this.api.allLog({ ...q, outcome: this.outcome }))); break;
        case 'audit': this.audit.set(await firstValueFrom(this.api.auditLog(q))); break;
        case 'signins': this.signIns.set(await firstValueFrom(this.api.signInLog({ ...q, result: this.result, ip: this.ip }))); break;
        case 'ai': this.ai.set(await firstValueFrom(this.api.aiLog(q))); break;
        case 'publishing': this.publishing.set(await firstValueFrom(this.api.publishingLog(q))); break;
        default: this.system.set(await firstValueFrom(this.api.systemLog({ ...q, level: this.level, service: this.service })));
      }
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
