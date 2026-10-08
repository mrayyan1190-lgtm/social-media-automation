import { SocialPlatform } from '../../../shared/models/platform.model';

export type ExecutionMode = 'LIVE' | 'TEST_ACCOUNT' | 'SIMULATED' | 'NOT_CONFIGURED';
export type ConnectionStatus = 'CONNECTED' | 'REAUTHORIZATION_REQUIRED' | 'DISCONNECTED';

/** A publishing destination. Its mode is always shown; SIMULATED never means a real publication. */
export interface SocialAccount {
  id: string;
  platform: SocialPlatform;
  displayName: string;
  executionMode: ExecutionMode;
  connectionStatus: ConnectionStatus;
  isEligibleDestination: boolean;
  createdAtUtc: string;
  externalAccountId: string | null;
  capabilities: string[] | null;
  lastVerifiedAtUtc: string | null;
}

/** Whether a provider can be connected live on this server; when not, the reason is shown instead of a fake option. */
export interface SocialProvider {
  platform: SocialPlatform;
  liveConnectionAvailable: boolean;
  blockedReason: string | null;
}

/** A provider grant. Metadata only: token material never reaches the browser. */
export interface SocialConnection {
  id: string;
  platform: SocialPlatform;
  displayName: string;
  status: 'ACTIVE' | 'EXPIRED' | 'REVOKED';
  scopes: string[];
  connectedAtUtc: string;
  accessTokenExpiresAtUtc: string | null;
  destinationCount: number;
}
