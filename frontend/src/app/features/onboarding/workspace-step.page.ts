import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { OrganizationApiService } from '../../core/services/organization-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { supportedTimeZones } from '../publications/models/time-zones';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';

/** Onboarding step 1: the workspace. The creator becomes its Admin. */
@Component({
  selector: 'app-onboarding-workspace',
  imports: [ProblemBannerComponent, ReactiveFormsModule],
  template: `
    <main class="login">
      <form class="card" [formGroup]="form" (ngSubmit)="submit()">
        <p class="muted">Step 1 of 3</p>
        <h1>Create your workspace</h1>
        <label>Business name <input formControlName="name" autocomplete="organization" /></label>
        <label>Industry <input formControlName="industry" placeholder="e.g. Software / Technology" /></label>
        <label>Time zone
          <select formControlName="timeZoneId">
            @for (z of zones; track z) { <option [value]="z">{{ z }}</option> }
          </select>
        </label>
        <app-problem-banner [problem]="problem()" />
        <button class="primary" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Saving…' : 'Continue' }}</button>
      </form>
    </main>
  `,
})
export class WorkspaceStepPage {
  private readonly auth = inject(AuthService);
  private readonly organizations = inject(OrganizationApiService);
  private readonly workspace = inject(WorkspaceService);
  private readonly router = inject(Router);

  protected readonly zones = withBrowserZone(supportedTimeZones());
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    industry: ['', [Validators.required, Validators.maxLength(200)]],
    timeZoneId: [Intl.DateTimeFormat().resolvedOptions().timeZone, Validators.required],
  });

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      const organization = await firstValueFrom(this.organizations.create(this.form.getRawValue()));
      this.workspace.select(organization.id);
      await this.router.navigateByUrl(await this.auth.nextOnboardingRoute());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/** The auto-detected zone is preselected, so it must be one of the options. */
function withBrowserZone(zones: string[]): string[] {
  const detected = Intl.DateTimeFormat().resolvedOptions().timeZone;
  return zones.includes(detected) ? zones : [detected, ...zones];
}
