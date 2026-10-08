import { OnboardingMenuComponent } from './onboarding-menu.component';
import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { SocialConnectionsComponent } from '../social-accounts/components/social-connections.component';

/** Onboarding step 3: connect at least one social account (no skip), then continue to the dashboard. */
@Component({
  selector: 'app-onboarding-social',
  imports: [SocialConnectionsComponent, RouterLink, OnboardingMenuComponent],
  templateUrl: './social-step.page.html',
  styleUrl: './social-step.page.css',
  // The page is a standalone design; Shadow DOM keeps the app's global element styles (label, input, button) out of it.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class SocialStepPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly orgId = this.auth.me()?.onboarding.organizationId ?? null;
  protected readonly anyConnected = signal(false);
  protected readonly busy = signal(false);
  /** Set when the provider sign-in redirected back with a failure. */
  protected readonly oauthFailed = signal<string | null>(null);
  /** The server's result after a successful sign-in, e.g. that the login has no publishable Pages or accounts. */
  protected readonly oauthResult = signal<string | null>(null);

  constructor() {
    const query = inject(ActivatedRoute).snapshot.queryParamMap;
    if (query.get('status') === 'failed') {
      this.oauthFailed.set(query.get('message') ?? 'please try again.');
      inject(ToastService).error(`The connection did not complete: ${this.oauthFailed()}`);
    }
    if (query.get('status') === 'connected') this.oauthResult.set(query.get('message'));
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
