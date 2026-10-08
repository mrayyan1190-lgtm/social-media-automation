import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../../shared/models/platform.model';
import { SocialAccount, SocialConnection } from '../models/social-account.model';
import { SocialAccountApiService } from '../services/social-account-api.service';
import { PlatformIconComponent } from '../../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';

type PlatformStatus = 'connected' | 'reconnect' | 'none';

interface PlatformCard {
  platform: SocialPlatform;
  status: PlatformStatus;
  /** Connected page/profile/account names. */
  names: string[];
}

/**
 * LinkedIn, Facebook and Instagram with plain-language status and Connect / Reconnect / Disconnect. Used by onboarding
 * and by Settings. Connect always goes through the provider sign-in; when the server has no app credentials for a
 * platform it answers with an error that is shown here. Provider modes, scopes and tokens are never shown.
 */
@Component({
  selector: 'app-social-connections',
  imports: [ProblemBannerComponent, PlatformIconComponent],
  styles: `
    .sa-head { display: flex; align-items: center; justify-content: space-between; gap: 14px; flex-wrap: wrap; margin-bottom: 16px; }
    .sa-head .label { font-size: 10.5px; font-weight: 700; letter-spacing: 0.09em; text-transform: uppercase; color: #a7b1c0; }
    .sa-head .note { font-size: 12px; color: #a7b1c0; }
    .sa-head .note b { color: #7b8798; font-weight: 600; }
    .sa-list { color: #0f172a; }
    .sa-row { display: grid; grid-template-columns: 30px minmax(0, 1fr) 150px 132px; align-items: center; gap: 16px; padding: 14px 0; }
    .sa-row + .sa-row { border-top: 1px solid #f4f6fa; }
    .sa-hint { margin: 10px 0 0; font-size: 12px; color: #a7b1c0; }
    .sa-row ::ng-deep .platform-mark { width: 26px; height: 26px; border-radius: 7px; font-size: 12px; font-weight: 800; }
    .sa-row ::ng-deep .platform-mark.facebook { border-radius: 50%; }
    .sa-row ::ng-deep .platform-mark svg { width: 15px; height: 15px; }
    .sa-txt { min-width: 0; }
    .sa-name { font-size: 13.5px; font-weight: 700; color: #25303f; letter-spacing: -0.005em; }
    .sa-sub { font-size: 12.5px; color: #7b8798; margin-top: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .sa-sub.muted { color: #a7b1c0; }
    .sa-chip { display: inline-flex; align-items: center; gap: 6px; font-size: 11.5px; font-weight: 600; border: 1px solid transparent; border-radius: 999px;
      padding: 4px 10px; justify-self: end; white-space: nowrap; }
    .sa-chip .dot { width: 6px; height: 6px; border-radius: 50%; background: currentColor; flex: none; }
    .sa-chip.connected { background: #f0fdf5; border-color: #cdf2dd; color: #15803d; }
    .sa-chip.off { background: #f8fafc; border-color: #e8edf4; color: #7b8798; }
    .sa-chip.attn { background: #fff1f2; border-color: #ffd9dd; color: #be123c; }
    .sa-btn { justify-self: end; width: 132px; min-height: 0; font: inherit; font-size: 12.5px; font-weight: 600; color: #fff;
      background: linear-gradient(135deg, #3b82f6, #2563eb); border: none; border-radius: 10px; padding: 9px 0; cursor: pointer;
      display: inline-flex; align-items: center; justify-content: center; gap: 7px; box-shadow: 0 8px 18px -12px rgba(37, 99, 235, 0.95);
      transition: background 0.15s, transform 0.06s, border-color 0.15s, color 0.15s; }
    .sa-btn svg { width: 14px; height: 14px; flex: none; }
    .sa-btn:hover:not(:disabled) { background: linear-gradient(135deg, #2f74ea, #1d4ed8); }
    .sa-btn:active:not(:disabled) { transform: translateY(1px); }
    .sa-btn:disabled { cursor: default; opacity: 0.6; }
    .sa-btn.ghost { color: #475569; background: #fff; border: 1px solid #dfe5ef; box-shadow: none; }
    .sa-btn.ghost:hover:not(:disabled) { color: #2563eb; border-color: #c7d7fa; background: #eef4ff; }
    @media (max-width: 720px) {
      .sa-row { grid-template-columns: 30px minmax(0, 1fr); gap: 12px; padding: 15px 0; }
      .sa-row .sa-chip, .sa-row .sa-btn { grid-column: 2; justify-self: start; }
      .sa-row .sa-btn { width: auto; padding: 9px 16px; }
    }
  `,
  template: `
    <app-problem-banner [problem]="problem()" />
    @if (compact()) {
      <!-- Onboarding: one card per platform, Connect or Connected. Styled by the onboarding page. Settings keeps the detailed status below. -->
      <div class="cards">
        @for (card of cards(); track card.platform) {
          <div class="card" [class.done]="card.status === 'connected'">
            @switch (card.platform) {
              @case ('LinkedIn') { <span class="ic li" aria-hidden="true"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M4.98 3.5a2.5 2.5 0 1 1 .02 5 2.5 2.5 0 0 1-.02-5ZM3.2 9.7h3.6v11.1H3.2V9.7Zm5.9 0h3.45v1.52h.05a3.78 3.78 0 0 1 3.4-1.87c3.64 0 4.3 2.4 4.3 5.5v5.95h-3.6v-5.27c0-1.26-.02-2.88-1.76-2.88-1.76 0-2.03 1.37-2.03 2.79v5.36H9.1V9.7Z"/></svg></span> }
              @case ('Facebook') { <span class="ic fb" aria-hidden="true"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M13.5 21.9V14h2.7l.5-3.2h-3.2V8.7c0-.9.3-1.7 1.7-1.7h1.6V4.2S15.6 4 14.4 4c-2.5 0-4.2 1.5-4.2 4.3v2.5H7.3V14h2.9v7.9a10 10 0 1 0 3.3 0Z"/></svg></span> }
              @case ('Instagram') { <span class="ic ig" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><rect x="3.6" y="3.6" width="16.8" height="16.8" rx="5"/><circle cx="12" cy="12" r="3.9"/><circle cx="17" cy="7" r="1.15" fill="currentColor" stroke="none"/></svg></span> }
            }
            <span class="card-txt"><strong>{{ card.platform }}</strong><span>{{ subtitles[card.platform] }}</span></span>
            @switch (card.status) {
              @case ('reconnect') { <button class="connect" type="button" (click)="connect(card)" [disabled]="busy()"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg><span>Reconnect</span></button> }
              @case ('none') { <button class="connect" type="button" (click)="connect(card)" [disabled]="busy()"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg><span>Connect</span></button> }
              @default { <button class="connect" type="button" disabled><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg><span>Connected</span></button> }
            }
          </div>
        }
      </div>
    } @else {
    <div class="sa-head">
      <span class="label">Social accounts</span>
      <span class="note"><b>{{ connectedCount() }} of {{ cards().length }}</b> connected · approve-to-publish destinations</span>
    </div>
    <section class="sa-list">
      @for (card of cards(); track card.platform) {
        <div class="sa-row">
          <app-platform-icon [platform]="card.platform" />
          <div class="sa-txt">
            <div class="sa-name">{{ card.platform }}</div>
            <div class="sa-sub" [class.muted]="!card.names.length">{{ card.names.length ? card.names.join(', ') : 'No account linked' }}</div>
          </div>
          @switch (card.status) {
            @case ('connected') { <span class="sa-chip connected"><span class="dot"></span>Connected</span> }
            @case ('reconnect') { <span class="sa-chip attn"><span class="dot"></span>Needs Reconnection</span> }
            @default { <span class="sa-chip off"><span class="dot"></span>Not Connected</span> }
          }
          @if (canManage()) {
            @switch (card.status) {
              @case ('connected') {
                @if (allowDisconnect()) {
                  <button type="button" class="sa-btn ghost" (click)="disconnect(card.platform)" [disabled]="busy()">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="M16 17l5-5-5-5"/><path d="M21 12H9"/></svg>Disconnect
                  </button>
                }
              }
              @case ('reconnect') {
                <button type="button" class="sa-btn" (click)="connect(card)" [disabled]="busy()">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12a9 9 0 1 1-2.6-6.4"/><path d="M21 3v6h-6"/></svg>Reconnect
                </button>
              }
              @default {
                <button type="button" class="sa-btn" (click)="connect(card)" [disabled]="busy()">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14"/><path d="M5 12h14"/></svg>Connect
                </button>
              }
            }
          }
        </div>
      }
      @if (!canManage()) { <p class="sa-hint">Ask a workspace Admin to connect or reconnect accounts.</p> }
    </section>
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

  protected readonly subtitles: Record<SocialPlatform, string> = { LinkedIn: 'Pages & profiles', Facebook: 'Pages & groups', Instagram: 'Business accounts' };
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
