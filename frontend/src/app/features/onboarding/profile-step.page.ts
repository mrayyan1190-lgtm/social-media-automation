import { OnboardingMenuComponent } from './onboarding-menu.component';
import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { browserTimeZone, supportedTimeZones } from '../publications/models/time-zones';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';

/** Onboarding step 1: the user's name and the workspace time zone (detected from the browser, editable). */
@Component({
  selector: 'app-onboarding-profile',
  imports: [ProblemBannerComponent, ReactiveFormsModule, RouterLink, OnboardingMenuComponent],
  templateUrl: './profile-step.page.html',
  styleUrl: './profile-step.page.css',
  // The page is a standalone design; Shadow DOM keeps the app's global element styles (label, input, button) out of it.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class ProfileStepPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly detectedZone = browserTimeZone();
  /** The detected zone is preselected, so it must be one of the options. */
  protected readonly zones = [...new Set([this.detectedZone, ...supportedTimeZones()])];
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    timeZoneId: [this.detectedZone, Validators.required],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const { fullName, timeZoneId } = this.form.getRawValue();
      await this.auth.completeProfile({ organizationId: this.auth.me()?.onboarding.organizationId ?? null, fullName, timeZoneId });
      await this.router.navigateByUrl(await this.auth.nextOnboardingRoute());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
