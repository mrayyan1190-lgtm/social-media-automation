import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService, ONBOARDING_ROUTES } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { OrganizationApiService } from '../../core/services/organization-api.service';
import { browserTimeZone, offsetAt, supportedTimeZones } from '../publications/models/time-zones';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { OnboardingLayoutComponent } from './onboarding-layout.component';

/**
 * Onboarding step 1: the user's name and the workspace time zone (detected from the browser, editable). Reopened from
 * step 2's Back button it shows the saved values; saving again simply updates them.
 */
@Component({
  selector: 'app-onboarding-profile',
  imports: [ProblemBannerComponent, ReactiveFormsModule, OnboardingLayoutComponent],
  templateUrl: './profile-step.page.html',
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

  constructor() {
    const me = this.auth.me();
    const orgId = me?.onboarding.organizationId;
    if (me?.onboarding.profileCompleted) {
      this.form.controls.fullName.setValue(me.displayName);
      if (orgId) {
        void firstValueFrom(inject(OrganizationApiService).get(orgId)).then((org) => {
          if (!this.zones.includes(org.timeZoneId)) this.zones.unshift(org.timeZoneId);
          this.form.controls.timeZoneId.setValue(org.timeZoneId);
        }, () => undefined);
      }
    }
  }

  private readonly labels = new Map<string, string>();

  /** "Asia/Karachi (UTC+5)", as in the design; UTC itself needs no offset. Cached: the list has ~400 zones. */
  protected zoneLabel(zone: string): string {
    let label = this.labels.get(zone);
    if (!label) {
      const minutes = offsetAt(Date.now(), zone);
      const abs = Math.abs(minutes);
      label = zone === 'UTC' ? zone
        : `${zone} (UTC${minutes < 0 ? '-' : '+'}${Math.floor(abs / 60)}${abs % 60 ? ':' + String(abs % 60).padStart(2, '0') : ''})`;
      this.labels.set(zone, label);
    }
    return label;
  }

  protected async submit(formEl: HTMLFormElement): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      // The design reports field problems with the browser's own validation bubbles.
      formEl.reportValidity();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const { fullName, timeZoneId } = this.form.getRawValue();
      await this.auth.completeProfile({ organizationId: this.auth.me()?.onboarding.organizationId ?? null, fullName, timeZoneId });
      // Next is always step 2, also when it was already finished (the user came back with Back to edit the profile).
      await this.router.navigateByUrl(ONBOARDING_ROUTES.BRAND_PROFILE);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
