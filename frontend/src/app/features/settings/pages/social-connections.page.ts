import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { SocialConnectionsComponent } from '../../social-accounts/components/social-connections.component';

/** LinkedIn, Facebook and Instagram status for everyone; Admins connect, reconnect or disconnect. No technical settings. */
@Component({
  selector: 'app-social-connections-page',
  imports: [SocialConnectionsComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Social Accounts</h1>
        <p class="muted">The pages and profiles your approved posts are published to.</p>
      </div>
    </div>
    @if (status === 'connected') {
      <p class="banner success" role="status">Account connected.</p>
    } @else if (status === 'failed') {
      <p class="banner error" role="alert">The connection did not complete: {{ message }}</p>
    }
    @if (workspace.orgId(); as orgId) {
      <section class="card narrow-card">
        <app-social-connections [orgId]="orgId" [allowDisconnect]="true" [canManage]="workspace.isAdmin()" />
        @if (!workspace.isAdmin()) { <p class="hint">Ask a workspace Admin to connect or reconnect accounts.</p> }
      </section>
    }
  `,
})
export class SocialConnectionsPage {
  protected readonly workspace = inject(WorkspaceService);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  protected readonly status = this.query.get('status');
  protected readonly message = this.query.get('message') ?? 'please try again.';
}
