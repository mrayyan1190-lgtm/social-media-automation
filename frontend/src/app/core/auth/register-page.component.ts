import { Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OrganizationApiService } from '../services/organization-api.service';
import { WorkspaceService } from '../services/workspace.service';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiProblem, toApiProblem } from '../http/api-problem';
import { AuthService } from './auth.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';

/**
 * Self-serve sign-up: email, password and workspace name only. Sign-up creates the workspace; profile is the next step.
 * Google mode: someone who signed up with Google (signed in, no workspace yet) lands here too, with the email locked to
 * the Google one and no password; they enter the workspace name, then continue to the profile step like everyone else.
 */
@Component({
  selector: 'app-register-page',
  imports: [ProblemBannerComponent, ReactiveFormsModule, RouterLink],
  templateUrl: './register-page.component.html',
  styleUrl: './auth-page.css',
  host: { '(document:keydown.escape)': 'legal.set(null)' },
})
export class RegisterPageComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly organizations = inject(OrganizationApiService);
  private readonly workspace = inject(WorkspaceService);

  /** Signed in with Google and no workspace yet: only the workspace name (and terms) are asked. */
  readonly googleMode = signal(false);

  readonly busy = signal(false);
  readonly showPassword = signal(false);
  /** Title of the open Terms of Service / Privacy Policy dialog, or null. */
  readonly legal = signal<string | null>(null);
  readonly problem = signal<ApiProblem | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    workspaceName: ['', [Validators.required, Validators.maxLength(200)]],
    terms: [false, Validators.requiredTrue],
  });

  constructor() {
    void this.auth.loadMe().then((me) => {
      if (!me) return;
      if (me.onboarding.nextStep !== 'WORKSPACE') {
        // Already has a workspace: sign-up is done, continue where onboarding left off.
        void this.router.navigateByUrl(this.auth.routeFor(me));
        return;
      }
      this.googleMode.set(true);
      this.form.controls.email.setValue(me.email);
      this.form.controls.email.disable();
      this.form.controls.password.clearValidators();
      this.form.controls.password.updateValueAndValidity();
    }, () => undefined);
  }

  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  /** 0 = empty, otherwise 1 (Weak) to 4 (Strong); any typed password shows at least one bar. */
  readonly strength = computed(() => {
    const v = this.password();
    if (!v) return 0;
    return Math.max(1, [v.length >= 8, /[A-Z]/.test(v) && /[a-z]/.test(v), /[0-9]/.test(v), /[^A-Za-z0-9]/.test(v)].filter(Boolean).length);
  });
  readonly strengthLabel = computed(() => ['Password', 'Weak', 'Fair', 'Good', 'Strong'][this.strength()]);
  readonly strengthColor = computed(() => ['#D9E5F1', '#D1A682', '#A8B5D3', '#75AD9A'][this.strength() - 1]);

  openLegal(title: string, close: HTMLElement): void {
    this.legal.set(title);
    setTimeout(() => close.focus());
  }

  /** Google mode: leave without creating a workspace. */
  async signOut(): Promise<void> {
    try {
      await this.auth.logout();
    } finally {
      this.googleMode.set(false);
      await this.router.navigateByUrl('/login');
    }
  }

  async submit(formEl: HTMLFormElement): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      // The design reports field problems with the browser's own validation bubbles.
      formEl.reportValidity();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const { email, password, workspaceName } = this.form.getRawValue();
      if (this.googleMode()) {
        // Same workspace as an email sign-up: time zone is set on the profile step, one person writes and approves.
        const organization = await firstValueFrom(this.organizations.create({
          name: workspaceName, timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC', requireDistinctReviewer: false,
        }));
        this.workspace.select(organization.id);
      } else {
        await this.auth.register({ email, password, workspaceName });
      }
      await this.router.navigateByUrl(await this.auth.nextOnboardingRoute());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
