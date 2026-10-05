import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { SocialPlatform } from '../../campaigns/models/campaign.model';
import { ExecutionMode, SocialAccount, SocialConnection, SocialProvider } from '../models/social-account.model';

@Injectable({ providedIn: 'root' })
export class SocialAccountApiService {
  private readonly http = inject(HttpClient);

  list(orgId: string, platform?: SocialPlatform): Observable<SocialAccount[]> {
    return this.http.get<SocialAccount[]>(this.url(orgId), platform ? { params: { platform } } : {});
  }

  /** Admin. Only SIMULATED or NOT_CONFIGURED destinations can be created without a provider connection. */
  create(orgId: string, platform: SocialPlatform, displayName: string, executionMode: ExecutionMode): Observable<SocialAccount> {
    return this.http.post<SocialAccount>(this.url(orgId), { platform, displayName, executionMode });
  }

  /** A clearly labelled demo destination, used when live connection is not available on this server. */
  createSimulated(orgId: string, platform: SocialPlatform): Observable<SocialAccount> {
    return this.http.post<SocialAccount>(`${this.url(orgId)}/simulated`, { platform });
  }

  providers(orgId: string): Observable<SocialProvider[]> {
    return this.http.get<SocialProvider[]>(`${this.connectionsUrl(orgId)}/providers`);
  }

  connections(orgId: string): Observable<SocialConnection[]> {
    return this.http.get<SocialConnection[]>(this.connectionsUrl(orgId));
  }

  /** Admin. Returns the provider sign-in URL; the server issued a one-time state bound to user, workspace and mode. */
  startConnection(orgId: string, platform: SocialPlatform, mode: 'LIVE' | 'TEST_ACCOUNT'): Observable<{ authorizationUrl: string }> {
    return this.http.post<{ authorizationUrl: string }>(`${this.connectionsUrl(orgId)}/${platform}/start`, { mode });
  }

  disconnect(orgId: string, connectionId: string): Observable<void> {
    return this.http.post<void>(`${this.connectionsUrl(orgId)}/${connectionId}/disconnect`, {});
  }

  private connectionsUrl(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/social-connections`;
  }

  private url(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/social-accounts`;
  }
}
