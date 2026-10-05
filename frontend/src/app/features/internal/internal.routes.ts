import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet, Routes } from '@angular/router';
import { InternalAuthService } from '../../core/auth/internal-auth.service';

const NAV: { heading: string; items: { path: string; label: string }[] }[] = [
  { heading: 'Overview', items: [{ path: '', label: 'Overview' }] },
  { heading: 'Tenants', items: [{ path: 'workspaces', label: 'Workspaces' }, { path: 'users', label: 'Users' }] },
  { heading: 'Configuration', items: [{ path: 'ai', label: 'AI / LLM' }, { path: 'social-providers', label: 'Social Providers' }] },
  {
    heading: 'Operations',
    items: [
      { path: 'workers', label: 'Workers' }, { path: 'queues', label: 'Queues' },
      { path: 'publications', label: 'Publications' }, { path: 'reconciliation', label: 'Reconciliation' },
    ],
  },
  { heading: 'Monitoring', items: [{ path: 'system-health', label: 'System Health' }, { path: 'logs', label: 'Logs' }] },
];

/**
 * Internal platform administration: its own grouped sidebar, never linked from (and never linking to) the customer
 * application. Every route sits behind systemAdminGuard; every /api/internal endpoint re-checks on the server.
 */
@Component({
  selector: 'app-internal-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="internal">
      <aside class="internal-nav">
        <div class="brand">System Administration</div>
        <nav aria-label="Internal">
          @for (group of nav; track group.heading) {
            <span class="nav-heading">{{ group.heading }}</span>
            @for (item of group.items; track item.path) {
              <a [routerLink]="'/internal/' + item.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: !item.path }">{{ item.label }}</a>
            }
          }
        </nav>
        <div class="identity">
          <a routerLink="/internal/profile" routerLinkActive="active">Admin Profile</a>
          <button type="button" (click)="signOut()">Sign Out</button>
          <span class="who">{{ auth.me()?.email }}</span>
        </div>
      </aside>
      <main class="page internal-page"><router-outlet /></main>
    </div>
  `,
})
export class InternalLayoutComponent {
  protected readonly auth = inject(InternalAuthService);
  private readonly router = inject(Router);
  protected readonly nav = NAV;

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/internal/login');
  }
}

export const INTERNAL_ROUTES: Routes = [
  {
    path: '',
    component: InternalLayoutComponent,
    children: [
      { path: '', loadComponent: () => import('./pages/overview.page').then((m) => m.OverviewPage) },
      { path: 'workspaces', loadComponent: () => import('./pages/workspaces.page').then((m) => m.WorkspacesPage) },
      { path: 'workspaces/:id', loadComponent: () => import('./pages/workspaces.page').then((m) => m.WorkspaceDetailPage) },
      { path: 'users', loadComponent: () => import('./pages/users.page').then((m) => m.UsersPage) },
      { path: 'users/:id', loadComponent: () => import('./pages/users.page').then((m) => m.UserDetailPage) },
      { path: 'ai', loadComponent: () => import('./pages/ai-configuration.page').then((m) => m.AiConfigurationPage) },
      { path: 'social-providers', loadComponent: () => import('./pages/social-providers.page').then((m) => m.SocialProvidersPage) },
      { path: 'workers', loadComponent: () => import('./pages/workers.page').then((m) => m.WorkersPage) },
      { path: 'queues', loadComponent: () => import('./pages/queues.page').then((m) => m.QueuesPage) },
      { path: 'publications', loadComponent: () => import('./pages/publications.page').then((m) => m.PublicationsPage), data: { mode: 'all' } },
      { path: 'publications/:id', loadComponent: () => import('./pages/publications.page').then((m) => m.PublicationDetailPage), data: { mode: 'all' } },
      { path: 'reconciliation', loadComponent: () => import('./pages/publications.page').then((m) => m.PublicationsPage), data: { mode: 'reconciliation' } },
      { path: 'reconciliation/:id', loadComponent: () => import('./pages/publications.page').then((m) => m.PublicationDetailPage), data: { mode: 'reconciliation' } },
      { path: 'system-health', loadComponent: () => import('./pages/system-health.page').then((m) => m.SystemHealthPage) },
      { path: 'logs', loadComponent: () => import('./pages/logs.page').then((m) => m.LogsPage) },
      { path: 'profile', loadComponent: () => import('./pages/admin-profile.page').then((m) => m.AdminProfilePage) },
      // Older link: audit now lives under Logs.
      { path: 'audit', pathMatch: 'full', redirectTo: 'logs' },
    ],
  },
];
