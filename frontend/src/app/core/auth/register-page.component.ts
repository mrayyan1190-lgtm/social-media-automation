import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiProblem, toApiProblem } from '../http/api-problem';
import { AuthService } from './auth.service';

/** Self-serve sign-up: email, password and workspace name only. Sign-up creates the workspace; profile is the next step. */
@Component({
  selector: 'app-register-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <main class="login">
      <form class="card" [formGroup]="form" (ngSubmit)="submit()">
        <h1>Create your account</h1>
        <label>Email
          <input type="email" formControlName="email" autocomplete="username" placeholder="you@company.com" />
          @if (form.controls.email.touched && form.controls.email.invalid) { <span class="field-error">Enter a valid email address.</span> }
        </label>
        <label>Password
          <input type="password" formControlName="password" autocomplete="new-password" placeholder="At least 8 characters" />
          @if (form.controls.password.touched && form.controls.password.invalid) { <span class="field-error">Use at least 8 characters.</span> }
        </label>
        <label>Workspace Name
          <input formControlName="workspaceName" autocomplete="organization" placeholder="e.g. Centaiva" />
          @if (form.controls.workspaceName.touched && form.controls.workspaceName.invalid) { <span class="field-error">Workspace name is required.</span> }
        </label>
        @if (problem(); as p) {
          <p class="error" role="alert">{{ p.detail }}</p>
        }
        <button class="primary block" type="submit" [disabled]="form.invalid || busy()">{{ busy() ? 'Creating…' : 'Create Account' }}</button>
        <p class="muted center">Already have an account? <a routerLink="/login">Sign in</a></p>
      </form>
    </main>
  `,
})
export class RegisterPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly problem = signal<ApiProblem | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    workspaceName: ['', [Validators.required, Validators.maxLength(200)]],
  });

  async submit(): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      await this.auth.register(this.form.getRawValue());
      await this.router.navigateByUrl(await this.auth.nextOnboardingRoute());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
