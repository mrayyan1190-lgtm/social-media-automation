import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { browserTimeZone, supportedTimeZones } from '../publications/models/time-zones';

/** Onboarding step 1: the user's name and the workspace time zone (detected from the browser, editable). */
@Component({
  selector: 'app-onboarding-profile',
  imports: [ReactiveFormsModule],
  template: `
    <main class="onboarding">
      <div class="onboarding-step">
        <header class="onboarding-intro">
          <p class="step-label">Step 1 of 3</p>
          <h1>Complete your profile</h1>
          <p class="muted">This helps us personalize your workspace and schedule posts in your local time.</p>
        </header>
        <form class="onboarding-form" [formGroup]="form" (ngSubmit)="submit()">
          <label>Full Name
            <input formControlName="fullName" autocomplete="name" placeholder="Your full name" />
            @if (form.controls.fullName.touched && form.controls.fullName.invalid) { <span class="field-error">Full name is required.</span> }
          </label>
          <label>Time Zone
            <select formControlName="timeZoneId">
              @for (z of zones; track z) { <option [value]="z">{{ z }}</option> }
            </select>
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
