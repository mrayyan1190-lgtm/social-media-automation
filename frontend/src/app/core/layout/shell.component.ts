import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { IsActiveMatchOptions, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { PostApiService } from '../../features/content/services/post-api.service';
import { DashboardApiService } from '../../features/dashboard/dashboard-api.service';
import { PROBLEM_DELIVERY, UPCOMING_DELIVERY } from '../../shared/models/states';
import { AuthService } from '../auth/auth.service';
import { NotificationApiService } from '../services/notification-api.service';
import { WorkspaceService } from '../services/workspace.service';

/** Sidebar symbols (the sprite in the template) and their accent tones, as the design. */
type Icon = 'home' | 'wand' | 'composer' | 'send' | 'palette' | 'users' | 'bell' | 'settings' | 'logout';
type Tone = 'sky' | 'violet' | 'peach' | 'mint';

interface NavItem {
  label: string;
  icon: Icon;
  tone: Tone;
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

/**
 * The customer application: the design's tonal sidebar (Main menu, Publisher, Workspace) with live Publisher counts;
 * notifications, settings and the profile sit in its footer. The app is light only. It collapses to an icon rail on wider
 * screens. There is no top bar: each page brings its own heading. On phones a menu button opens the sidebar as a drawer.
 * Navigation follows the workspace role as a convenience only. Internal platform administration is never linked here.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <svg xmlns="http://www.w3.org/2000/svg" class="absolute h-0 w-0 overflow-hidden" aria-hidden="true">
      <symbol id="nav-home" viewBox="0 0 24 24"><path d="m3 10 9-7 9 7v9a2 2 0 0 1-2 2h-4.5v-6h-5v6H5a2 2 0 0 1-2-2z"/></symbol>
      <symbol id="nav-wand" viewBox="0 0 24 24"><path d="m4 20 11-11"/><path d="m14 3 1.3 3.7L19 8l-3.7 1.3L14 13l-1.3-3.7L9 8l3.7-1.3L14 3Z"/><path d="m5 5 .7 1.7L7.5 7.5l-1.8.7L5 10l-.7-1.8-1.8-.7 1.8-.8L5 5Z"/></symbol>
      <symbol id="nav-composer" viewBox="0 0 24 24"><path d="M13 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-7"/><path d="m14.8 12.2 6-6a2.1 2.1 0 0 0-3-3l-6 6-.8 3.8 3.8-.8Z"/></symbol>
      <symbol id="nav-send" viewBox="0 0 24 24"><path d="m21.3 3.1-8 18a.9.9 0 0 1-1.7-.1l-2.7-7.5-7.5-2.7a.9.9 0 0 1-.1-1.7l18-8a1.5 1.5 0 0 1 2 2Z"/><path d="M8.9 13.5 21.3 3.1"/></symbol>
      <symbol id="nav-palette" viewBox="0 0 24 24"><path d="M12 3a9 9 0 1 0 0 18h1.3a2.4 2.4 0 0 0 2.1-3.5A2.4 2.4 0 0 1 17.5 14H19a3 3 0 0 0 3-3 9 9 0 0 0-10-8Z"/><circle cx="7.5" cy="11" r="1"/><circle cx="11" cy="7" r="1"/><circle cx="16" cy="8" r="1"/></symbol>
      <symbol id="nav-users" viewBox="0 0 24 24"><circle cx="9" cy="8" r="3"/><path d="M3 20v-1.5a6 6 0 0 1 12 0V20M17 5.4a3 3 0 0 1 0 5.4M21 20v-1.5a5 5 0 0 0-3.2-4.7"/></symbol>
      <symbol id="nav-bell" viewBox="0 0 24 24"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4"/></symbol>
      <symbol id="nav-settings" viewBox="0 0 24 24"><path d="M12 2.8a2 2 0 0 1 2 1.6l.3 1.1 1.4.8 1.1-.3a2 2 0 0 1 2.3.9l1 1.7a2 2 0 0 1-.4 2.4l-.9.8v1.7l.9.8a2 2 0 0 1 .4 2.4l-1 1.7a2 2 0 0 1-2.3.9l-1.1-.3-1.4.8-.3 1.1a2 2 0 0 1-2 1.6h-2a2 2 0 0 1-2-1.6L7.7 19l-1.4-.8-1.1.3a2 2 0 0 1-2.3-.9l-1-1.7a2 2 0 0 1 .4-2.4l.9-.8v-1.7l-.9-.8a2 2 0 0 1-.4-2.4l1-1.7a2 2 0 0 1 2.3-.9l1.1.3 1.4-.8.3-1.1a2 2 0 0 1 2-1.6h2Z" transform="translate(1 0) scale(.91)"/><circle cx="12" cy="12" r="3"/></symbol>
      <symbol id="nav-logout" viewBox="0 0 24 24"><path d="M10 4H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h5m5-4 4-4-4-4m4 4H9"/></symbol>
      <symbol id="nav-chevron" viewBox="0 0 24 24"><path d="m6 9 6 6 6-6"/></symbol>
      <symbol id="nav-close" viewBox="0 0 24 24"><path d="M18 6 6 18M6 6l12 12"/></symbol>
      <symbol id="nav-menu" viewBox="0 0 24 24"><path d="M4 6h16M4 12h16M4 18h16"/></symbol>
    </svg>
    <div class="shell" [class.sidebar-collapsed]="rail()" [class.mobile-drawer-open]="navOpen()">
      <aside class="side" aria-label="Main workspace navigation">
        <div class="side-top">
          <div class="brand-row flex items-center px-1">
            <div class="brand-logo" [title]="(workspace.current()?.organizationName ?? '') + ' Social Suite'">
              <svg width="22" height="22" viewBox="0 0 28 28" fill="none" aria-hidden="true"><rect x="3" y="3" width="9" height="9" rx="3.1" fill="#F3F5FF"/><rect x="16" y="3" width="9" height="9" rx="3.1" fill="#BBDDF1"/><rect x="3" y="16" width="9" height="9" rx="3.1" fill="#C8B5F4"/><rect x="16" y="16" width="9" height="9" rx="3.1" fill="#EEC4B5"/></svg>
            </div>
            <div class="hide-in-mini min-w-0 flex-1">
              @if (workspace.memberships().length > 1) {
                <select class="brand-select" aria-label="Workspace" [value]="workspace.orgId()" (change)="switchWorkspace($event)">
                  @for (m of workspace.memberships(); track m.organizationId) { <option [value]="m.organizationId">{{ m.organizationName }}</option> }
                </select>
              } @else {
                <div class="brand-name truncate">{{ workspace.current()?.organizationName }}<span>.</span></div>
              }
              <div class="brand-sub">SOCIAL SUITE</div>
            </div>
            <button type="button" class="collapse-control" (click)="toggleRail()" [attr.aria-label]="rail() ? 'Expand sidebar' : 'Collapse sidebar'" [title]="rail() ? 'Expand sidebar' : 'Collapse sidebar'">
              <svg class="h-[14px] w-[14px] fill-none stroke-current stroke-[2]" [style.transform]="rail() ? 'rotate(180deg)' : null" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>
            </button>
            <button type="button" class="close-mobile" aria-label="Close sidebar" (click)="navOpen.set(false)"><svg class="h-4 w-4 fill-none stroke-current stroke-2"><use href="#nav-close"/></svg></button>
          </div>
        </div>
        <div class="divider-dark"></div>

        <nav class="nav-scroller" aria-label="Main navigation">
          <div class="flex flex-col gap-[3px]">
            @for (item of main; track item.link) {
              @if (!item.show || item.show()) {
                <a class="nav-item" [routerLink]="ws(item.link)" routerLinkActive="is-active" [title]="item.label"><span [class]="'nav-icon ' + item.tone"><svg><use [attr.href]="'#nav-' + item.icon"/></svg></span><span class="hide-in-mini flex-1">{{ item.label }}</span></a>
              }
            }
            <button type="button" class="nav-item" [class.publisher-open]="publisherOpen()" [class.is-active]="rail() && inPublisher()" [attr.aria-expanded]="publisherOpen()" aria-controls="publisherChildren" title="Publisher" (click)="togglePublisher()">
              <span class="nav-icon sky"><svg><use href="#nav-send"/></svg></span><span class="hide-in-mini flex-1">Publisher</span>
              <svg class="nav-chevron hide-in-mini fill-none stroke-current stroke-[2]" aria-hidden="true"><use href="#nav-chevron"/></svg>
            </button>
            @if (publisherOpen()) {
              <div id="publisherChildren" class="sublist hide-in-mini flex flex-col gap-[1px]">
                @for (item of publisher; track item.label) {
                  @if (!item.show || item.show()) {
                    <a class="sub-item" [routerLink]="ws(item.link)" [queryParams]="item.query ?? null" routerLinkActive="is-active" [routerLinkActiveOptions]="exact" [title]="item.label">
                      <span class="flex-1 truncate">{{ item.label }}</span>
                      @if (item.count && item.count() > 0) { <span [class]="'pill-count ' + (item.tone ?? '')">{{ item.count() }}</span> }
                    </a>
                  }
                }
              </div>
            }
          </div>

          @if (workspace.isAdmin()) {
            <div class="divider-dark"></div>
            <p class="group-title hide-in-mini">WORKSPACE</p>
            <div class="flex flex-col gap-[3px]">
              <a class="nav-item" [routerLink]="ws('/brand')" routerLinkActive="is-active" title="Brand"><span class="nav-icon peach"><svg><use href="#nav-palette"/></svg></span><span class="hide-in-mini flex-1">Brand</span></a>
              <a class="nav-item" [routerLink]="ws('/members')" routerLinkActive="is-active" title="Members"><span class="nav-icon violet"><svg><use href="#nav-users"/></svg></span><span class="hide-in-mini flex-1">Members</span></a>
            </div>
          }
        </nav>

        <div class="bottom-area">
          <a class="nav-item" [routerLink]="ws('/notifications')" routerLinkActive="is-active" title="Notifications" [attr.aria-label]="'Notifications' + (unread() ? ', ' + unread() + ' unread' : '')">
            <span class="nav-icon peach"><svg><use href="#nav-bell"/></svg></span><span class="hide-in-mini flex-1">Notifications</span>
            @if (unread() > 0) { <span class="pill-count rose hide-in-mini">{{ unread() }}</span><span class="unread-dot"></span> }
          </a>
          @if (workspace.isAdmin()) {
            <a class="nav-item" [routerLink]="ws('/settings/workspace')" routerLinkActive="is-active" title="Settings"><span class="nav-icon sky"><svg><use href="#nav-settings"/></svg></span><span class="hide-in-mini flex-1">Settings</span></a>
          }
          <a class="side-profile" [routerLink]="ws('/profile')" title="Profile">
            <span class="avatar">{{ meInitials() }}</span>
            <span class="hide-in-mini min-w-0 flex-1"><span class="pname truncate">{{ auth.me()?.displayName || 'Profile' }}</span><span class="prole truncate">{{ workspace.current()?.role }} · {{ workspace.current()?.organizationName }}</span></span>
            <span class="status-dot hide-in-mini"></span>
          </a>
          <button type="button" class="logout" title="Sign out" (click)="signOut()"><svg><use href="#nav-logout"/></svg><span class="hide-in-mini">Sign out</span></button>
        </div>
      </aside>

      <div class="main">
        <!-- No top bar: phones get just this button to open the sidebar drawer. -->
        <button type="button" class="tgl" aria-label="Open navigation" title="Open navigation" (click)="navOpen.set(true)"><svg class="h-5 w-5 fill-none stroke-current stroke-2"><use href="#nav-menu"/></svg></button>
        <main class="page" [class.wide]="wide()">
          @if (workspace.current()) {
            <router-outlet />
          } @else {
            <p>You are not an active member of any workspace yet.</p>
          }
        </main>
      </div>
      <div class="menu-overlay theme-scrim" (click)="navOpen.set(false)"></div>
    </div>
  `,
  // The design's sidebar (tonal sky → lavender → peach), merged from its three style blocks into the final values.
  styles: [`
    :host{display:block}
    .shell{--side-width:200px;display:flex;min-height:100vh}
    .side{position:sticky;top:0;z-index:60;flex:none;display:flex;flex-direction:column;width:var(--side-width);height:100dvh;overflow:hidden;
      font-family:'DM Sans',ui-sans-serif,system-ui,sans-serif;color:#F2F6FF;-webkit-font-smoothing:antialiased;
      background:radial-gradient(ellipse 130% 32% at -40% -4%,rgba(170,229,255,.11),transparent 74%),radial-gradient(ellipse 110% 41% at 120% 47%,rgba(203,178,255,.10),transparent 77%),
        radial-gradient(ellipse 112% 37% at -20% 105%,rgba(255,191,172,.10),transparent 82%),linear-gradient(172deg,#193B53 0%,#254059 26%,#332F50 53%,#482F4D 77%,#54343F 100%);
      border-right:1px solid rgba(234,225,247,.18);box-shadow:8px 0 35px rgba(37,34,73,.27);transition:width .25s cubic-bezier(.2,.8,.2,1),transform .25s ease}
    .side::after{content:'';position:absolute;inset:0;pointer-events:none;background:linear-gradient(90deg,rgba(255,255,255,.045),transparent 56%)}
    /* The legacy global button/link styles must not leak into the sidebar. */
    .side button,.side select{font-family:inherit;margin:0;min-height:0;cursor:pointer}
    .side a{text-decoration:none}
    .side button:focus-visible,.side a:focus-visible{outline:2px solid #9CC6FF;outline-offset:2px}
    .side svg{display:block}

    .side-top{position:relative;z-index:1;padding:14px 9px 8px}
    .brand-row{gap:7px}
    .brand-logo{width:33px;height:33px;flex:none;display:grid;place-items:center;border-radius:11px;color:#fff;background:linear-gradient(140deg,#A8DEFA 0%,#ACA9E6 53%,#EAAEAF 100%);
      border:1px solid rgba(255,255,255,.65);box-shadow:0 7px 22px rgba(35,49,88,.25),inset 0 1px 0 rgba(255,255,255,.6)}
    .brand-name{font-size:14.5px;letter-spacing:-.5px;color:#fff;line-height:1.15;font-weight:800}
    .brand-name span{color:#EEC7D4}
    .brand-select{display:block;max-width:100%;padding:0;border:0;background:transparent;color:#fff;font-size:14.5px;font-weight:800;letter-spacing:-.5px;line-height:1.15}
    .brand-select option{color:#203653}
    .brand-sub{color:#D0DEED;font-size:8.5px;letter-spacing:1.45px;font-weight:800;line-height:1.7}
    .collapse-control{width:21px;height:22px;flex:none;display:grid;place-items:center;padding:0;border:1px solid rgba(225,228,255,.32);border-radius:7px;color:#E5E7FF;background:rgba(255,255,255,.11);transition:background .2s,transform .2s}
    .collapse-control:hover{background:rgba(255,255,255,.21);color:#fff}
    .collapse-control svg{transition:transform .2s}
    .close-mobile{display:none;width:28px;height:28px;flex:none;place-items:center;padding:0;border:0;border-radius:8px;background:rgba(255,255,255,.1);color:#d5e4fb}

    .nav-scroller{position:relative;z-index:1;flex:1;min-height:0;overflow-y:auto;overflow-x:hidden;padding:0 7px 6px;scrollbar-color:rgba(216,202,242,.34) transparent;scrollbar-width:thin}
    .nav-scroller::-webkit-scrollbar{width:3px}
    .nav-scroller::-webkit-scrollbar-thumb{background:rgba(216,202,242,.3);border-radius:6px}
    .divider-dark{position:relative;z-index:1;height:1px;margin:8px 5px;background:linear-gradient(90deg,transparent,rgba(242,229,255,.31) 20%,rgba(255,223,211,.30) 80%,transparent)}
    .group-title{color:#D6D3EC;font-size:10px;font-weight:800;letter-spacing:1.3px;padding-left:10px;margin:9px 0 5px;white-space:nowrap}

    .nav-item{position:relative;width:100%;min-height:35px;display:flex;align-items:center;justify-content:flex-start;gap:7px;padding:3px 7px;border:1px solid transparent;border-radius:10px;
      color:#E2E6F4;background:transparent;font-size:13.5px;font-weight:650;letter-spacing:-.12px;white-space:nowrap;text-align:left;
      transition:background .18s,color .18s,border-color .18s,transform .18s}
    .nav-item:hover{background:rgba(246,241,255,.125);border-color:rgba(255,255,255,.13);color:#fff;transform:translateX(1px)}
    .nav-item.is-active{background:linear-gradient(105deg,rgba(209,231,251,.22),rgba(204,193,241,.25) 58%,rgba(253,204,193,.18));border-color:rgba(249,241,255,.28);color:#fff;
      box-shadow:inset 0 1px 0 rgba(255,255,255,.14),0 7px 20px rgba(32,37,70,.12)}
    .nav-item.is-active::before{content:'';position:absolute;left:-7px;top:8px;bottom:8px;width:3px;border-radius:0 5px 5px 0;background:linear-gradient(#B4ECFF,#C8B5FF,#FFC4A9);box-shadow:0 0 10px #e4c9f9a0}
    .nav-icon{height:26px;width:26px;flex:none;display:grid;place-items:center;border-radius:9px;border:1px solid rgba(255,255,255,.08);background:rgba(255,255,255,.065);transition:all .2s ease}
    .nav-icon svg{height:17px;width:17px;fill:none;stroke:currentColor;stroke-width:1.85;stroke-linecap:round;stroke-linejoin:round;filter:drop-shadow(0 1px 3px #15121c28)}
    .nav-icon.sky{color:#C6EBFF;background:rgba(174,222,247,.15);border-color:rgba(179,229,255,.14)}
    .nav-icon.violet{color:#E0D2FF;background:rgba(209,183,248,.16);border-color:rgba(219,198,255,.13)}
    .nav-icon.peach{color:#FFD1BF;background:rgba(255,194,177,.15);border-color:rgba(255,211,197,.13)}
    .nav-icon.mint{color:#B5EAD9;background:rgba(169,239,212,.12);border-color:rgba(177,239,210,.10)}
    .nav-item.is-active .nav-icon{background:rgba(234,235,255,.19);border-color:rgba(255,255,255,.28);color:#fff;box-shadow:0 0 14px rgba(215,196,253,.12)}
    .nav-item:hover .nav-icon{filter:brightness(1.15)}
    .nav-chevron{width:12px;height:12px;flex:none;opacity:.65;color:#DFDAEE;transition:transform .2s}
    .publisher-open .nav-chevron{transform:rotate(180deg)}

    .sublist{position:relative;padding:2px 0 3px 10px;margin:1px 0 2px 19px}
    .sublist::before{content:'';position:absolute;left:3px;top:2px;bottom:4px;width:1px;background:linear-gradient(rgba(191,225,252,.55),rgba(243,198,215,.55))}
    .sub-item{position:relative;min-height:25px;width:100%;display:flex;align-items:center;gap:4px;padding:3px 5px 3px 12px;border:1px solid transparent;border-radius:7px;
      color:#DEE0F0;font-size:12.8px;font-weight:600;white-space:nowrap;transition:background .18s,color .18s}
    .sub-item::before{content:'';position:absolute;left:0;width:5px;height:5px;border-radius:50%;background:#BAC9DB;border:1px solid #506077}
    .sub-item:hover{background:rgba(255,255,255,.11);color:#fff}
    .sub-item:hover::before{background:#FFE0CE}
    .sub-item.is-active{background:linear-gradient(104deg,rgba(221,233,255,.25),rgba(214,190,255,.21) 57%,rgba(255,202,184,.17));border-color:rgba(255,255,255,.31);color:#fff;font-weight:800;box-shadow:0 4px 12px rgba(51,53,84,.12)}
    .sub-item.is-active::before{width:7px;height:7px;background:#DCC5FF;border-color:#8976A7;box-shadow:0 0 8px rgba(224,192,255,.75)}
    .pill-count{display:inline-flex;align-items:center;justify-content:center;min-width:19px;height:18px;padding:0 5px;margin-left:auto;border-radius:7px;font-size:10.5px;font-weight:800;background:rgba(255,255,255,.14);color:#EAF0FF}
    .pill-count.warn{background:#453a68;color:#d5cbff}
    .pill-count.rose{background:#593646;color:#ffb8c4}

    .bottom-area{position:relative;z-index:1;flex:none;padding:8px 7px 9px;border-top:1px solid rgba(255,234,235,.24);background:linear-gradient(160deg,rgba(34,36,72,.32),rgba(74,35,53,.42))}
    .bottom-area .nav-item{min-height:32px;margin-bottom:1px}
    .bottom-area .nav-icon{height:24px;width:24px}
    .bottom-area .nav-icon svg{width:16px;height:16px}
    .unread-dot{display:none}
    .side-profile{margin-top:6px;padding:7px;min-height:44px;display:flex;gap:7px;align-items:center;border:1px solid rgba(247,231,255,.26);border-radius:10px;color:#fff;
      background:linear-gradient(120deg,rgba(205,214,246,.19),rgba(239,190,200,.18));box-shadow:0 4px 16px rgba(59,46,84,.14);transition:border-color .2s}
    .side-profile:hover{border-color:rgba(255,255,255,.4)}
    .pname{display:block;font-size:12.5px;font-weight:800;color:#fff}
    .prole{display:block;font-size:10.5px;color:#DFD7E7}
    .avatar{width:27px;height:27px;flex:none;display:grid;place-items:center;border-radius:9px;font-size:12px;font-weight:800;color:#2F3D5B;
      background:linear-gradient(140deg,#A7DDF7,#C5AFE8 56%,#F3B5AE);box-shadow:0 5px 14px rgba(54,52,100,.25)}
    .status-dot{height:7px;width:7px;flex:none;border-radius:50%;background:#A9F0CF;box-shadow:0 0 0 3px rgba(168,232,202,.22)}
    .logout{padding:7px 8px 3px;display:flex;align-items:center;justify-content:flex-start;gap:8px;width:100%;border:0;background:none;color:#FFD7CA;font-size:12.5px;font-weight:700;text-align:left;transition:color .2s}
    .logout:hover{color:#fff}
    .logout svg{width:15px;height:15px;fill:none;stroke:currentColor;stroke-width:1.8;stroke-linecap:round;stroke-linejoin:round}

    .main{flex:1;min-width:0}
    .tgl{display:none;position:fixed;top:12px;left:12px;z-index:65;width:40px;height:40px;min-height:0;padding:0;place-items:center;border-radius:12px;border:1px solid #fff;
      background:rgba(255,255,255,.85);color:#24476c;box-shadow:0 1px 3px rgba(0,0,0,.1),0 8px 22px -12px rgba(15,28,51,.45);cursor:pointer}
    .menu-overlay{display:none;position:fixed;inset:0;z-index:70;background:#0c1525a8;backdrop-filter:blur(2px)}

    /* Icon rail (desktop and tablet only); the choice is remembered in this browser. */
    @media (min-width:768px){
      .sidebar-collapsed{--side-width:62px}
      .sidebar-collapsed .hide-in-mini{display:none!important}
      .sidebar-collapsed .side-top{padding:14px 8px 38px}
      .sidebar-collapsed .collapse-control{position:absolute;left:20px;top:60px}
      .sidebar-collapsed .brand-row{justify-content:center;gap:0}
      .sidebar-collapsed .brand-logo{width:37px;height:37px}
      .sidebar-collapsed .nav-scroller{padding-inline:7px}
      .sidebar-collapsed .nav-item{justify-content:center;padding:3px 0;min-height:39px}
      .sidebar-collapsed .nav-icon{height:30px;width:32px}
      .sidebar-collapsed .bottom-area{padding-inline:7px}
      .sidebar-collapsed .bottom-area .nav-item{min-height:37px}
      .sidebar-collapsed .side-profile{justify-content:center;padding-inline:0}
      .sidebar-collapsed .logout{justify-content:center;padding-inline:0}
      .sidebar-collapsed .unread-dot{display:block;position:absolute;top:7px;right:12px;width:7px;height:7px;border-radius:50%;background:#FFD0BE;box-shadow:0 0 0 3px rgba(255,214,202,.16)}
    }
    @media (max-height:720px) and (min-width:768px){
      .side-top{padding-top:9px}.group-title{margin-top:5px;margin-bottom:3px}.nav-item{min-height:30px}.sub-item{min-height:23px}.divider-dark{margin:5px 4px}
      .bottom-area{padding-top:3px;padding-bottom:4px}.side-profile{min-height:39px;padding:5px}.logout{padding-top:4px}
    }
    /* Phones: the sidebar is a drawer opened from the floating menu button. */
    @media (max-width:767px){
      .side{position:fixed;inset:0 auto 0 0;width:215px;transform:translateX(-105%);box-shadow:16px 0 45px rgba(69,60,110,.26)}
      .mobile-drawer-open .side{transform:translateX(0)}
      .mobile-drawer-open .menu-overlay{display:block}
      .collapse-control{display:none}
      .close-mobile{display:grid}
      .tgl{display:grid}
      .mobile-drawer-open .tgl{display:none}
      /* Room for the floating menu button above each page's own heading. */
      .page{padding-top:58px}
    }
    @media (prefers-reduced-motion:reduce){.side,.side *{transition:none!important}}
  `],
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationApiService);
  private readonly dashboard = inject(DashboardApiService);
  private readonly postsApi = inject(PostApiService);

  protected readonly exact = EXACT;
  /** Publisher counts (refreshed on navigation): the whole workspace for Admins, the person's own posts for everyone else. */
  private readonly counts = signal({ approvals: 0, drafts: 0, scheduled: 0, published: 0, failed: 0, changes: 0, inReview: 0 });

  protected readonly main: NavItem[] = [
    { label: 'Home', icon: 'home', tone: 'sky', link: '/home' },
    { label: 'AI Studio', icon: 'wand', tone: 'violet', link: '/ai-studio', show: () => this.workspace.canManageContent() },
    { label: 'Composer', icon: 'composer', tone: 'peach', link: '/composer', show: () => this.workspace.canManageContent() },
  ];
  /**
   * Admins run publishing (approve, schedule, fix failures). A Creator only follows their own posts through review:
   * what to fix, what is waiting, what was accepted and what went out; publishing problems are the Admin's to fix.
   */
  protected readonly publisher: SubItem[] = [
    { label: 'Planner', link: '/planner' },
    { label: 'All Posts', link: '/planner', query: { view: 'list' } },
    { label: 'Approvals', link: '/approvals', count: () => this.counts().approvals, tone: 'warn', show: () => this.workspace.isAdmin() },
    { label: 'Drafts', link: '/planner', query: { status: 'Draft' }, count: () => this.counts().drafts },
    { label: 'Changes requested', link: '/planner', query: { status: 'Changes Requested' }, count: () => this.counts().changes, tone: 'warn',
      show: () => !this.workspace.isAdmin() },
    { label: 'In review', link: '/planner', query: { status: 'In Review' }, count: () => this.counts().inReview, show: () => !this.workspace.isAdmin() },
    { label: 'Scheduled', link: '/planner', query: { status: 'Scheduled' }, count: () => this.counts().scheduled },
    { label: 'Published', link: '/planner', query: { status: 'Published' }, count: () => this.counts().published },
    { label: 'Failed', link: '/planner', query: { status: 'Failed' }, count: () => this.counts().failed, tone: 'rose', show: () => this.workspace.isAdmin() },
  ];

  protected readonly publisherOpen = signal(true);
  protected readonly inPublisher = signal(false);
  protected readonly navOpen = signal(false);
  /** Desktop icon rail (the design's collapse button), remembered in this browser. */
  protected readonly rail = signal(readRail());
  protected readonly unread = signal(0);
  /** Home, Composer, AI Studio, Planner, Brand, Settings, Notifications, Members and the Approvals list use the full width beside the sidebar. */
  protected readonly wide = signal(false);
  protected readonly meInitials = computed(() =>
    (this.auth.me()?.displayName ?? '?').split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?');

  /** App path inside the current workspace: /home -> /{slug}/home. */
  protected ws(path: string): string {
    return `/${this.workspace.slug()}${path}`;
  }

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe((e) => {
      const path = (e as NavigationEnd).urlAfterRedirects.split('?')[0].split('/');
      const section = path[2];
      const inPublisher = section === 'planner' || section === 'approvals';
      this.inPublisher.set(inPublisher);
      this.wide.set(['home', 'composer', 'ai-studio', 'planner', 'brand', 'settings', 'notifications', 'members', 'approvals'].includes(section));
      if (inPublisher) this.publisherOpen.set(true);
      this.navOpen.set(false);
      void this.refresh();
    });
    effect(() => {
      if (this.workspace.orgId()) void this.refresh();
    });
    // The app has no dark mode: pin light while it is open, and give the landing page back its own choice on leaving.
    const root = document.documentElement;
    const previous = root.dataset['theme'];
    root.dataset['theme'] = 'light';
    inject(DestroyRef).onDestroy(() => (previous ? (root.dataset['theme'] = previous) : delete root.dataset['theme']));
  }

  protected toggleRail(): void {
    this.rail.set(!this.rail());
    try {
      localStorage.setItem(RAIL_KEY, this.rail() ? '1' : '0');
    } catch {
      // Storage may be unavailable; the choice still applies until reload.
    }
  }

  /** In the icon rail the Publisher entry expands the sidebar to show its shortcuts (as the design). */
  protected togglePublisher(): void {
    if (this.rail() && window.innerWidth >= 768) {
      this.toggleRail();
      this.publisherOpen.set(true);
      return;
    }
    this.publisherOpen.set(!this.publisherOpen());
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
    if (!this.workspace.isAdmin()) {
      try {
        // Same scope as a Creator's Planner: only posts they created, counted by each post's latest state.
        const me = this.auth.me()?.userId;
        const mine = (await firstValueFrom(this.postsApi.listAll(orgId))).filter((p) => p.createdByUserId === me);
        const latest = (states: string[]) => mine.filter((p) => !!p.latestDeliveryState && states.includes(p.latestDeliveryState)).length;
        this.counts.set({
          approvals: 0,
          drafts: mine.filter((p) => p.editorialState === 'DRAFT').length,
          scheduled: latest(UPCOMING_DELIVERY),
          published: latest(['PUBLISHED', 'SIMULATED']),
          failed: latest(PROBLEM_DELIVERY),
          changes: mine.filter((p) => p.editorialState === 'CHANGES_REQUESTED').length,
          inReview: mine.filter((p) => p.editorialState === 'IN_REVIEW').length,
        });
      } catch {
        // ignore
      }
      return;
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
        changes: 0, inReview: 0,
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

/** Planner status filter covering both stages after approval (one Creator menu item). */
