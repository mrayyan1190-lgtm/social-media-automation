import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { httpUrlValidator } from '../../shared/validators/http-url.validator';
import { BRAND_TONES, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';

/** Onboarding step 2: the four brand inputs. Advanced brand settings stay in Settings for later. */
@Component({
  selector: 'app-onboarding-brand',
  imports: [ReactiveFormsModule, RouterLink],
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
      await this.router.navigateByUrl(await this.auth.nextOnboardingRoute());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
