import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { DEMO_MODE_NOTE } from '../../../shared/models/states';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../campaigns/models/campaign.model';
import { SocialAccount, SocialConnection, SocialProvider } from '../models/social-account.model';
import { SocialAccountApiService } from '../services/social-account-api.service';

type PlatformStatus = 'connected' | 'reconnect' | 'demo' | 'none';

interface PlatformCard {
  platform: SocialPlatform;
  status: PlatformStatus;
  /** Connected page/profile/account names. */
  names: string[];
  canGoLive: boolean;
}

/**
 * LinkedIn, Facebook and Instagram with plain-language status and Connect / Reconnect / Disconnect. Used by onboarding
 * and by Settings. Where the server cannot connect a platform for real yet, Connect adds a demo connection that is
 * labelled as such (posts are never sent externally); provider modes, scopes and tokens are never shown.
 */
@Component({
  selector: 'app-social-connections',
  template: `
    @if (problem(); as p) {
      <p class="error" role="alert">{{ p.detail }}</p>
    }
    @if (compact()) {
      <!-- Onboarding: one card per platform, Connect or Connected. Styled by the onboarding page. Settings keeps the detailed status below. -->
      <div class="cards">
        @for (card of cards(); track card.platform) {
          <div class="card" [class.done]="card.status === 'connected' || card.status === 'demo'">
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
      @if (hasDemo()) { <p class="hint">Some connections are in demo mode: posts are not sent until a live connection is set up in Settings.</p> }
    } @else {
    <ul class="list">
      @for (card of cards(); track card.platform) {
        <li>
          <span>
            <strong>{{ card.platform }}</strong>
            @if (card.names.length) { · {{ card.names.join(', ') }} }
            <br />
            @switch (card.status) {
              @case ('connected') { <span class="badge delivery success">Connected</span> }
              @case ('reconnect') { <span class="badge delivery danger">Needs Reconnection</span> }
              @case ('demo') { <span class="badge mode simulated">Demo Mode</span> <span class="hint">Posts are not sent externally</span> }
              @default { <span class="badge">Not Connected</span> }
            }
          </span>
          @if (canManage()) {
          <span class="actions">
            @switch (card.status) {
              @case ('connected') {
                @if (allowDisconnect()) { <button type="button" (click)="disconnect(card.platform)" [disabled]="busy()">Disconnect</button> }
              }
              @case ('reconnect') { <button type="button" class="primary" (click)="connect(card)" [disabled]="busy()">Reconnect</button> }
              @case ('demo') {
                @if (card.canGoLive) { <button type="button" class="primary" (click)="connect(card)" [disabled]="busy()">Connect</button> }
              }
              @default { <button type="button" class="primary" (click)="connect(card)" [disabled]="busy()">Connect</button> }
            }
          </span>
          }
        </li>
      }
    </ul>
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
  /** Whether at least one platform is connected (real or demo). */
  readonly connectedChange = output<boolean>();

  protected readonly demoNote = DEMO_MODE_NOTE;
  protected readonly subtitles: Record<SocialPlatform, string> = { LinkedIn: 'Pages & profiles', Facebook: 'Pages & groups', Instagram: 'Business accounts' };
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  private readonly accounts = signal<SocialAccount[]>([]);
  private readonly providers = signal<SocialProvider[]>([]);
  private readonly connections = signal<SocialConnection[]>([]);

  protected readonly cards = computed<PlatformCard[]>(() =>
    SOCIAL_PLATFORMS.map((platform) => {
      const mine = this.accounts().filter((a) => a.platform === platform && a.executionMode !== 'NOT_CONFIGURED');
      const real = mine.filter((a) => a.executionMode !== 'SIMULATED');
      const realConnected = real.filter((a) => a.connectionStatus === 'CONNECTED');
      const demoConnected = mine.filter((a) => a.executionMode === 'SIMULATED' && a.connectionStatus === 'CONNECTED');
      const status: PlatformStatus = realConnected.length ? 'connected' : real.length ? 'reconnect' : demoConnected.length ? 'demo' : 'none';
      const names = (status === 'connected' ? realConnected : status === 'reconnect' ? real : []).map((a) => a.displayName);
      const canGoLive = this.providers().find((p) => p.platform === platform)?.liveConnectionAvailable ?? false;
      return { platform, status, names, canGoLive };
    }),
  );

  /** Demo connections are never passed off as live: onboarding still says so in plain words. */
  protected readonly hasDemo = computed(() => this.cards().some((c) => c.status === 'demo'));

  constructor() {
    effect(() => {
      if (this.orgId()) void this.load();
    });
  }

  protected connect(card: PlatformCard): Promise<void> {
    return this.run(async (orgId) => {
      if (card.canGoLive) {
        // The provider sign-in happens on the provider's site; the server stores the grant and returns to the app.
        const { authorizationUrl } = await firstValueFrom(this.api.startConnection(orgId, card.platform, 'LIVE'));
        window.location.assign(authorizationUrl);
        return;
      }
      await firstValueFrom(this.api.createSimulated(orgId, card.platform));
      await this.reload(orgId);
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
    const [accounts, providers, connections] = await Promise.all([
      firstValueFrom(this.api.list(orgId)),
      firstValueFrom(this.api.providers(orgId)),
      firstValueFrom(this.api.connections(orgId)),
    ]);
    this.accounts.set(accounts);
    this.providers.set(providers);
    this.connections.set(connections);
    this.connectedChange.emit(accounts.some((a) => a.connectionStatus === 'CONNECTED'));
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
