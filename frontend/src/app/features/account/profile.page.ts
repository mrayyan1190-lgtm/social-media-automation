import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';

/** The signed-in user: name (editable), email, time zone and workspaces. */
@Component({
  selector: 'app-profile-page',
  imports: [FormsModule, RouterLink, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>Profile</h1></div></div>
    <app-problem-banner [problem]="problem()" />
    @if (saved()) { <p class="banner success" role="status">Profile saved.</p> }
    @if (auth.me(); as me) {
      <form class="card form-grid narrow" (ngSubmit)="save()">
        <label>Full Name <input name="fullName" required maxlength="200" [(ngModel)]="fullName" /></label>
        <label>Email <input [value]="me.email" disabled /></label>
        <label>Time Zone
          <input [value]="workspace.timeZone()" disabled />
          <span class="hint">Times follow your workspace time zone.@if (workspace.isAdmin()) { <a routerLink="/settings/workspace">Change it in Workspace Settings</a>. }</span>
        </label>
        <div class="actions">
          <button class="primary" type="submit" [disabled]="busy() || !fullName.trim() || !workspace.organization()">{{ busy() ? 'Saving…' : 'Save' }}</button>
        </div>
      </form>
      <section class="card narrow">
        <h2>Workspaces</h2>
        <ul class="rows">
          @for (m of me.memberships; track m.organizationId) {
            <li><span class="grow">{{ m.organizationName }}</span><span class="badge">{{ m.role }}</span></li>
          }
        </ul>
      </section>
      <button type="button" (click)="signOut()">Sign Out</button>
    }
  `,
})
export class ProfilePage {
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected fullName = '';

  constructor() {
    effect(() => (this.fullName = this.auth.me()?.displayName ?? ''));
  }

  protected async save(): Promise<void> {
    const organization = this.workspace.organization();
    if (!organization) return;
    this.busy.set(true);
    this.saved.set(false);
    this.problem.set(null);
    try {
      // The profile endpoint also carries the workspace time zone; send the current one so it never changes from here.
      await this.auth.completeProfile({ organizationId: organization.id, fullName: this.fullName.trim(), timeZoneId: organization.timeZoneId });
      this.saved.set(true);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
