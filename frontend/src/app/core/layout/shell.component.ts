import { Component, effect, inject, signal } from '@angular/core';
import { IsActiveMatchOptions, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { IconComponent } from '../../shared/components/icon.component';
import { AuthService } from '../auth/auth.service';
import { NotificationApiService } from '../services/notification-api.service';
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
                <a [routerLink]="ws(item.link)" [queryParams]="item.query ?? null" routerLinkActive="active" [routerLinkActiveOptions]="exact">{{ item.label }}</a>
              }
            </div>
          }

          <span class="nav-heading">Assets</span>
          <a [routerLink]="ws('/library')" routerLinkActive="active"><app-icon name="image" />Media Library</a>

          <span class="nav-heading">Workspace</span>
          @for (item of workspaceItems; track item.link) {
            @if (!item.show || item.show()) {
              <a [routerLink]="ws(item.link)" routerLinkActive="active"><app-icon [name]="item.icon" />{{ item.label }}</a>
            }
          }
        </nav>

        <nav class="side-nav bottom" aria-label="Account">
          <a [routerLink]="ws('/notifications')" routerLinkActive="active">
            <app-icon name="bell" />Notifications
            @if (unread() > 0) { <span class="count">{{ unread() }}</span> }
          </a>
          @if (workspace.isAdmin()) {
            <a [routerLink]="ws('/settings/workspace')" routerLinkActive="active"><app-icon name="settings" />Settings</a>
          }
          <a [routerLink]="ws('/profile')" routerLinkActive="active"><app-icon name="user" />{{ auth.me()?.displayName || 'Profile' }}</a>
          <button type="button" (click)="signOut()"><app-icon name="logout" />Sign Out</button>
        </nav>
      </aside>

      <div class="app-main">
        <header class="mobile-bar">
          <button type="button" aria-label="Menu" (click)="navOpen.set(!navOpen())"><app-icon name="menu" /></button>
          <strong>{{ workspace.current()?.organizationName }}</strong>
        </header>
        <main class="page">
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

  protected readonly exact = EXACT;
  protected readonly main: NavItem[] = [
    { label: 'Home', icon: 'home', link: '/home' },
    { label: 'AI Studio', icon: 'sparkles', link: '/ai-studio', show: () => this.workspace.canManageCampaigns() },
    { label: 'Composer', icon: 'edit', link: '/composer', show: () => this.workspace.canManageCampaigns() },
    { label: 'Campaigns', icon: 'megaphone', link: '/campaigns' },
  ];
  protected readonly publisher: NavItem[] = [
    { label: 'Planner', icon: 'calendar', link: '/planner' },
    { label: 'All Posts', icon: 'list', link: '/planner', query: { view: 'list' } },
    { label: 'Approvals', icon: 'checkCircle', link: '/approvals' },
    { label: 'Drafts', icon: 'edit', link: '/planner', query: { status: 'Draft' } },
    { label: 'Scheduled', icon: 'clock', link: '/planner', query: { status: 'Scheduled' } },
    { label: 'Published', icon: 'globe', link: '/planner', query: { status: 'Published' } },
    { label: 'Failed', icon: 'alert', link: '/planner', query: { status: 'Failed' } },
  ];
  protected readonly workspaceItems: NavItem[] = [
    { label: 'Brand', icon: 'palette', link: '/brand' },
    { label: 'Social Accounts', icon: 'share', link: '/social-accounts', show: () => this.workspace.isAdmin() },
    { label: 'Team', icon: 'users', link: '/team', show: () => this.workspace.isAdmin() },
  ];

  protected readonly publisherOpen = signal(true);
  protected readonly inPublisher = signal(false);
  protected readonly navOpen = signal(false);
  protected readonly unread = signal(0);

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
