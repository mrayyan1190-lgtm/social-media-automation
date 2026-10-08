import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiProblem, toApiProblem } from '../http/api-problem';
import { AuthService, ONBOARDING_ROUTES } from './auth.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';

const GOOGLE_ERRORS: Record<string, string> = {
  google_not_configured: 'Google sign-in is not available yet. Sign in with your email and password.',
  google_failed: 'Google sign-in did not complete. Please try again.',
  google_email_unverified: 'Your Google account has no verified email address.',
  account_disabled: 'This account is disabled. Contact your administrator.',
};

function googleProblem(code: string | null): ApiProblem | null {
  return code && GOOGLE_ERRORS[code] ? { status: 0, code: code.toUpperCase(), detail: GOOGLE_ERRORS[code] } : null;
}

@Component({
  selector: 'app-login-page',
  imports: [ProblemBannerComponent, ReactiveFormsModule, RouterLink],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.css',
  // The page is a standalone design; Shadow DOM keeps the app's global element styles (label, input, button) out of it.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class LoginPageComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly busy = signal(false);
  readonly showPassword = signal(false);
  readonly problem = signal<ApiProblem | null>(googleProblem(inject(ActivatedRoute).snapshot.queryParamMap.get('error')));
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const { email, password } = this.form.getRawValue();
      const me = await this.auth.login(email, password);
      // System admins go to the platform console; for everyone else an unfinished onboarding step wins over the return URL.
      const step = me.onboarding.nextStep;
      await this.router.navigateByUrl(
        me.isSystemAdmin ? '/internal'
          : step !== 'COMPLETE' ? ONBOARDING_ROUTES[step] : this.route.snapshot.queryParamMap.get('returnUrl') || '/home');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
