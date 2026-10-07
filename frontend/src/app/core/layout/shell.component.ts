import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { IsActiveMatchOptions, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { DashboardApiService } from '../../features/dashboard/dashboard-api.service';
import { formatInZone } from '../../features/publications/models/time-zones';
import { PROBLEM_DELIVERY, UPCOMING_DELIVERY } from '../../shared/models/states';
import { AuthService } from '../auth/auth.service';
import { NotificationApiService } from '../services/notification-api.service';
import { TopbarActionService } from '../services/topbar-action.service';
import { WorkspaceService } from '../services/workspace.service';

type Icon = 'home' | 'studio' | 'composer' | 'publisher' | 'media' | 'brand' | 'members' | 'settings' | 'signout';

/** The sidebar's icon set (the dashboard design's strokes). */
@Component({
  selector: 'app-nav-icon',
  template: `
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      @switch (name()) {
        @case ('home') { <path d="M4 10.4 12 4l8 6.4V19a1.6 1.6 0 0 1-1.6 1.6h-3.6V15H9.2v5.6H5.6A1.6 1.6 0 0 1 4 19v-8.6Z"/> }
        @case ('studio') { <path d="M11 3.6l1.6 4.3 4.3 1.6-4.3 1.6L11 15.4 9.4 11.1 5.1 9.5l4.3-1.6L11 3.6Z"/><path d="M17.6 14.2l.9 2.4 2.4.9-2.4.9-.9 2.4-.9-2.4-2.4-.9 2.4-.9.9-2.4Z" stroke-width="1.5"/> }
        @case ('composer') { <path d="M15.6 4.4 19.6 8.4 9 19H5v-4L15.6 4.4Z"/><path d="M14 6l4 4"/> }
        @case ('publisher') { <path d="M21 3 10.6 13.4M21 3l-6.7 18-3.7-7.6L3 9.7 21 3Z"/> }
        @case ('media') { <rect x="3.6" y="4.6" width="16.8" height="14.8" rx="3"/><circle cx="9" cy="10" r="1.7" stroke-width="1.6"/><path d="M4.8 17.6 9.6 13l3.7 3.3 2.3-1.9 4 3.2"/> }
        @case ('brand') { <path d="M12 3.6a8.4 8.4 0 0 0 0 16.8c1.2 0 2.1-.9 2.1-2 0-.6-.2-1-.6-1.4-.3-.4-.5-.8-.5-1.3 0-1.1.9-2 2-2h1.6a3.8 3.8 0 0 0 3.8-3.9c-1.3-3.4-4.6-5.6-8.4-6.2Z"/><circle cx="8.6" cy="10.4" r="1.1" fill="currentColor"/><circle cx="12.4" cy="7.9" r="1.1" fill="currentColor"/><circle cx="16" cy="10.6" r="1.1" fill="currentColor"/> }
        @case ('members') { <circle cx="9.4" cy="8.6" r="3.2"/><path d="M3.8 19.6c.6-3.2 2.9-5 5.6-5s5 1.8 5.6 5"/><path d="M16.2 5.9a3 3 0 0 1 0 5.5M18 19.6c-.2-1.8-.8-3.3-1.7-4.4 2.3.3 4 2 4.5 4.4"/> }
        @case ('settings') { <circle cx="12" cy="12" r="3.1"/><path d="M12 3.6v2.2M12 18.2v2.2M4.6 12h2.2M17.2 12h2.2M6.8 6.8l1.6 1.6M15.6 15.6l1.6 1.6M17.2 6.8l-1.6 1.6M8.4 15.6l-1.6 1.6"/> }
        @case ('signout') { <path d="M14.4 12H4"/><path d="M8.4 8 4.4 12l4 4"/><path d="M12.6 4.2h4.8a2 2 0 0 1 2 2v11.6a2 2 0 0 1-2 2h-4.8"/> }
      }
    </svg>
  `,
  styles: [':host{display:grid;place-items:center;width:18px;height:18px;flex:none} svg{width:18px;height:18px;display:block}'],
})
export class NavIconComponent {
  readonly name = input.required<Icon>();
}

interface NavItem {
  label: string;
  icon: Icon;
  link: string;
  /** Hidden unless true (UX only; the API enforces every permission). */
  show?: () => boolean;
}

interface SubItem {
  label: string;
  link: string;
  /** Planner filters (e.g. ?status=Draft) for the Publisher shortcuts. */
  query?: Record<string, string>;
  count?: () => number;
  tone?: 'warn' | 'rose';
  show?: () => boolean;
}

/** Publisher shortcuts share the Planner route; only the one whose filter matches the URL is highlighted. */
const EXACT: IsActiveMatchOptions = { paths: 'exact', queryParams: 'exact', fragment: 'ignored', matrixParams: 'ignored' };
const RAIL_KEY = 'shell-rail';

const TITLES: Record<string, string> = {
  home: 'Home', 'ai-studio': 'AI Studio', composer: 'Composer', campaigns: 'Campaigns', planner: 'Publisher', approvals: 'Approvals',
  library: 'Media Library', brand: 'Brand', members: 'Members', settings: 'Settings', notifications: 'Notifications', profile: 'Profile',
};

/**
 * The customer application: a sidebar grouped like a social-management product (Home, Create, Publisher, Assets,
 * Workspace) with live Publisher counts, and a top bar with the page title, search, notifications and Create with AI.
 * Navigation follows the workspace role as a convenience only. Internal platform administration is never linked here.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NavIconComponent],
  template: `
    <div class="shell" [class.rail]="rail()" [class.drawer]="navOpen()">
      <aside class="side" aria-label="Main navigation">
        <div class="brand">
          <span class="logo" aria-hidden="true">{{ initial() }}</span>
          <span class="bname-wrap">
            @if (workspace.memberships().length > 1) {
              <select class="bselect" aria-label="Workspace" [value]="workspace.orgId()" (change)="switchWorkspace($event)">
                @for (m of workspace.memberships(); track m.organizationId) { <option [value]="m.organizationId">{{ m.organizationName }}</option> }
              </select>
            } @else {
              <span class="bname">{{ workspace.current()?.organizationName }}</span>
            }
            <span class="btag">Social suite</span>
          </span>
        </div>

        <nav class="snav">
          @for (item of main; track item.link) {
            @if (!item.show || item.show()) {
              <a class="nitem" [routerLink]="ws(item.link)" routerLinkActive="on" [title]="item.label"><app-nav-icon [name]="item.icon" /><span class="nl">{{ item.label }}</span></a>
            }
          }

          <button type="button" class="nitem grp" [class.open]="publisherOpen()" [class.inside]="inPublisher()" [attr.aria-expanded]="publisherOpen()"
            title="Publisher" (click)="publisherOpen.set(!publisherOpen())">
            <app-nav-icon name="publisher" /><span class="nl">Publisher</span>
            <span class="chev"><svg viewBox="0 0 24 24" fill="none"><path d="M6 9.5l6 6 6-6" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg></span>
          </button>
          @if (publisherOpen()) {
            <div class="sub">
              @for (item of publisher; track item.label) {
                @if (!item.show || item.show()) {
                  <a class="sitem" [routerLink]="ws(item.link)" [queryParams]="item.query ?? null" routerLinkActive="on" [routerLinkActiveOptions]="exact">
                    {{ item.label }}
                    @if (item.count && item.count() > 0) { <span [class]="'cnt ' + (item.tone ?? '')">{{ item.count() }}</span> }
                  </a>
                }
              }
            </div>
          }

          @if (workspace.isAdmin()) {
            <p class="slabel">Assets</p>
            <a class="nitem" [routerLink]="ws('/library')" routerLinkActive="on" title="Media Library"><app-nav-icon name="media" /><span class="nl">Media Library</span></a>
            <p class="slabel">Workspace</p>
            <a class="nitem" [routerLink]="ws('/brand')" routerLinkActive="on" title="Brand"><app-nav-icon name="brand" /><span class="nl">Brand</span></a>
            <a class="nitem" [routerLink]="ws('/members')" routerLinkActive="on" title="Members"><app-nav-icon name="members" /><span class="nl">Members</span></a>
          }
        </nav>

        <div class="sfoot">
          @if (workspace.isAdmin()) {
            <a class="nitem" [routerLink]="ws('/settings/workspace')" routerLinkActive="on" title="Settings"><app-nav-icon name="settings" /><span class="nl">Settings</span></a>
          }
          <a class="user" [routerLink]="ws('/profile')" title="Profile">
            <span class="uav">{{ meInitials() }}</span>
            <span class="utext"><span class="uname">{{ auth.me()?.displayName || 'Profile' }}</span>
              <span class="urole">{{ workspace.current()?.role }} · {{ workspace.current()?.organizationName }}</span></span>
          </a>
          <button type="button" class="nitem out" title="Sign Out" (click)="signOut()"><app-nav-icon name="signout" /><span class="nl">Sign Out</span></button>
        </div>
      </aside>

      <div class="main">
        <header class="top">
          <div class="top-in">
            <button type="button" class="tgl" title="Toggle sidebar" aria-label="Toggle sidebar" (click)="toggleNav()">
              <svg viewBox="0 0 24 24" fill="none"><rect x="3.2" y="4.2" width="17.6" height="15.6" rx="3" stroke="currentColor" stroke-width="1.8"/><path d="M9.6 4.4v15.2" stroke="currentColor" stroke-width="1.8"/></svg>
            </button>
            <h1 class="ptitle">{{ title() }}</h1>
            <span class="pdate">{{ today() }}</span>
            <form class="search" role="search" (submit)="search($event, q.value); q.value = ''">
              <svg viewBox="0 0 24 24" fill="none"><circle cx="11" cy="11" r="6.6" stroke="currentColor" stroke-width="1.9"/><path d="M16 16l4.2 4.2" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"/></svg>
              <input #q type="search" placeholder="Search posts, campaigns, people…" aria-label="Search posts" />
            </form>
            <div class="tacts">
              <a class="icon-btn" [routerLink]="ws('/notifications')" [title]="unread() ? unread() + ' new notifications' : 'Notifications'"
                [attr.aria-label]="'Notifications' + (unread() ? ', ' + unread() + ' unread' : '')">
                <svg viewBox="0 0 24 24" fill="none"><path d="M6.4 15.4V10a5.6 5.6 0 1 1 11.2 0v5.4l1.5 2.8H4.9l1.5-2.8Z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"/><path d="M10 21h4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
                @if (unread() > 0) { <span class="dot"></span> }
              </a>
              @if (topbar.action(); as a) {
                <button type="button" class="create" [disabled]="a.disabled" [title]="a.title ?? ''" (click)="a.run()">
                  <svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 2L11 13"/><path d="M22 2l-7 20-4-9-9-4 20-7z"/></svg>{{ a.label }}
                </button>
              } @else if (workspace.canManageCampaigns()) {
                <a class="create" [routerLink]="ws('/ai-studio')">
                  <svg viewBox="0 0 24 24" fill="none"><path d="M12 3l1.7 4.6L18 9.3l-4.3 1.7L12 15.6l-1.7-4.6L6 9.3l4.3-1.7L12 3Z" fill="#fff"/><path d="M18.5 15l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8.8-2.2Z" fill="#fff" opacity=".85"/></svg>Create with AI
                </a>
              }
              <a class="me" [routerLink]="ws('/profile')" [title]="auth.me()?.displayName ?? 'Profile'"><span class="me-in">{{ meInitials() }}</span></a>
            </div>
          </div>
        </header>
        <main class="page" [class.wide]="wide()">
          @if (workspace.current()) {
            <router-outlet />
          } @else {
            <p>You are not an active member of any workspace yet.</p>
          }
        </main>
      </div>
      <div class="scrim" (click)="navOpen.set(false)"></div>
    </div>
  `,
  styles: [`
    :host{display:block;--line:#e9edf4;--line-2:#f1f4f9;--ink:#0f1c33;--ink-2:#26334d;--muted:#6b7891;--faint:#98a3b6;--rose:#dc4d5b}
    .shell{display:flex;min-height:100vh}
    .side{width:264px;flex:none;background:#fff;border-right:1px solid var(--line);position:sticky;top:0;height:100vh;display:flex;
      flex-direction:column;z-index:60;box-shadow:16px 0 44px -36px rgba(15,28,51,.55);transition:width .2s ease}
    .brand{display:flex;align-items:center;gap:10px;padding:16px 16px 14px;min-width:0}
    .logo{width:34px;height:34px;border-radius:11px;flex:none;display:grid;place-items:center;color:#fff;font-size:17px;font-weight:800;
      background:linear-gradient(140deg,#3b82f6,#123a7a);box-shadow:0 8px 18px -10px rgba(18,58,122,.95)}
    .bname-wrap{min-width:0;display:block}
    .bname{font-size:15.5px;font-weight:750;letter-spacing:-.4px;display:block;color:var(--ink);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .bselect{font:inherit;font-size:14.5px;font-weight:700;color:var(--ink);border:0;background:none;padding:0;max-width:170px;height:auto;min-height:0;cursor:pointer}
    .btag{display:block;font-size:10.5px;font-weight:650;letter-spacing:.09em;text-transform:uppercase;color:var(--faint);margin-top:1px}

    .snav{flex:1;overflow-y:auto;padding:4px 12px 12px}
    .snav::-webkit-scrollbar{width:8px}
    .snav::-webkit-scrollbar-thumb{background:#e7ecf4;border-radius:8px}
    .nitem{display:flex;align-items:center;gap:11px;width:100%;padding:9px 11px;border:0;background:none;border-radius:10px;font:inherit;
      font-size:13.5px;font-weight:550;color:var(--ink-2);text-decoration:none;cursor:pointer;transition:.15s;text-align:left;min-height:0;margin:0}
    .nitem + .nitem{margin-top:2px}
    .nitem:hover{background:#f4f7fc}
    .nitem app-nav-icon{color:#7d8aa0}
    .nl{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .nitem.on{background:linear-gradient(135deg,#2f6fed,#123a7a);color:#fff;font-weight:650;box-shadow:0 12px 26px -14px rgba(20,60,140,.9)}
    .nitem.on app-nav-icon{color:#fff}
    .grp{margin-top:2px}
    .grp .chev{margin-left:auto;color:var(--faint);display:grid;place-items:center;transition:transform .18s}
    .grp .chev svg{width:15px;height:15px;display:block}
    .grp.open,.grp.inside{background:#f4f7fc;color:var(--ink)}
    .grp.open .chev{transform:rotate(180deg)}
    .nitem.out{color:#b23b48}
    .nitem.out app-nav-icon{color:#cf6b76}
    .cnt{margin-left:auto;font-size:11px;font-weight:700;color:#5b6a86;background:#eef1f7;border-radius:20px;padding:1.5px 7px;font-variant-numeric:tabular-nums}
    .cnt.warn{background:#fdf3e2;color:#a96f0a}
    .cnt.rose{background:#fdeef0;color:#c34a57}
    .slabel{display:flex;align-items:center;gap:10px;margin:16px 12px 7px;font-size:10.5px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:var(--faint)}
    .slabel::after{content:"";flex:1;height:1px;background:var(--line-2)}
    .sub{position:relative;margin:1px 0 4px 21px;padding-left:13px;display:flex;flex-direction:column;gap:1px}
    .sub::before{content:"";position:absolute;left:0;top:7px;bottom:7px;width:2px;border-radius:2px;background:#e6ebf4}
    .sitem{display:flex;align-items:center;gap:8px;padding:7px 10px;border-radius:8px;font-size:13px;color:var(--muted);text-decoration:none;cursor:pointer;transition:.14s;position:relative}
    .sitem:hover{background:#f4f7fc;color:var(--ink-2)}
    .sitem.on{background:#eaf1fe;color:#2059c6;font-weight:650}
    .sitem.on::before{content:"";position:absolute;left:-13px;top:7px;bottom:7px;width:2px;border-radius:2px;background:#2f6fed}

    .sfoot{border-top:1px solid var(--line);padding:10px 12px 14px}
    .user{display:flex;align-items:center;gap:10px;padding:9px 11px;border-radius:11px;background:#f7f9fc;border:1px solid var(--line-2);margin:6px 0;text-decoration:none;color:var(--ink)}
    .user:hover{border-color:#e0e6f0}
    .uav{width:30px;height:30px;border-radius:50%;flex:none;display:grid;place-items:center;font-size:11px;font-weight:700;color:#fff;background:linear-gradient(140deg,#2f6fed,#123a7a)}
    .utext{min-width:0}
    .uname{display:block;font-size:13px;font-weight:650;line-height:1.25;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .urole{display:block;font-size:11px;color:var(--muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}

    .main{flex:1;min-width:0}
    .top{background:rgba(255,255,255,.92);backdrop-filter:saturate(140%) blur(6px);border-bottom:1px solid var(--line);position:sticky;top:0;z-index:50}
    .top-in{display:flex;align-items:center;gap:14px;height:62px;padding:0 24px}
    .tgl,.icon-btn{width:34px;height:34px;border-radius:10px;border:1px solid var(--line);background:#fff;cursor:pointer;display:grid;place-items:center;
      color:var(--muted);flex:none;transition:.15s;position:relative;padding:0;min-height:0}
    .icon-btn{color:var(--ink-2)}
    .tgl:hover,.icon-btn:hover{background:#f4f7fc;color:var(--ink)}
    .tgl svg,.icon-btn svg{width:17px;height:17px;display:block}
    .icon-btn .dot{position:absolute;top:7px;right:8px;width:7px;height:7px;border-radius:50%;background:var(--rose);box-shadow:0 0 0 2px #fff}
    .ptitle{margin:0;font-size:16px;font-weight:700;letter-spacing:-.35px;white-space:nowrap;color:var(--ink)}
    .pdate{font-size:12.5px;color:var(--faint);white-space:nowrap}
    .search{margin:0 0 0 14px;flex:1;max-width:320px;display:flex;align-items:center;gap:8px;height:36px;padding:0 12px;border:1px solid var(--line);
      border-radius:10px;background:#f7f9fc;transition:.15s}
    .search:focus-within{background:#fff;border-color:#c7d7fa;box-shadow:0 0 0 3px rgba(47,111,237,.12)}
    .search svg{width:15px;height:15px;display:block;color:var(--faint);flex:none}
    .search input{border:0;background:none;outline:none;box-shadow:none;font:inherit;font-size:13px;color:var(--ink);width:100%;height:auto;padding:0;min-height:0}
    .tacts{margin-left:auto;display:flex;align-items:center;gap:12px}
    .create{display:inline-flex;align-items:center;gap:7px;border:0;cursor:pointer;padding:8px 14px;border-radius:9px;color:#fff;font:inherit;font-size:13px;
      font-weight:600;white-space:nowrap;text-decoration:none;min-height:0;
      background:linear-gradient(140deg,#2f6fed,#123a7a);box-shadow:0 8px 18px -10px rgba(18,58,122,.9);transition:.18s}
    .create:hover:not(:disabled){transform:translateY(-1px)}
    .create:disabled{opacity:.55;cursor:not-allowed}
    .create svg{width:14px;height:14px;display:block}
    .me{width:32px;height:32px;border-radius:50%;flex:none;padding:2px;cursor:pointer;text-decoration:none;background:conic-gradient(from 210deg,#f58529,#dd2a7b,#8134af)}
    .me-in{width:100%;height:100%;border-radius:50%;background:#fff;display:grid;place-items:center;font-size:11px;font-weight:700;color:#123a7a;letter-spacing:.3px}
    .scrim{position:fixed;inset:0;background:rgba(15,28,51,.45);opacity:0;pointer-events:none;transition:.2s;z-index:70}

    @media (min-width:1025px){
      .shell.rail .side{width:76px}
      .shell.rail .bname-wrap,.shell.rail .nl,.shell.rail .cnt,.shell.rail .slabel,.shell.rail .sub,.shell.rail .utext,.shell.rail .chev{display:none}
      .shell.rail .brand{justify-content:center;padding:16px 0 12px}
      .shell.rail .snav{padding:4px 10px 12px}
      .shell.rail .nitem{justify-content:center;padding:10px 0}
      .shell.rail .sfoot{padding:10px 10px 14px}
      .shell.rail .user{justify-content:center;padding:8px 0}
    }
    @media (max-width:1024px){
      .side{position:fixed;inset:0 auto 0 0;width:272px;transform:translateX(-102%);transition:transform .22s ease}
      .shell.drawer .side{transform:none;box-shadow:26px 0 60px -30px rgba(15,28,51,.65)}
      .shell.drawer .scrim{opacity:1;pointer-events:auto}
      .top-in{padding:0 16px}
      .search{display:none}
    }
    @media (max-width:560px){ .pdate{display:none} .create{padding:8px 10px} }
  `],
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationApiService);
  private readonly dashboard = inject(DashboardApiService);
  protected readonly topbar = inject(TopbarActionService);

  protected readonly exact = EXACT;
  /** Publisher counts from the workspace overview (refreshed on navigation). */
  private readonly counts = signal({ approvals: 0, drafts: 0, scheduled: 0, published: 0, failed: 0 });

  protected readonly main: NavItem[] = [
    { label: 'Home', icon: 'home', link: '/home' },
    { label: 'AI Studio', icon: 'studio', link: '/ai-studio', show: () => this.workspace.canManageCampaigns() },
    { label: 'Composer', icon: 'composer', link: '/composer', show: () => this.workspace.canManageCampaigns() },
  ];
  protected readonly publisher: SubItem[] = [
    { label: 'Planner', link: '/planner' },
    { label: 'All Posts', link: '/planner', query: { view: 'list' } },
    { label: 'Approvals', link: '/approvals', count: () => this.counts().approvals, tone: 'warn', show: () => this.workspace.isAdmin() },
    { label: 'Drafts', link: '/planner', query: { status: 'Draft' }, count: () => this.counts().drafts },
    { label: 'Scheduled', link: '/planner', query: { status: 'Scheduled' }, count: () => this.counts().scheduled },
    { label: 'Published', link: '/planner', query: { status: 'Published' }, count: () => this.counts().published },
    { label: 'Failed', link: '/planner', query: { status: 'Failed' }, count: () => this.counts().failed, tone: 'rose' },
  ];

  protected readonly publisherOpen = signal(true);
  protected readonly inPublisher = signal(false);
  protected readonly navOpen = signal(false);
  protected readonly rail = signal(readRail());
  protected readonly unread = signal(0);
  protected readonly title = signal('Home');
  /** Home, Composer, AI Studio, Planner, Brand, Settings, Notifications, Members and the Approvals list use the full width beside the sidebar. */
  protected readonly wide = signal(false);
  protected readonly today = computed(() =>
    formatInZone(new Date().toISOString(), this.workspace.timeZone(), { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }));
  protected readonly meInitials = computed(() =>
    (this.auth.me()?.displayName ?? '?').split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?');

  /** App path inside the current workspace: /home -> /{slug}/home. */
  protected ws(path: string): string {
    return `/${this.workspace.slug()}${path}`;
  }

  protected initial(): string {
    return (this.workspace.current()?.organizationName ?? '?').trim().charAt(0).toUpperCase();
  }

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe((e) => {
      const path = (e as NavigationEnd).urlAfterRedirects.split('?')[0].split('/');
      const section = path[2];
      const inPublisher = section === 'planner' || section === 'approvals';
      this.inPublisher.set(inPublisher);
      this.title.set(TITLES[section] ?? 'Home');
      this.wide.set(['home', 'composer', 'ai-studio', 'planner', 'brand', 'settings', 'notifications', 'members'].includes(section) || (section === 'approvals' && !path[3]));
      if (inPublisher) this.publisherOpen.set(true);
      this.navOpen.set(false);
      void this.refresh();
    });
    effect(() => {
      if (this.workspace.orgId()) void this.refresh();
    });
  }

  /** Desktop: collapse to an icon rail (remembered in this browser). Narrow screens: open the drawer. */
  protected toggleNav(): void {
    if (window.innerWidth <= 1024) return this.navOpen.set(!this.navOpen());
    this.rail.set(!this.rail());
    try {
      localStorage.setItem(RAIL_KEY, this.rail() ? '1' : '0');
    } catch {
      // Storage may be unavailable; the choice still applies until reload.
    }
  }

  protected search(event: Event, term: string): void {
    event.preventDefault();
    const q = term.trim();
    if (q) void this.router.navigate([this.ws('/planner')], { queryParams: { q } });
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

  /** Unread badge and Publisher counts. Both are conveniences: pages show their own data and errors. */
  private async refresh(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    try {
      this.unread.set((await firstValueFrom(this.notifications.unreadCount(orgId))).count);
    } catch {
      // ignore
    }
    try {
      const d = await firstValueFrom(this.dashboard.get(orgId));
      const sum = (states: string[]) => states.reduce((n, s) => n + (d.delivery[s] ?? 0), 0);
      this.counts.set({
        approvals: d.pendingReviewCount,
        drafts: d.editorial['DRAFT'] ?? 0,
        scheduled: sum(UPCOMING_DELIVERY),
        // Same as the Planner's Published filter, which includes demo (simulated) sends.
        published: sum(['PUBLISHED', 'SIMULATED']),
        failed: sum(PROBLEM_DELIVERY),
      });
    } catch {
      // ignore
    }
  }
}

function readRail(): boolean {
  try {
    return localStorage.getItem(RAIL_KEY) === '1';
  } catch {
    return false;
  }
}
