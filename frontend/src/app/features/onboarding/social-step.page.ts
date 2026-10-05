import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { SocialConnectionsComponent } from '../social-accounts/components/social-connections.component';

/** Onboarding step 3: connect at least one social account (no skip), then continue to the dashboard. */
@Component({
  selector: 'app-onboarding-social',
  imports: [SocialConnectionsComponent],
  template: `
    <main class="onboarding">
      <div class="onboarding-step">
        <header class="onboarding-intro">
          <p class="step-label">Step 3 of 3</p>
          <h1>Connect your social accounts</h1>
          <p class="muted">Connect at least one account to continue.</p>
        </header>
        <section class="onboarding-form">
          @if (oauthFailed(); as message) {
            <p class="error" role="alert">The connection did not complete: {{ message }}</p>
          }
          @if (orgId) {
            <app-social-connections [orgId]="orgId" [compact]="true" (connectedChange)="anyConnected.set($event)" />
          }
          <button type="button" class="primary block" (click)="finish()" [disabled]="!anyConnected() || busy()">Continue to Home</button>
        </section>
      </div>
    </main>
  `,
})
export class SocialStepPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly orgId = this.auth.me()?.onboarding.organizationId ?? null;
  protected readonly anyConnected = signal(false);
  protected readonly busy = signal(false);
  /** Set when the provider sign-in redirected back with a failure. */
  protected readonly oauthFailed = signal<string | null>(null);

  constructor() {
    const query = inject(ActivatedRoute).snapshot.queryParamMap;
    if (query.get('status') === 'failed') this.oauthFailed.set(query.get('message') ?? 'please try again.');
  }

  protected async finish(): Promise<void> {
    this.busy.set(true);
    try {
      await this.router.navigateByUrl(await this.auth.nextOnboardingRoute());
    } finally {
      this.busy.set(false);
    }
  }
}
