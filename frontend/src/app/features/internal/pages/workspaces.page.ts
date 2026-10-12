import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { AuditLogItem, InternalApiService, WorkspaceDetail, WorkspaceListItem, ago, tone, when, words } from '../internal-api.service';
import { ThemeSelectComponent } from '../../../shared/components/theme-select.component';

@Component({
  selector: 'app-internal-workspaces-page',
  imports: [ThemeSelectComponent, FormsModule, RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>Workspaces</h1><p class="muted">Customer tenants. Operators can suspend a workspace, never edit its content.</p></div></div>
    <div class="filter-bar">
      <label class="search"><input type="search" placeholder="Search workspaces" [(ngModel)]="search" (ngModelChange)="load()" aria-label="Search" /></label>
      <app-theme-select [(ngModel)]="status" (ngModelChange)="load()" label="Status">
        <option value="">All statuses</option><option value="Active">Active</option><option value="Suspended">Suspended</option>
      </app-theme-select>
      <label class="date">Created from <input type="date" [(ngModel)]="createdFrom" (ngModelChange)="load()" /></label>
      <span class="count-note">{{ rows().length }} {{ rows().length === 1 ? 'workspace' : 'workspaces' }}</span>
    </div>
    <app-problem-banner [problem]="problem()" />
    <div class="table-wrap">
      <table class="table">
        <thead><tr><th>Workspace</th><th>Primary Admin</th><th>Members</th><th>Social Accounts</th><th>Status</th><th>Created</th><th>Last Activity</th></tr></thead>
        <tbody>
          @for (w of rows(); track w.id) {
            <tr>
              <td><a [routerLink]="['/internal/workspaces', w.id]">{{ w.name }}</a></td>
              <td>{{ w.primaryAdminName || '—' }}<br /><span class="hint">{{ w.primaryAdminEmail }}</span></td>
              <td>{{ w.members }}</td><td>{{ w.socialAccounts }}</td>
              <td><span [class]="'badge delivery ' + tone(w.status)">{{ w.status }}</span></td>
              <td>{{ when(w.createdAtUtc) }}</td><td>{{ ago(w.lastActivityAtUtc) }}</td>
            </tr>
          } @empty { <tr><td colspan="8" class="empty">{{ loading() ? 'Loading…' : 'No workspaces match.' }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
})
export class WorkspacesPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly rows = signal<WorkspaceListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly tone = tone;
  protected readonly when = when;
  protected readonly ago = ago;
  protected search = '';
  protected status = '';
  protected createdFrom = '';

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.rows.set(await firstValueFrom(this.api.workspaces({ search: this.search, status: this.status, createdFrom: this.createdFrom })));
      this.problem.set(null);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}

@Component({
  selector: 'app-internal-workspace-detail-page',
  imports: [RouterLink, ProblemBannerComponent],
  template: `
    <a class="back" routerLink="/internal/workspaces">← Workspaces</a>
    <app-problem-banner [problem]="problem()" />
    @if (w(); as d) {
      <div class="page-header">
        <div><h1>{{ d.name }}</h1><span [class]="'badge delivery ' + tone(d.status)">{{ d.status }}</span></div>
        <div class="actions">
          <button type="button" (click)="toggleAudit()">{{ audit() ? 'Hide' : 'View' }} Audit History</button>
          @if (d.status === 'Active') {
            <button type="button" class="danger" (click)="suspend()" [disabled]="busy()">Suspend Workspace</button>
          } @else {
            <button type="button" class="primary" (click)="reactivate()" [disabled]="busy()">Reactivate Workspace</button>
          }
        </div>
      </div>
      <div class="admin-grid three">
        <section class="card">
          <h2>General</h2>
          <ul class="kv">
            <li><span>Workspace ID</span><code>{{ d.id }}</code></li>
            <li><span>Time Zone</span><span>{{ d.timeZoneId }}</span></li>
            <li><span>Created</span><span>{{ when(d.createdAtUtc) }}</span></li>
            <li><span>Status</span><span>{{ d.status }}</span></li>
          </ul>
        </section>
        <section class="card">
          <h2>Usage</h2>
          <ul class="kv">
            <li><span>Members</span><strong>{{ d.usage.members }}</strong></li>
            <li><span>Posts</span><strong>{{ d.usage.posts }}</strong></li>
            <li><span>Connected social accounts</span><strong>{{ d.usage.connectedSocialAccounts }}</strong></li>
            <li><span>AI requests</span><strong>{{ d.usage.aiRequests }}</strong></li>
            <li><span>Published posts</span><strong>{{ d.usage.publishedPosts }}</strong></li>
          </ul>
        </section>
        <section class="card">
          <h2>Connections</h2>
          <ul class="kv">
            @for (c of d.connections; track $index) {
              <li><span>{{ c.platform }} · {{ c.displayName }}</span><span [class]="'badge delivery ' + (c.status === 'Connected' ? 'success' : c.status === 'Demo Mode' ? 'simulated' : 'danger')">{{ c.status }}</span></li>
            } @empty { <li class="hint">No social accounts.</li> }
          </ul>
        </section>
      </div>
      <section class="card">
        <h2>Members</h2>
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th></tr></thead>
            <tbody>
              @for (m of d.members; track m.userId) {
                <tr><td><a [routerLink]="['/internal/users', m.userId]">{{ m.name || '—' }}</a></td><td>{{ m.email }}</td><td>{{ m.role }}</td>
                  <td><span [class]="'badge delivery ' + tone(m.status)">{{ m.status === 'Suspended' ? 'Deactivated' : m.status }}</span></td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>
      @if (audit(); as entries) {
        <section class="card">
          <h2>Audit History</h2>
          <ul class="kv">
            @for (a of entries; track a.id) {
              <li><span>{{ words(a.action) }} · {{ a.targetType }}</span><span class="hint">{{ a.actor ?? 'System' }} · {{ when(a.atUtc) }} · {{ a.scope === 'PLATFORM' ? 'Operator' : 'Workspace' }}</span></li>
            } @empty { <li class="hint">No audit entries.</li> }
          </ul>
        </section>
      }
    }
  `,
})
export class WorkspaceDetailPage implements OnInit {
  private readonly api = inject(InternalApiService);
  readonly id = input.required<string>();
  protected readonly w = signal<WorkspaceDetail | null>(null);
  protected readonly audit = signal<AuditLogItem[] | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly tone = tone;
  protected readonly when = when;
  protected readonly words = words;

  ngOnInit(): void {
    void this.run(() => firstValueFrom(this.api.workspace(this.id())));
  }

  protected suspend(): void {
    if (confirm('Suspend this workspace? Members lose access and scheduled posts are not sent until it is reactivated.'))
      void this.run(() => firstValueFrom(this.api.suspendWorkspace(this.id())));
  }

  protected reactivate(): void {
    if (confirm('Reactivate this workspace? Members regain access.')) void this.run(() => firstValueFrom(this.api.reactivateWorkspace(this.id())));
  }

  protected async toggleAudit(): Promise<void> {
    if (this.audit()) return this.audit.set(null);
    try {
      this.audit.set(await firstValueFrom(this.api.workspaceAudit(this.id())));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  private async run(action: () => Promise<WorkspaceDetail>): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      this.w.set(await action());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
