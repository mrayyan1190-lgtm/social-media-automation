import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { InternalAuthService } from '../../core/auth/internal-auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';

/** Internal system-administration sign-in. No sign-up, workspace or onboarding: customers use /login. */
@Component({
  selector: 'app-internal-login-page',
  imports: [ReactiveFormsModule],
  template: `
    <main class="login internal-login">
      <form class="card" [formGroup]="form" (ngSubmit)="submit()">
        <p class="eyebrow">Internal</p>
        <h1>System Administration</h1>
        <p class="muted">Restricted to platform operators.</p>
        <label>Email <input type="email" formControlName="email" autocomplete="username" /></label>
        <label>Password <input type="password" formControlName="password" autocomplete="current-password" /></label>
        @if (problem(); as p) {
          <p class="error" role="alert">{{ p.status === 403 ? 'This account does not have system administration access.' : p.detail }}</p>
        }
        <button class="primary" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Signing in…' : 'Sign In' }}</button>
      </form>
    </main>
  `,
  styles: `
    .internal-login { background: #1f2933; min-height: 100vh; }
    .eyebrow { margin: 0; text-transform: uppercase; letter-spacing: 0.08em; font-size: 0.75rem; color: #b45309; font-weight: 600; }
  `,
})
export class InternalLoginPage {
  private readonly internalAuth = inject(InternalAuthService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly problem = signal<ApiProblem | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  async submit(): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      const { email, password } = this.form.getRawValue();
      await this.internalAuth.login(email, password);
      this.auth.clear(); // the customer session context (if any) was replaced by this sign-in
      await this.router.navigateByUrl('/internal');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
