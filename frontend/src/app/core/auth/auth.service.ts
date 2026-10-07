import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CompleteProfileRequest, Me, OnboardingStep, RegisterRequest } from './auth.models';

/** Where each onboarding step lives; the API decides which step is next. */
export const ONBOARDING_ROUTES: Record<OnboardingStep, string> = {
  WORKSPACE: '/onboarding/workspace',
  PROFILE: '/onboarding/profile',
  BRAND_PROFILE: '/onboarding/brand',
  SOCIAL_CONNECTIONS: '/onboarding/social-connections',
  COMPLETE: '/home',
};

/**
 * Session state. The session itself is an HttpOnly cookie set by the API; no token is ever
 * readable here or stored in browser storage.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  readonly me = signal<Me | null>(null);

  /** Returns null when not signed in; rethrows other failures. */
  async loadMe(): Promise<Me | null> {
    try {
      const me = await firstValueFrom(this.http.get<Me>(`${environment.apiBaseUrl}/me`));
      this.me.set(me);
      return me;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.me.set(null);
        return null;
      }
      throw error;
    }
  }

  /** The login response is the same session context as GET /me. */
  async login(email: string, password: string): Promise<Me> {
    const me = await firstValueFrom(this.http.post<Me>(`${environment.apiBaseUrl}/auth/login`, { email, password }));
    this.me.set(me);
    return me;
  }

  async register(request: RegisterRequest): Promise<Me | null> {
    await firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/auth/register`, request));
    return this.loadMe();
  }

  /** Onboarding profile step; the response is the refreshed session context. */
  async completeProfile(request: CompleteProfileRequest): Promise<Me> {
    const me = await firstValueFrom(this.http.put<Me>(`${environment.apiBaseUrl}/me/profile`, request));
    this.me.set(me);
    return me;
  }

  /** Reloads the session and returns the route of the next required onboarding step (or the dashboard). */
  async nextOnboardingRoute(): Promise<string> {
    const me = await this.loadMe();
    return me ? ONBOARDING_ROUTES[me.onboarding.nextStep] : '/login';
  }

  /** Full-page navigation: the server runs the Google sign-in and comes back to /home (or /login?error=...). */
  continueWithGoogle(): void {
    window.location.href = `${environment.apiBaseUrl}/auth/google`;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/auth/logout`, null));
    } finally {
      this.me.set(null);
    }
  }

  clear(): void {
    this.me.set(null);
  }
}
