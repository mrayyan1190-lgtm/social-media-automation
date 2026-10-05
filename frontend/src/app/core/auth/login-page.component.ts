import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiProblem, toApiProblem } from '../http/api-problem';
import { AuthService, ONBOARDING_ROUTES } from './auth.service';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <main class="login">
      <form class="card" [formGroup]="form" (ngSubmit)="submit()">
        <h1>Sign in</h1>
        <label>Email <input type="email" formControlName="email" autocomplete="username" placeholder="you@company.com" /></label>
        <label>Password <input type="password" formControlName="password" autocomplete="current-password" /></label>
        @if (problem(); as p) {
          <p class="error" role="alert">{{ p.detail }}</p>
        }
        <button class="primary block" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Signing in…' : 'Sign In' }}</button>
        <p class="muted center">New here? <a routerLink="/signup">Create an account</a></p>
      </form>
    </main>
  `,
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

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
      const me = await this.auth.login(email, password);
      // An unfinished onboarding step always wins over the return URL.
      const step = me.onboarding.nextStep;
      await this.router.navigateByUrl(
        step !== 'COMPLETE' ? ONBOARDING_ROUTES[step] : this.route.snapshot.queryParamMap.get('returnUrl') || '/home');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
