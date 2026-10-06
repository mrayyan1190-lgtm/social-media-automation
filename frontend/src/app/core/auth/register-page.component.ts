import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiProblem, toApiProblem } from '../http/api-problem';
import { AuthService } from './auth.service';

/** Self-serve sign-up: email, password and workspace name only. Sign-up creates the workspace; profile is the next step. */
@Component({
  selector: 'app-register-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register-page.component.html',
  styleUrl: './register-page.component.css',
  // The page is a standalone design; Shadow DOM keeps the app's global element styles (label, input, button) out of it.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class RegisterPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly showPassword = signal(false);
  readonly problem = signal<ApiProblem | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    workspaceName: ['', [Validators.required, Validators.maxLength(200)]],
    terms: [false, Validators.requiredTrue],
  });

  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  readonly strength = computed(() => {
    const v = this.password();
    if (!v) return 0;
    return [v.length >= 8, /[A-Z]/.test(v) && /[a-z]/.test(v), /[0-9]/.test(v), /[^A-Za-z0-9]/.test(v)].filter(Boolean).length;
  });
  readonly hint = computed(() => {
    const score = this.strength();
    if (!this.password()) return 'Use 8+ characters with a number or symbol.';
    return score <= 1 ? 'Weak password.' : score === 2 ? 'Fair — add a number or symbol.' : score === 3 ? 'Good password.' : 'Strong password.';
  });

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    try {
      const { email, password, workspaceName } = this.form.getRawValue();
      await this.auth.register({ email, password, workspaceName });
      await this.router.navigateByUrl(await this.auth.nextOnboardingRoute());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
