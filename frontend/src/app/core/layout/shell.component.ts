import { Component, effect, inject, signal } from '@angular/core';
import { IsActiveMatchOptions, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { IconComponent } from '../../shared/components/icon.component';
import { AuthService } from '../auth/auth.service';
import { NotificationApiService } from '../services/notification-api.service';
import { TopbarActionService } from '../services/topbar-action.service';
import { WorkspaceService } from '../services/workspace.service';

interface NavItem {
  label: string;
  icon: string;
  link: string;
  /** Planner filters (e.g. ?status=Draft) for the Publisher shortcuts. */
  query?: Record<string, string>;
  /** Hidden unless true (UX only; the API enforces every permission). */
  show?: () => boolean;
}

/** Publisher shortcuts share the Planner route; only the one whose filter matches the URL is highlighted. */
const EXACT: IsActiveMatchOptions = { paths: 'exact', queryParams: 'exact', fragment: 'ignored', matrixParams: 'ignored' };

/**
 * The customer application: a persistent left sidebar grouped like a social-management product (Home, Create, Publisher,
 * Assets, Workspace). Navigation follows the workspace role as a convenience only. Internal platform administration and
 * infrastructure are deliberately never linked from here.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent],
  template: `
    <div class="app-layout" [class.nav-open]="navOpen()">
      <aside class="sidebar" aria-label="Main navigation">
        <div class="workspace-switch">
          <span class="workspace-avatar" aria-hidden="true">{{ initial() }}</span>
          @if (workspace.memberships().length > 1) {
            <select aria-label="Workspace" [value]="workspace.orgId()" (change)="switchWorkspace($event)">
              @for (m of workspace.memberships(); track m.organizationId) {
                <option [value]="m.organizationId">{{ m.organizationName }}</option>
              }
            </select>
          } @else {
            <strong class="workspace-name">{{ workspace.current()?.organizationName }}</strong>
          }
        </div>

        <nav class="side-nav">
          @for (item of main; track item.link) {
            @if (!item.show || item.show()) {
              <a [routerLink]="ws(item.link)" routerLinkActive="active"><app-icon [name]="item.icon" />{{ item.label }}</a>
            }
          }

          <button type="button" class="nav-group" [class.active]="inPublisher()" [attr.aria-expanded]="publisherOpen()" (click)="publisherOpen.set(!publisherOpen())">
            <app-icon name="send" />Publisher<app-icon class="chevron" [class.open]="publisherOpen()" name="chevron" [size]="14" />
          </button>
          @if (publisherOpen()) {
            <div class="sub-nav">
              @for (item of publisher; track item.label) {
                @if (!item.show || item.show()) {
                  <a [routerLink]="ws(item.link)" [queryParams]="item.query ?? null" routerLinkActive="active" [routerLinkActiveOptions]="exact">{{ item.label }}</a>
                }
              }
            </div>
          }

          @if (workspace.isAdmin()) {
            <span class="nav-heading">Assets</span>
            <a [routerLink]="ws('/library')" routerLinkActive="active"><app-icon name="image" />Media Library</a>

            <span class="nav-heading">Workspace</span>
            @for (item of workspaceItems; track item.link) {
              @if (!item.show || item.show()) {
                <a [routerLink]="ws(item.link)" routerLinkActive="active"><app-icon [name]="item.icon" />{{ item.label }}</a>
              }
            }
          }
        </nav>

        <nav class="side-nav bottom" aria-label="Account">
          @if (workspace.isAdmin()) {
            <a [routerLink]="ws('/settings/workspace')" routerLinkActive="active"><app-icon name="settings" />Settings</a>
          }
          <a [routerLink]="ws('/profile')" routerLinkActive="active"><app-icon name="user" />{{ auth.me()?.displayName || 'Profile' }}</a>
          <button type="button" (click)="signOut()"><app-icon name="logout" />Sign Out</button>
        </nav>
      </aside>

      <div class="app-main">
        <header class="topbar" [class.wide]="wide()">
          <nav class="topbar-inner" aria-label="Top bar">
            <button type="button" class="topbar-menu" aria-label="Menu" (click)="navOpen.set(!navOpen())"><app-icon name="menu" /></button>
            <a class="topbar-brand" [routerLink]="ws('/home')">
              <span class="topbar-logo" aria-hidden="true">
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 2L11 13"/><path d="M22 2l-7 20-4-9-9-4 20-7z"/></svg>
              </span>
              SocialMedia<span>Engine</span>
            </a>
            <div class="topbar-right">
              <a class="topbar-icon" [routerLink]="ws('/notifications')" routerLinkActive="active" title="Notifications"
                [attr.aria-label]="'Notifications' + (unread() ? ', ' + unread() + ' unread' : '')">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/></svg>
                @if (unread() > 0) { <span class="topbar-badge">{{ unread() > 9 ? '9+' : unread() }}</span> }
              </a>
              @if (topbar.action(); as a) {
                <button type="button" class="topbar-cta" [disabled]="a.disabled" [title]="a.title ?? ''" (click)="a.run()">
                  <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 2L11 13"/><path d="M22 2l-7 20-4-9-9-4 20-7z"/></svg>
                  {{ a.label }}
                </button>
              } @else if (workspace.canManageCampaigns()) {
                <a class="topbar-cta" [routerLink]="ws('/ai-studio')"><app-icon name="sparkles" [size]="16" />Create with AI</a>
              }
            </div>
          </nav>
        </header>
        <main class="page" [class.wide]="wide()">
          @if (workspace.current()) {
            <router-outlet />
          } @else {
            <p>You are not an active member of any workspace yet.</p>
          }
        </main>
      </div>
    </div>
  `,
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationApiService);
  protected readonly topbar = inject(TopbarActionService);

  protected readonly exact = EXACT;
  protected readonly main: NavItem[] = [
    { label: 'Home', icon: 'home', link: '/home' },
    { label: 'AI Studio', icon: 'sparkles', link: '/ai-studio', show: () => this.workspace.canManageCampaigns() },
    { label: 'Composer', icon: 'edit', link: '/composer', show: () => this.workspace.canManageCampaigns() },
    { label: 'Campaigns', icon: 'megaphone', link: '/campaigns', show: () => this.workspace.isAdmin() },
  ];
  protected readonly publisher: NavItem[] = [
    { label: 'Planner', icon: 'calendar', link: '/planner' },
    { label: 'All Posts', icon: 'list', link: '/planner', query: { view: 'list' } },
    { label: 'Approvals', icon: 'checkCircle', link: '/approvals', show: () => this.workspace.isAdmin() },
    { label: 'Drafts', icon: 'edit', link: '/planner', query: { status: 'Draft' } },
    { label: 'Scheduled', icon: 'clock', link: '/planner', query: { status: 'Scheduled' } },
    { label: 'Published', icon: 'globe', link: '/planner', query: { status: 'Published' } },
    { label: 'Failed', icon: 'alert', link: '/planner', query: { status: 'Failed' } },
  ];
  protected readonly workspaceItems: NavItem[] = [
    { label: 'Brand', icon: 'palette', link: '/brand', show: () => this.workspace.isAdmin() },
    { label: 'Members', icon: 'users', link: '/members', show: () => this.workspace.isAdmin() },
  ];

  protected readonly publisherOpen = signal(true);
  protected readonly inPublisher = signal(false);
  protected readonly navOpen = signal(false);
  protected readonly unread = signal(0);
  /** Home, Composer, AI Studio, Planner, Brand, Settings, Notifications, Members and the Approvals list use the full width beside the sidebar. */
  protected readonly wide = signal(false);

  /** App path inside the current workspace: /home -> /{slug}/home. */
  protected ws(path: string): string {
    return `/${this.workspace.slug()}${path}`;
  }

  protected initial(): string {
    return (this.workspace.current()?.organizationName ?? '?').trim().charAt(0).toUpperCase();
  }

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe((e) => {
      const section = (e as NavigationEnd).urlAfterRedirects.split('?')[0].split('/')[2];
      const inPublisher = section === 'planner' || section === 'approvals';
      this.inPublisher.set(inPublisher);
      const path = (e as NavigationEnd).urlAfterRedirects.split('?')[0].split('/');
      this.wide.set(['home', 'composer', 'ai-studio', 'planner', 'brand', 'settings', 'notifications', 'members'].includes(section) || (section === 'approvals' && !path[3]));
      if (inPublisher) this.publisherOpen.set(true);
      this.navOpen.set(false);
      void this.refreshUnread();
    });
    effect(() => {
      if (this.workspace.orgId()) void this.refreshUnread();
    });
  }

  switchWorkspace(event: Event): void {
    this.workspace.select((event.target as HTMLSelectElement).value);
    // Resources are workspace-scoped, so leave any open detail page.
    void this.router.navigateByUrl(this.ws('/home'));
  }

  async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }

  private async refreshUnread(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    try {
      this.unread.set((await firstValueFrom(this.notifications.unreadCount(orgId))).count);
    } catch {
      // The badge is a convenience; the Notifications page shows its own errors.
    }
  }
}
