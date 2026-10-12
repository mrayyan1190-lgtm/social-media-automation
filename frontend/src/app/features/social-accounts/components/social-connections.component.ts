import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../../shared/models/platform.model';
import { SocialAccount, SocialConnection } from '../models/social-account.model';
import { SocialAccountApiService } from '../services/social-account-api.service';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';

type PlatformStatus = 'connected' | 'reconnect' | 'none';

interface PlatformCard {
  platform: SocialPlatform;
  status: PlatformStatus;
  /** Connected page/profile/account names. */
  names: string[];
}

/**
 * LinkedIn, Facebook and Instagram with plain-language status and Connect / Reconnect / Disconnect: compact cards in
 * onboarding, the full Social accounts panel in Settings. Connect always goes through the provider sign-in; when the server has no app credentials for a
 * platform it answers with an error that is shown here. Provider modes, scopes and tokens are never shown.
 */
@Component({
  selector: 'app-social-connections',
  imports: [ProblemBannerComponent],
  host: { '(document:keydown.escape)': 'confirming.set(null)' },
  // The Settings card design (the onboarding cards use global onboarding styles). Component-scoped.
  styles: `
    :host { display: flex; flex-direction: column; min-width: 0; }
    button:not(:disabled) { cursor: pointer; }
    .display-font { font-family: Manrope, "DM Sans", system-ui, sans-serif; }
    .glass-panel { background: rgba(255,255,255,.86); border: 1px solid rgba(255,255,255,.98); box-shadow: 0 17px 48px rgba(60,85,126,.075), 0 3px 8px rgba(60,85,126,.035); backdrop-filter: blur(20px); }
    .panel-head { min-height: 77px; }
    .float-shape { position: absolute; pointer-events: none; border: 1px solid rgba(255,255,255,.62); border-radius: 50%; transform: rotate(-12deg); }
    .account-card { border: 1px solid #E2EAF4; background: rgba(251,253,255,.96); border-radius: 19px; padding: 15px 16px; transition: transform .2s, box-shadow .2s, border-color .2s; }
    .account-card button svg { display: block !important; width: 14px !important; height: 14px !important; flex: 0 0 14px !important; }
    .account-card button { min-width: 104px; max-width: 124px; }
    .account-card:hover { transform: translateY(-2px); border-color: #C8D9EC; box-shadow: 0 10px 26px rgba(92,117,153,.095); }
    .account-card.connected { background: linear-gradient(112deg, #FBFEFF 0%, #F2FAF7 100%); border-color: #CBE7D9; }
    .account-card.reconnect { border-color: #F3D3D9; }
    .primary-button { color: #fff; background: linear-gradient(105deg, #193D64 0%, #346BA3 62%, #6B75C0 100%); box-shadow: 0 9px 18px rgba(42,79,133,.20); transition: transform .18s, box-shadow .18s, filter .18s; }
    .primary-button:hover:not(:disabled) { transform: translateY(-2px); box-shadow: 0 12px 24px rgba(42,79,133,.29); filter: saturate(1.12); }
    .secondary-button { border: 1px solid #DCE6F2; background: #fff; color: #566F90; transition: background .18s, border-color .18s, transform .18s; }
    .secondary-button:hover:not(:disabled) { background: #F1F6FD; border-color: #BED3EA; transform: translateY(-1px); }
    .primary-button:disabled, .secondary-button:disabled { opacity: .55; cursor: default; }
    .problem { color: #B65165; background: linear-gradient(105deg, #FFF0F1, #FFF8F7); border: 1px solid #F5D8DC; }
    button:focus-visible { outline: 3px solid #93BEEB; outline-offset: 3px; }
    @media (max-width: 680px) { .account-card { padding: 13px; } .glass-panel { backdrop-filter: blur(10px); } }
    @media (prefers-reduced-motion: reduce) { *, *::before, *::after { transition-duration: .01ms !important; } }
  `,
  template: `
    @if (compact()) {
      <app-problem-banner [problem]="problem()" />
      <!-- Onboarding: the design's three cards, Connect or Connected. Settings keeps the detailed status below. -->
      <div class="grid grid-cols-1 gap-3 min-[500px]:grid-cols-3" id="socialCards">
        @for (card of cards(); track card.platform) {
          <article class="social-card flex min-h-[148px] flex-col items-center rounded-[19px] border border-[#dce4ee] bg-white/85 px-2.5 py-3 text-center" [attr.data-network]="card.platform.toLowerCase()" [attr.data-connected]="card.status === 'connected'">
            @switch (card.platform) {
              @case ('LinkedIn') { <span class="grid h-10 w-10 place-items-center rounded-[15px] bg-[#e8f1fb] font-display text-[22px] font-extrabold leading-none text-[#0a66c2] shadow-[0_5px_10px_rgba(10,102,194,.08)]" aria-hidden="true">in</span> }
              @case ('Facebook') { <span class="grid h-10 w-10 place-items-center rounded-[15px] bg-[#e7f0fe] font-display text-[22px] font-extrabold leading-none text-[#1877f2] shadow-[0_5px_10px_rgba(24,119,242,.08)]" aria-hidden="true">f</span> }
              @case ('Instagram') { <span class="grid h-10 w-10 place-items-center rounded-[15px] bg-[#fff0e6] text-[#f07f2d] shadow-[0_5px_10px_rgba(240,127,45,.1)]" aria-hidden="true"><svg viewBox="0 0 24 24" class="h-6 w-6 fill-none stroke-current stroke-[2]"><rect x="3.4" y="3.4" width="17.2" height="17.2" rx="5"/><circle cx="12" cy="12" r="3.9"/><circle cx="17.4" cy="6.9" r="1" fill="currentColor" stroke="none"/></svg></span> }
            }
            <p class="mt-2 font-display text-[13px] font-bold text-[#203a58]">{{ card.platform }}</p>
            @if (card.status === 'connected') {
              <!-- Disconnecting lives in Settings; here Connected is a status. -->
              <button type="button" disabled class="connect-btn mt-auto inline-flex h-[40px] w-full items-center justify-center gap-1.5 rounded-[10px] bg-[#e6f4eb] px-2 text-[12px] font-bold text-[#3b8363]" aria-pressed="true"><svg viewBox="0 0 24 24" class="h-4 w-4 fill-none stroke-current stroke-2.2" aria-hidden="true"><path d="m4 12 5 5L20 6"/></svg> Connected</button>
            } @else {
              <button type="button" (click)="connect(card)" [disabled]="busy()" class="connect-btn mt-auto inline-flex h-[40px] w-full items-center justify-center rounded-[10px] bg-[#183354] px-2 text-[12px] font-bold text-white transition hover:bg-[#27496e]" aria-pressed="false">{{ card.status === 'reconnect' ? 'Reconnect' : 'Connect' }}</button>
            }
          </article>
        }
      </div>
    } @else {
      <!-- Settings: the design's account cards. Connect always goes through the provider sign-in; tokens never reach the browser. -->
      <section class="glass-panel flex min-w-0 flex-1 flex-col overflow-hidden rounded-[27px]" aria-labelledby="channelsTitle">
        <div class="panel-head relative overflow-hidden border-b border-white/80 bg-[linear-gradient(110deg,#E7EDFC_0%,#EDE8F9_54%,#F7E6E2_100%)] px-5 py-4 sm:px-6">
          <div class="float-shape -right-[48px] -top-[80px] h-[215px] w-[215px]" aria-hidden="true"></div>
          <div class="relative flex items-center justify-between gap-3"><div class="flex min-w-0 items-center gap-3"><span class="grid h-10 w-10 shrink-0 place-items-center rounded-[14px] bg-white/85 text-[#8B80BE] shadow-sm"><svg class="h-5 w-5 fill-none stroke-current stroke-[1.75]"><use href="#ic-link"/></svg></span><div><h2 id="channelsTitle" class="display-font text-[17px] font-extrabold tracking-[-.04em] text-[#23415F] sm:text-[19px]">Social accounts</h2><p class="mt-0.5 text-[11px] font-medium text-[#7C8DAC]">Connect your publishing destinations</p></div></div><span class="shrink-0 rounded-full border border-white bg-white/70 px-3 py-1.5 text-[10px] font-extrabold text-[#627D9B]">{{ connectedCount() }} / {{ cards().length }} active</span></div>
        </div>
        <div class="flex flex-col gap-3 px-4 pb-4 pt-4 sm:px-5 sm:pb-5">
          @if (problem(); as p) { <p class="problem rounded-[15px] px-4 py-3 text-[12px] font-bold leading-[1.5]" role="alert">{{ p.detail }}</p> }
          @for (card of cards(); track card.platform) {
            <div class="account-card" [class.connected]="card.status === 'connected'" [class.reconnect]="card.status === 'reconnect'">
              <div class="flex min-w-0 items-center gap-3">
                @switch (card.platform) {
                  @case ('LinkedIn') { <span class="grid h-11 w-11 shrink-0 place-items-center rounded-[15px] bg-[#116FB7] text-[13px] font-extrabold text-white shadow-[0_7px_14px_rgba(64,108,158,.13)]" aria-hidden="true">in</span> }
                  @case ('Facebook') { <span class="grid h-11 w-11 shrink-0 place-items-center rounded-[15px] bg-[#257CE1] text-[19px] font-extrabold text-white shadow-[0_7px_14px_rgba(64,108,158,.13)]" aria-hidden="true">f</span> }
                  @default { <span class="grid h-11 w-11 shrink-0 place-items-center rounded-[15px] bg-[linear-gradient(140deg,#956FC6,#D36BA4,#E8A5A0)] text-white shadow-[0_7px_14px_rgba(64,108,158,.13)]" aria-hidden="true"><svg viewBox="0 0 24 24" class="h-[22px] w-[22px]" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><rect x="3.4" y="3.4" width="17.2" height="17.2" rx="5"/><circle cx="12" cy="12" r="3.9"/><circle cx="17.4" cy="6.9" r="1" fill="currentColor" stroke="none"/></svg></span> }
                }
                <div class="min-w-0 flex-1">
                  <div class="flex flex-wrap items-center gap-2"><h3 class="text-[13px] font-extrabold text-[#2B4868]">{{ card.platform }}</h3>
                    @switch (card.status) {
                      @case ('connected') { <span class="inline-flex items-center gap-1 rounded-full bg-[#E2F6EB] px-2 py-1 text-[9px] font-extrabold text-[#4C9975]"><i class="h-[6px] w-[6px] rounded-full bg-[#50B989]"></i>Connected</span> }
                      @case ('reconnect') { <span class="inline-flex items-center gap-1 rounded-full bg-[#FFF0F1] px-2 py-1 text-[9px] font-extrabold text-[#C06A76]"><i class="h-[6px] w-[6px] rounded-full bg-[#D87B84]"></i>Needs reconnection</span> }
                      @default { <span class="inline-flex items-center gap-1 rounded-full bg-[#EDF1F7] px-2 py-1 text-[9px] font-extrabold text-[#95A6BA]"><i class="h-[6px] w-[6px] rounded-full bg-[#A9B9CA]"></i>Not connected</span> }
                    }
                  </div>
                  <p class="mt-1 truncate text-[10px] font-medium text-[#8CA0B5]" [title]="card.names.join(', ')">{{ card.names.length ? card.names.join(', ') + ' · ' + kinds[card.platform] : settingsSubtitles[card.platform] }}</p>
                </div>
                @if (canManage()) {
                  @switch (card.status) {
                    @case ('connected') {
                      @if (allowDisconnect()) {
                        <button type="button" (click)="confirming.set(card.platform)" [disabled]="busy()" class="secondary-button inline-flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-full px-3.5 text-[11px] font-extrabold sm:px-4"><svg class="h-3.5 w-3.5 fill-none stroke-current stroke-[2]"><use href="#ic-x"/></svg>Disconnect</button>
                      }
                    }
                    @case ('reconnect') {
                      <button type="button" (click)="connect(card)" [disabled]="busy()" class="primary-button inline-flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-full px-3.5 text-[11px] font-extrabold sm:px-4"><svg class="h-3.5 w-3.5 fill-none stroke-current stroke-[2]"><use href="#ic-refresh"/></svg>Reconnect</button>
                    }
                    @default {
                      <button type="button" (click)="connect(card)" [disabled]="busy()" class="primary-button inline-flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-full px-3.5 text-[11px] font-extrabold sm:px-4"><svg class="h-3.5 w-3.5 fill-none stroke-current stroke-[2]"><use href="#ic-plus"/></svg>Connect</button>
                    }
                  }
                }
              </div>
            </div>
          }
        </div>
        <!-- mt-auto keeps this note on the card's bottom edge when the column is stretched to match its neighbour. -->
        <div class="mx-4 mb-4 mt-auto flex items-start gap-2 rounded-[15px] border border-[#E6E9F7] bg-[linear-gradient(110deg,#F5F8FF,#F9F6FC)] p-3 sm:mx-5 sm:mb-5"><svg class="mt-[2px] h-[15px] w-[15px] shrink-0 fill-none stroke-[#8598BF] stroke-[1.7]"><use href="#ic-shield"/></svg><p class="text-[10.5px] font-medium leading-[1.55] text-[#8292AA]">Only connected destinations can be selected for publishing. @if (canManage()) { <strong class="font-extrabold text-[#637E9C]">Connect</strong> opens the platform's own sign-in. } @else { Ask a workspace Admin to connect or reconnect accounts. }</p></div>
      </section>

      @if (confirming(); as platform) {
        <div class="fixed inset-0 z-[100] flex items-center justify-center theme-scrim p-4" role="dialog" aria-modal="true" aria-labelledby="disconnectTitle" (click)="$event.target === $event.currentTarget ? confirming.set(null) : null">
          <div class="glass-panel w-full max-w-[410px] rounded-[25px] p-6 shadow-[0_35px_75px_#1b395655]">
            <div class="mb-4 grid h-12 w-12 place-items-center rounded-[16px] bg-[#FFF0ED] text-[#CD8B83]"><svg class="h-6 w-6 fill-none stroke-current stroke-2"><use href="#ic-link"/></svg></div>
            <h2 id="disconnectTitle" class="display-font text-[21px] font-extrabold tracking-[-.04em] text-[#23415F]">Disconnect account?</h2>
            <p class="mt-2 text-[12px] leading-[1.7] text-[#7F93AB]">Posts can no longer be published to <strong class="text-[#41607F]">{{ platform }}</strong> until you connect it again.</p>
            <div class="mt-6 flex justify-end gap-2"><button type="button" (click)="confirming.set(null)" class="secondary-button h-10 rounded-full px-5 text-[12px] font-bold">Cancel</button><button type="button" (click)="disconnect(platform)" [disabled]="busy()" class="h-10 rounded-full bg-[#CA6074] px-5 text-[12px] font-extrabold text-white transition hover:bg-[#B34E64]">{{ busy() ? 'Disconnecting…' : 'Disconnect' }}</button></div>
          </div>
        </div>
      }
    }
  `,
})
export class SocialConnectionsComponent {
  private readonly api = inject(SocialAccountApiService);

  readonly orgId = input.required<string>();
  readonly allowDisconnect = input(false);
  /** Workspace Admins connect and disconnect; everyone else only sees the status. */
  readonly canManage = input(true);
  /** Onboarding cards: Connect / Connected ✓ only. */
  readonly compact = input(false);
  /** Whether at least one platform has a real provider connection. */
  readonly connectedChange = output<boolean>();
  /** Short confirmation for the page's toast, e.g. after a disconnect. */
  readonly notice = output<string>();

  protected readonly settingsSubtitles: Record<SocialPlatform, string> = { LinkedIn: 'Pages & profiles', Facebook: 'Facebook Pages', Instagram: 'Business accounts' };
  protected readonly kinds: Record<SocialPlatform, string> = { LinkedIn: 'LinkedIn', Facebook: 'Facebook Page', Instagram: 'Instagram account' };
  /** The platform waiting for the Disconnect confirmation. */
  protected readonly confirming = signal<SocialPlatform | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  private readonly accounts = signal<SocialAccount[]>([]);
  private readonly connections = signal<SocialConnection[]>([]);

  protected readonly cards = computed<PlatformCard[]>(() =>
    SOCIAL_PLATFORMS.map((platform) => {
      const real = this.accounts().filter((a) => a.platform === platform && isReal(a));
      const realConnected = real.filter((a) => a.connectionStatus === 'CONNECTED');
      const status: PlatformStatus = realConnected.length ? 'connected' : real.length ? 'reconnect' : 'none';
      const names = (status === 'connected' ? realConnected : real).map((a) => a.displayName);
      return { platform, status, names };
    }),
  );

  protected readonly connectedCount = computed(() => this.cards().filter((c) => c.status === 'connected').length);

  constructor() {
    effect(() => {
      if (this.orgId()) void this.load();
    });
  }

  protected connect(card: PlatformCard): Promise<void> {
    return this.run(async (orgId) => {
      // The provider sign-in happens on the provider's site; the server stores the grant and returns to the app.
      const { authorizationUrl } = await firstValueFrom(this.api.startConnection(orgId, card.platform, 'LIVE'));
      window.location.assign(authorizationUrl);
    });
  }

  protected disconnect(platform: SocialPlatform): Promise<void> {
    return this.run(async (orgId) => {
      for (const connection of this.connections().filter((c) => c.platform === platform && c.status === 'ACTIVE'))
        await firstValueFrom(this.api.disconnect(orgId, connection.id));
      this.confirming.set(null);
      this.notice.emit(`${platform} disconnected`);
      await this.reload(orgId);
    });
  }

  private load(): Promise<void> {
    return this.run((orgId) => this.reload(orgId));
  }

  private async reload(orgId: string): Promise<void> {
    const [accounts, connections] = await Promise.all([firstValueFrom(this.api.list(orgId)), firstValueFrom(this.api.connections(orgId))]);
    this.accounts.set(accounts);
    this.connections.set(connections);
    this.connectedChange.emit(accounts.some((a) => isReal(a) && a.connectionStatus === 'CONNECTED'));
  }

  private async run(action: (orgId: string) => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      await action(this.orgId());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/** A destination backed by a provider connection; simulated and not-configured destinations are not offered here. */
function isReal(account: SocialAccount): boolean {
  return account.executionMode === 'LIVE' || account.executionMode === 'TEST_ACCOUNT';
}
