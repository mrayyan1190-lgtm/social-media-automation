import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { OrganizationRole } from '../../../core/auth/auth.models';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { Member, OrganizationApiService } from '../../../core/services/organization-api.service';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { IconComponent } from '../../../shared/components/icon.component';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';

/**
 * Workspace members and roles (Admin only). Invite adds someone who already has an account; the API enforces
 * permission, concurrency and the last-Admin rule. Platform system administration never appears here.
 */
@Component({
  selector: 'app-team-page',
  imports: [FormsModule, IconComponent, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Team</h1>
        <p class="muted">Admins manage the workspace, brand, team and social accounts. Creators write, submit and schedule content. Reviewers approve or request changes.</p>
      </div>
      <button type="button" class="primary" (click)="inviteOpen.set(!inviteOpen())"><app-icon name="plus" [size]="16" />Invite Member</button>
    </div>

    @if (inviteOpen()) {
      <form class="card invite-form" (ngSubmit)="invite()">
        <label>Email <input type="email" name="email" required [(ngModel)]="email" placeholder="teammate@company.com" /></label>
        <label>Role
          <select name="role" [(ngModel)]="role">
            @for (r of roles; track r) { <option [value]="r">{{ r }}</option> }
          </select>
        </label>
        <div class="actions">
          <button type="button" (click)="inviteOpen.set(false)">Cancel</button>
          <button class="primary" type="submit" [disabled]="busy() === 'invite' || !email.trim()">{{ busy() === 'invite' ? 'Adding…' : 'Add to Workspace' }}</button>
        </div>
        <p class="hint wide">The person needs an account first: ask them to sign up, then add them here. They'll get a notification.</p>
      </form>
    }

    @if (saved(); as message) { <p class="banner success" role="status">{{ message }}</p> }
    <app-problem-banner [problem]="problem()" (reload)="load()" />
    @if (loading()) {
      <p class="muted">Loading team…</p>
    } @else {
      <div class="table-wrap">
        <table class="table">
          <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th></th></tr></thead>
          <tbody>
            @for (m of members(); track m.memberId) {
              <tr [class.muted]="m.status !== 'Active'">
                <td>{{ m.displayName || '—' }}</td>
                <td>{{ m.email }}</td>
                <td>
                  @if (m.status === 'Active') {
                    <select [attr.aria-label]="'Role of ' + m.displayName" [value]="m.role" [disabled]="busy() === m.memberId" (change)="changeRole(m, $any($event.target).value)">
                      @for (r of roles; track r) { <option [value]="r" [selected]="r === m.role">{{ r }}</option> }
                    </select>
                  } @else {
                    {{ m.role }}
                  }
                </td>
                <td><span [class]="'badge delivery ' + (m.status === 'Active' ? 'success' : 'neutral')">{{ m.status === 'Active' ? 'Active' : 'Deactivated' }}</span></td>
                <td class="row-actions">
                  @if (m.status === 'Active') {
                    <button type="button" class="link danger" [disabled]="busy() === m.memberId" (click)="deactivate(m)">Deactivate</button>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="empty">No members.</td></tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class TeamPage {
  private readonly api = inject(OrganizationApiService);
  protected readonly workspace = inject(WorkspaceService);
  protected readonly roles: OrganizationRole[] = ['Admin', 'Creator', 'Reviewer'];
  protected readonly members = signal<Member[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly saved = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly inviteOpen = signal(false);
  protected email = '';
  protected role: OrganizationRole = 'Creator';

  constructor() {
    effect(() => {
      if (this.workspace.orgId()) void this.load();
    });
  }

  async load(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.members.set(await firstValueFrom(this.api.members(orgId)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async invite(): Promise<void> {
    const added = await this.run('invite', (orgId) => firstValueFrom(this.api.addMember(orgId, this.email.trim(), this.role)));
    if (added) {
      this.saved.set(`${added.displayName || added.email} was added as ${added.role}.`);
      this.email = '';
      this.inviteOpen.set(false);
    }
  }

  protected async changeRole(member: Member, role: OrganizationRole): Promise<void> {
    if (role === member.role) return;
    const updated = await this.run(member.memberId, (orgId) => firstValueFrom(this.api.changeRole(orgId, member, role)));
    if (updated) this.saved.set(`${updated.displayName} is now ${updated.role}.`);
  }

  protected async deactivate(member: Member): Promise<void> {
    if (!confirm(`Deactivate ${member.displayName || member.email}? They lose access to this workspace. Their content stays.`)) return;
    const updated = await this.run(member.memberId, (orgId) => firstValueFrom(this.api.deactivate(orgId, member)));
    if (updated) this.saved.set(`${updated.displayName || updated.email} was deactivated.`);
  }

  /** Runs one change and always shows the server's truth afterwards (e.g. the last Admin cannot be removed). */
  private async run(key: string, action: (orgId: string) => Promise<Member>): Promise<Member | null> {
    const orgId = this.workspace.orgId();
    if (!orgId) return null;
    this.busy.set(key);
    this.saved.set(null);
    this.problem.set(null);
    try {
      return await action(orgId);
    } catch (error) {
      this.problem.set(toApiProblem(error));
      return null;
    } finally {
      this.busy.set(null);
      await this.load();
    }
  }
}
