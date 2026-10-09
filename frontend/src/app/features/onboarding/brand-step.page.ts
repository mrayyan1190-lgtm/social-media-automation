import { OnboardingMenuComponent } from './onboarding-menu.component';
import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService, ONBOARDING_ROUTES } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { httpUrlValidator } from '../../shared/validators/http-url.validator';
import { BRAND_TONES, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';

/** Onboarding step 2: the four brand inputs. Advanced brand settings stay in Settings for later. */
@Component({
  selector: 'app-onboarding-brand',
  imports: [ProblemBannerComponent, ReactiveFormsModule, RouterLink, OnboardingMenuComponent],
  templateUrl: './brand-step.page.html',
  styleUrl: './brand-step.page.css',
  // The page is a standalone design; Shadow DOM keeps the app's global element styles (label, input, button) out of it.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class BrandStepPage {
  private readonly auth = inject(AuthService);
  private readonly brandProfiles = inject(BrandProfileApiService);
  private readonly router = inject(Router);

  protected readonly tones = BRAND_TONES;
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    businessDescription: ['', [Validators.required, Validators.maxLength(2000)]],
    targetAudience: ['', [Validators.required, Validators.maxLength(2000)]],
    brandTone: ['Professional' as BrandTone, Validators.required],
    website: ['', httpUrlValidator],
  });

  protected async submit(): Promise<void> {
    const orgId = this.auth.me()?.onboarding.organizationId;
    if (!orgId) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const v = this.form.getRawValue();
      // First save of the workspace's single profile: no row version yet.
      await firstValueFrom(this.brandProfiles.save(orgId, { ...v, website: v.website.trim() || null, prohibitedTerms: [] }, null));
      // Always show the social step after the brand step, even when a destination (e.g. the workspace's default Facebook
      // Page) already counts as connected: the user sees it and may add LinkedIn or Instagram before continuing.
      const next = await this.auth.nextOnboardingRoute();
      await this.router.navigateByUrl(next === ONBOARDING_ROUTES.COMPLETE ? ONBOARDING_ROUTES.SOCIAL_CONNECTIONS : next);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
