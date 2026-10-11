import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService, ONBOARDING_ROUTES } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { httpUrlValidator } from '../../shared/validators/http-url.validator';
import { BRAND_TONES, BrandProfile, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { OnboardingLayoutComponent } from './onboarding-layout.component';

/**
 * Onboarding step 2: the four brand inputs. Advanced brand settings stay in Settings for later. Reopened from step 3's
 * Back button it loads the saved profile and saves an edit (with its row version and the existing prohibited terms).
 */
@Component({
  selector: 'app-onboarding-brand',
  imports: [ProblemBannerComponent, ReactiveFormsModule, RouterLink, OnboardingLayoutComponent],
  templateUrl: './brand-step.page.html',
})
export class BrandStepPage {
  private readonly auth = inject(AuthService);
  private readonly brandProfiles = inject(BrandProfileApiService);
  private readonly router = inject(Router);

  protected readonly backRoute = ONBOARDING_ROUTES.PROFILE;
  protected readonly tones = BRAND_TONES;
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  /** The saved profile when this step was already finished; null on the first visit. */
  private saved: BrandProfile | null = null;
  protected readonly form = inject(FormBuilder).nonNullable.group({
    businessDescription: ['', [Validators.required, Validators.maxLength(2000)]],
    targetAudience: ['', [Validators.required, Validators.maxLength(2000)]],
    brandTone: ['Professional' as BrandTone, Validators.required],
    website: ['', httpUrlValidator],
  });

  constructor() {
    const onboarding = this.auth.me()?.onboarding;
    if (onboarding?.brandProfileCompleted && onboarding.organizationId) {
      void firstValueFrom(this.brandProfiles.get(onboarding.organizationId)).then((profile) => {
        this.saved = profile;
        this.form.setValue({
          businessDescription: profile.businessDescription, targetAudience: profile.targetAudience,
          brandTone: profile.brandTone, website: profile.website ?? '',
        });
      }, (error) => this.problem.set(toApiProblem(error)));
    }
  }

  protected async submit(formEl: HTMLFormElement): Promise<void> {
    const orgId = this.auth.me()?.onboarding.organizationId;
    if (!orgId) return;
    // type="url" accepts any scheme; the API needs http(s), so that rule joins the browser's validation bubbles.
    formEl.querySelector<HTMLInputElement>('#website')
      ?.setCustomValidity(this.form.controls.website.hasError('httpUrl') ? 'Enter a full address such as https://example.com.' : '');
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      formEl.reportValidity();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const v = this.form.getRawValue();
      // First save: no row version yet. An edit sends the saved version and keeps the prohibited terms set in Settings.
      this.saved = await firstValueFrom(this.brandProfiles.save(orgId,
        { ...v, website: v.website.trim() || null, prohibitedTerms: this.saved?.prohibitedTerms ?? [] }, this.saved?.rowVersion ?? null));
      // Always show the social step after the brand step, even when a destination (e.g. the workspace's default Facebook
      // Page) already counts as connected: the user sees it and may add LinkedIn or Instagram before continuing.
      await this.router.navigateByUrl(ONBOARDING_ROUTES.SOCIAL_CONNECTIONS);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
