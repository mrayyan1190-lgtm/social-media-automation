import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { InternalAuthService } from '../../../core/auth/internal-auth.service';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { InternalApiService, UserDetail, UserListItem, WorkspaceListItem, ago, tone, when, words } from '../internal-api.service';

/** All accounts. Never shows passwords, hashes, tokens or security stamps (the API never returns them). */
@Component({
  selector: 'app-internal-users-page',
  imports: [FormsModule, RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>Users</h1><p class="muted">Every account across workspaces.</p></div></div>
    <div class="filter-bar">
      <label class="search"><input type="search" placeholder="Search name or email" [(ngModel)]="search" (ngModelChange)="load()" aria-label="Search" /></label>
      <select [(ngModel)]="status" (ngModelChange)="load()" aria-label="Status">
        <option value="">All accounts</option><option value="active">Active</option><option value="disabled">Disabled</option>
      </select>
      <select [(ngModel)]="workspaceId" (ngModelChange)="load()" aria-label="Workspace">
        <option value="">All workspaces</option>
        @for (w of workspaces(); track w.id) { <option [value]="w.id">{{ w.name }}</option> }
      </select>
      <label class="date">Created from <input type="date" [(ngModel)]="createdFrom" (ngModelChange)="load()" /></label>
    </div>
    <app-problem-banner [problem]="problem()" />
    <div class="table-wrap">
      <table class="table">
        <thead><tr><th>Name</th><th>Email</th><th>Workspace(s)</th><th>Workspace Role</th><th>Account Status</th><th>Created</th><th>Last Login</th></tr></thead>
        <tbody>
          @for (u of rows(); track u.id) {
            <tr>
              <td><a [routerLink]="['/internal/users', u.id]">{{ u.name || '—' }}</a> @if (u.isSystemAdmin) { <span class="badge">System Admin</span> }</td>
              <td>{{ u.email }}</td>
              <td>@for (m of u.workspaces; track m.organizationId) { <div>{{ m.organizationName }}</div> } @empty { <span class="hint">—</span> }</td>
              <td>@for (m of u.workspaces; track m.organizationId) { <div>{{ m.role }}@if (m.status !== 'Active') { <span class="hint"> (deactivated)</span> }</div> }</td>
              <td><span [class]="'badge delivery ' + (u.isActive ? 'success' : 'danger')">{{ u.isActive ? 'Active' : 'Disabled' }}</span></td>
              <td>{{ when(u.createdAtUtc) }}</td>
              <td>{{ ago(u.lastLoginAtUtc) }}</td>
            </tr>
          } @empty { <tr><td colspan="7" class="empty">{{ loading() ? 'Loading…' : 'No users match.' }}</td></tr> }
        </tbody>
      </table>
    </div>
  `,
})
export class UsersPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly rows = signal<UserListItem[]>([]);
  protected readonly workspaces = signal<WorkspaceListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly when = when;
  protected readonly ago = ago;
  protected search = '';
  protected status = '';
  protected workspaceId = '';
  protected createdFrom = '';

  ngOnInit(): void {
    void this.load();
    void firstValueFrom(this.api.workspaces({})).then((w) => this.workspaces.set(w)).catch(() => undefined);
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.rows.set(await firstValueFrom(this.api.users({ search: this.search, status: this.status, workspaceId: this.workspaceId, createdFrom: this.createdFrom })));
      this.problem.set(null);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}

@Component({
  selector: 'app-internal-user-detail-page',
  imports: [RouterLink, ProblemBannerComponent],
  template: `
    <a class="back" routerLink="/internal/users">← Users</a>
    <app-problem-banner [problem]="problem()" />
    @if (d(); as detail) {
      @let u = detail.user;
      <div class="page-header">
        <div>
          <h1>{{ u.name || u.email }}</h1>
          <span [class]="'badge delivery ' + (u.isActive ? 'success' : 'danger')">{{ u.isActive ? 'Active' : 'Disabled' }}</span>
          @if (u.isSystemAdmin) { <span class="badge">System Admin</span> }
        </div>
        <div class="actions">
          @if (u.isActive) {
            <button type="button" class="danger" (click)="disable()" [disabled]="busy() || u.id === me()">Disable User</button>
          } @else {
            <button type="button" class="primary" (click)="enable()" [disabled]="busy()">Enable User</button>
          }
        </div>
      </div>
      <div class="admin-grid">
        <section class="card">
          <h2>Account</h2>
          <ul class="kv">
            <li><span>Full Name</span><span>{{ u.name || '—' }}</span></li>
            <li><span>Email</span><span>{{ u.email }}</span></li>
            <li><span>User ID</span><code>{{ u.id }}</code></li>
            <li><span>Created</span><span>{{ when(u.createdAtUtc) }}</span></li>
            <li><span>Last Login</span><span>{{ when(u.lastLoginAtUtc) }}</span></li>
            <li><span>Status</span><span>{{ u.isActive ? 'Active' : 'Disabled' }}</span></li>
          </ul>
        </section>
        <section class="card">
          <h2>Workspace Memberships</h2>
          <ul class="kv">
            @for (m of u.workspaces; track m.organizationId) {
              <li><a [routerLink]="['/internal/workspaces', m.organizationId]">{{ m.organizationName }}</a><span>{{ m.role }} · {{ m.status === 'Active' ? 'Active' : 'Deactivated' }}</span></li>
            } @empty { <li class="hint">No workspaces.</li> }
          </ul>
        </section>
      </div>
      <div class="admin-grid">
        <section class="card">
          <h2>Sign-in Activity</h2>
          <ul class="kv">
            @for (s of detail.recentSignIns; track s.id) {
              <li><span><span [class]="'badge delivery ' + (s.succeeded ? 'success' : 'danger')">{{ words(s.eventType) }}</span> {{ s.area === 'INTERNAL' ? 'Internal' : 'Customer' }}</span>
                <span class="hint">{{ s.ipAddress ?? '—' }} · {{ when(s.atUtc) }}</span></li>
            } @empty { <li class="hint">No sign-ins recorded.</li> }
          </ul>
        </section>
        <section class="card">
          <h2>Audit Activity</h2>
          <ul class="kv">
            @for (a of detail.recentAudit; track a.id) {
              <li><span>{{ words(a.action) }} · {{ a.target }}</span><span class="hint">{{ a.workspace ?? 'Platform' }} · {{ when(a.atUtc) }}</span></li>
            } @empty { <li class="hint">No audit activity.</li> }
          </ul>
        </section>
      </div>
    }
  `,
})
export class UserDetailPage implements OnInit {
  private readonly api = inject(InternalApiService);
  private readonly auth = inject(InternalAuthService);
  readonly id = input.required<string>();
  protected readonly d = signal<UserDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly when = when;
  protected readonly words = words;
  protected readonly tone = tone;
  protected readonly me = () => this.auth.me()?.id;

  ngOnInit(): void {
    void this.run(() => firstValueFrom(this.api.user(this.id())));
  }

  protected disable(): void {
    if (confirm('Disable this account? The user is signed out everywhere and cannot sign in until enabled.'))
      void this.run(() => firstValueFrom(this.api.disableUser(this.id())));
  }

  protected enable(): void {
    void this.run(() => firstValueFrom(this.api.enableUser(this.id())));
  }

  private async run(action: () => Promise<UserDetail>): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      this.d.set(await action());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
