import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet, Routes } from '@angular/router';
import { filter, firstValueFrom, map } from 'rxjs';
import { InternalAuthService } from '../../core/auth/internal-auth.service';
import { ToastService } from '../../core/services/toast.service';
import { InternalApiService, ago, words } from './internal-api.service';

/** Sidebar icons (24×24 stroke paths). */
const ICONS: Record<string, string> = {
  overview: 'M5 3h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM15 3h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM5 13h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2zM15 13h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2z',
  workspaces: 'M5 8h14a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2zM9 8V6a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2',
  users: 'M9 4.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7zM2.5 20a6.5 6.5 0 0 1 13 0M16 4.6a3.5 3.5 0 0 1 0 6.8M17.5 14a6.5 6.5 0 0 1 4 6',
  ai: 'M8.5 6h7A2.5 2.5 0 0 1 18 8.5v7a2.5 2.5 0 0 1-2.5 2.5h-7A2.5 2.5 0 0 1 6 15.5v-7A2.5 2.5 0 0 1 8.5 6zM9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3',
  social: 'M6 9.4a2.6 2.6 0 1 1 0 5.2 2.6 2.6 0 0 1 0-5.2zM18 3.4a2.6 2.6 0 1 1 0 5.2 2.6 2.6 0 0 1 0-5.2zM18 15.4a2.6 2.6 0 1 1 0 5.2 2.6 2.6 0 0 1 0-5.2zM8.3 10.8l7.4-3.6M8.3 13.2l7.4 3.6',
  operations: 'M5 4h14a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM5 13h14a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2zM7 7.5h.01M7 16.5h.01',
  monitoring: 'M3 12h4l2.5-7 4 14L16 12h5',
  profile: 'M12 4.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7zM5 20a7 7 0 0 1 14 0',
  signout: 'M9 21H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3M16 17l5-5-5-5M21 12H9',
};

/** Sidebar sections. Monitoring groups several routes, shown as tabs above the page. */
const NAV: { heading: string; items: { key: string; label: string; path: string; routes: string[] }[] }[] = [
  { heading: 'Overview', items: [{ key: 'overview', label: 'Overview', path: '', routes: [''] }] },
  {
    heading: 'Tenants',
    items: [
      { key: 'workspaces', label: 'Workspaces', path: 'workspaces', routes: ['workspaces'] },
      { key: 'users', label: 'Users', path: 'users', routes: ['users'] },
    ],
  },
  {
    heading: 'Configuration',
    items: [
      { key: 'ai', label: 'AI / LLM', path: 'ai', routes: ['ai'] },
      { key: 'social', label: 'Social Providers', path: 'social-providers', routes: ['social-providers'] },
    ],
  },
  {
    heading: 'System',
    items: [
      { key: 'monitoring', label: 'Monitoring', path: 'system-health', routes: ['system-health', 'logs'] },
    ],
  },
];

/** Pages with no sidebar entry (reached from Overview tiles and alerts); they still need a page title. */
const PAGE_TITLES: Record<string, string> = {
  workers: 'Workers', queues: 'Queues', publications: 'Publications', reconciliation: 'Reconciliation',
};

const TABS: Record<string, { path: string; label: string }[]> = {
  monitoring: [{ path: 'system-health', label: 'System Health' }, { path: 'logs', label: 'Logs' }],
};

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
        <div class="brand">
          <span class="mk"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2 2 7l10 5 10-5-10-5z"/><path d="M2 12l10 5 10-5"/><path d="M2 17l10 5 10-5"/></svg></span>
          <span><span class="t1">SocialMedia Engine</span><span class="t2">System Administration</span></span>
        </div>
        <nav class="side-scroll" aria-label="Internal">
          @for (group of nav; track group.heading) {
            <span class="nav-heading">{{ group.heading }}</span>
            @for (item of group.items; track item.key) {
              <a class="nav-item" [routerLink]="'/internal/' + item.path" [class.active]="section()?.key === item.key">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="icons[item.key]"/></svg>
                {{ item.label }}
              </a>
            }
          }
        </nav>
        <div class="identity">
          <a class="nav-item" routerLink="/internal/profile" routerLinkActive="active">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path [attr.d]="icons['profile']"/></svg>
            Admin Profile
          </a>
          <button type="button" class="nav-item" (click)="signOut()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="icons['signout']"/></svg>
            Sign Out
          </button>
          <span class="who">{{ auth.me()?.email }}</span>
        </div>
      </aside>

      <div class="internal-shell">
        <header class="internal-top">
          <span class="top-title">{{ title() }}</span>
          <div class="top-right">
            @if (meta(); as m) {
              <span class="env" [class.prod]="m.environment === 'PRODUCTION'">{{ m.environment }}</span>
              <span class="updated"><i></i>Updated {{ ago(m.generatedAtUtc) }}</span>
            }
            <button type="button" class="btn-ghost sm" (click)="refresh()">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 12a9 9 0 1 1-2.6-6.4"/><path d="M21 3v6h-6"/></svg>
              Refresh
            </button>
            <span class="admin-chip"><span class="av">{{ (auth.me()?.email ?? '?').charAt(0) }}</span><span class="em">{{ auth.me()?.email }}</span></span>
          </div>
        </header>

        <main class="internal-page">
          @if (tabs(); as t) {
            <nav class="tabs section-tabs">
              @for (tab of t; track tab.path) { <a [routerLink]="'/internal/' + tab.path" routerLinkActive="active">{{ tab.label }}</a> }
            </nav>
          }
          <!-- Refresh re-creates the page, so it loads its data again. -->
          @for (k of [reloadKey()]; track k) { <router-outlet /> }
        </main>
      </div>
    </div>
  `,
})
export class InternalLayoutComponent implements OnInit {
  protected readonly auth = inject(InternalAuthService);
  private readonly api = inject(InternalApiService);
  private readonly router = inject(Router);
  protected readonly nav = NAV;
  protected readonly icons = ICONS;
  protected readonly ago = ago;
  protected readonly reloadKey = signal(0);
  protected readonly meta = signal<{ environment: string; generatedAtUtc: string } | null>(null);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  /** Errors already shown as a popup (polls overlap on purpose, so nothing is missed between them). */
  private readonly seenErrors = new Set<string>();
  private errorWatch?: ReturnType<typeof setInterval>;

  private readonly url = toSignal(this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url });
  /** First path segment after /internal ('' for the overview). */
  private readonly segment = computed(() => this.url().split('?')[0].replace(/^\/internal\/?/, '').split('/')[0]);
  protected readonly section = computed(() => NAV.flatMap((g) => g.items).find((i) => i.routes.includes(this.segment())) ?? null);
  protected readonly tabs = computed(() => TABS[this.section()?.key ?? ''] ?? null);
  protected readonly title = computed(() => (this.segment() === 'profile' ? 'Admin Profile' : this.section()?.label ?? PAGE_TITLES[this.segment()] ?? ''));

  ngOnInit(): void {
    void this.loadMeta();
    // Every error anywhere in the system (API, AI, publishing, workers) pops up here, never in the customer app.
    void this.watchErrors();
    this.errorWatch = setInterval(() => void this.watchErrors(), ERROR_POLL_MS);
    this.destroyRef.onDestroy(() => clearInterval(this.errorWatch));
  }

  /**
   * Polls Logs → All for failures and warnings of the last few minutes (overlapping windows absorb clock skew between
   * browser and server) and shows each new one once as a popup.
   */
  private async watchErrors(): Promise<void> {
    const from = new Date(Date.now() - ERROR_WINDOW_MS).toISOString();
    const items = await firstValueFrom(this.api.allLog({ outcome: 'errors', from })).catch(() => null);
    if (!items) return;
    const fresh = items.filter((i) => !this.seenErrors.has(i.source + i.id)).reverse(); // oldest first
    fresh.forEach((i) => this.seenErrors.add(i.source + i.id));
    if (this.seenErrors.size > 2000) this.seenErrors.clear(); // ponytail: bounded memory; a reset may re-show the last few minutes once
    const openLogs = { label: 'Open logs', run: () => void this.router.navigateByUrl('/internal/logs') };
    // At most two popups at a time; a burst is summarised so the screen never fills up.
    for (const i of fresh.slice(-2)) {
      const text = `${i.outcome === 'FAILED' ? 'Error' : 'Warning'} · ${i.source} · ${words(i.event)}${i.workspace ? ` (${i.workspace})` : ''}: ${i.message}`;
      this.toast.error(text.length > 260 ? `${text.slice(0, 257)}…` : text, openLogs);
    }
    if (fresh.length > 2) this.toast.error(`${fresh.length - 2} more errors or warnings just happened.`, openLogs);
  }

  protected refresh(): void {
    this.reloadKey.update((k) => k + 1);
    void this.loadMeta();
  }

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/internal/login');
  }

  /** Environment badge and "Updated" time for the top bar. */
  private async loadMeta(): Promise<void> {
    const o = await firstValueFrom(this.api.overview()).catch(() => null);
    if (o) this.meta.set({ environment: o.environment, generatedAtUtc: o.generatedAtUtc });
  }
}

const ERROR_POLL_MS = 20_000;
const ERROR_WINDOW_MS = 5 * 60_000;

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
