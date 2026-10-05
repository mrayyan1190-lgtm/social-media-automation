import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SystemAdminMe } from './auth.models';

/**
 * Internal system-administration session. Same HttpOnly cookie as the customer app, but issued by the internal sign-in,
 * which the API only grants to system administrators; every /api/internal endpoint re-checks that.
 */
@Injectable({ providedIn: 'root' })
export class InternalAuthService {
  private readonly http = inject(HttpClient);
  readonly me = signal<SystemAdminMe | null>(null);

  /** null when not signed in (401) or not signed in as a system administrator (403); rethrows other failures. */
  async loadMe(): Promise<SystemAdminMe | null> {
    try {
      const me = await firstValueFrom(this.http.get<SystemAdminMe>(`${environment.internalApiBaseUrl}/me`));
      this.me.set(me);
      return me;
    } catch (error) {
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.me.set(null);
        return null;
      }
      throw error;
    }
  }

  async login(email: string, password: string): Promise<SystemAdminMe> {
    const me = await firstValueFrom(this.http.post<SystemAdminMe>(`${environment.internalApiBaseUrl}/auth/login`, { email, password }));
    this.me.set(me);
    return me;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(`${environment.internalApiBaseUrl}/auth/logout`, null));
    } finally {
      this.me.set(null);
    }
  }
}
