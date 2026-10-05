import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { httpUrlValidator } from '../../shared/validators/http-url.validator';
import { BRAND_TONES, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';

/** Onboarding step 2: the four brand inputs. Advanced brand settings stay in Settings for later. */
@Component({
  selector: 'app-onboarding-brand',
  imports: [ReactiveFormsModule],
  template: `
    <main class="onboarding">
      <div class="onboarding-step">
        <header class="onboarding-intro">
          <p class="step-label">Step 2 of 3</p>
          <h1>Tell us about your brand</h1>
          <p class="muted">We use this to write content that sounds like you and speaks to the right people.</p>
        </header>
        <form class="onboarding-form" [formGroup]="form" (ngSubmit)="submit()">
          <label>Business Description *
            <textarea rows="3" formControlName="businessDescription" placeholder="What does your business do?"></textarea>
            @if (form.controls.businessDescription.touched && form.controls.businessDescription.invalid) {
              <span class="field-error">Business description is required.</span>
            }
          </label>
          <label>Target Audience *
            <textarea rows="2" formControlName="targetAudience" placeholder="Who are your customers?"></textarea>
            @if (form.controls.targetAudience.touched && form.controls.targetAudience.invalid) {
              <span class="field-error">Target audience is required.</span>
            }
          </label>
          <label>Brand Tone *
            <select formControlName="brandTone">
              @for (t of tones; track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          <label>Website <span class="hint">Optional</span>
            <input type="url" formControlName="website" placeholder="https://yourwebsite.com" />
            @if (form.controls.website.hasError('httpUrl')) {
              <span class="field-error">Enter a full address such as https://example.com.</span>
            }
          </label>
          @if (problem(); as p) {
            <p class="error" role="alert">{{ p.detail }}</p>
          }
          <button class="primary block" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Continue' }}</button>
        </form>
      </div>
    </main>
  `,
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
