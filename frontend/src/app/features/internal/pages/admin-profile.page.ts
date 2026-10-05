import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { InternalAuthService } from '../../../core/auth/internal-auth.service';

/** The signed-in platform operator. System administration is a platform role, never a workspace role. */
@Component({
  selector: 'app-internal-admin-profile-page',
  imports: [RouterLink],
  template: `
    <div class="page-header"><div><h1>Admin Profile</h1></div></div>
    @if (auth.me(); as me) {
      <section class="card narrow">
        <ul class="kv">
          <li><span>Name</span><span>{{ me.fullName }}</span></li>
          <li><span>Email</span><span>{{ me.email }}</span></li>
          <li><span>Role</span><span class="badge">{{ me.systemRole }}</span></li>
          <li><span>User ID</span><code>{{ me.id }}</code></li>
        </ul>
        <p class="hint">Your sign-ins and admin actions are listed under <a routerLink="/internal/logs">Logs</a>.</p>
      </section>
    }
  `,
})
export class AdminProfilePage {
  protected readonly auth = inject(InternalAuthService);
}
