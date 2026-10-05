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
      <!-- Onboarding: one card per platform, Connect or Connected ✓. Settings keeps the detailed status below. -->
      <div class="connect-cards">
        @for (card of cards(); track card.platform) {
          <div class="connect-card" [class.connected]="card.status === 'connected' || card.status === 'demo'">
            <strong>{{ card.platform }}</strong>
            @switch (card.status) {
              @case ('connected') { <span class="connected-label">Connected ✓</span> }
              @case ('demo') { <span class="connected-label">Connected ✓</span> }
              @case ('reconnect') { <button type="button" class="primary block" (click)="connect(card)" [disabled]="busy()">Reconnect</button> }
              @default { <button type="button" class="primary block" (click)="connect(card)" [disabled]="busy()">Connect</button> }
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
  /** Onboarding cards: Connect / Connected ✓ only. */
  readonly compact = input(false);
  /** Whether at least one platform is connected (real or demo). */
  readonly connectedChange = output<boolean>();

  protected readonly demoNote = DEMO_MODE_NOTE;
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
