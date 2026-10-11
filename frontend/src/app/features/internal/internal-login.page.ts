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
  styleUrl: '../../core/auth/auth-page.css',
  template: `
    <div class="tw-page m-0 font-sans antialiased text-[#142D4B]">
      <main class="auth-canvas relative isolate grid min-h-[100svh] place-items-center px-4 py-8">
        <form class="card-enter relative w-full max-w-[430px] rounded-[26px] border border-white/90 bg-white/88 px-7 py-7 shadow-[0_25px_75px_rgba(49,77,109,.12)] backdrop-blur-2xl"
          [formGroup]="form" (ngSubmit)="submit()">
          <div class="flex items-center gap-3">
            <span class="grid h-11 w-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-[#6f79d9] to-[#9b7fd0] text-white shadow-[0_8px_20px_#6f79d940]">
              <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m12 3 9 5-9 5-9-5 9-5Z"/><path d="m3 13 9 5 9-5"/></svg>
            </span>
            <span class="inline-flex items-center rounded-full border border-[#DDE7F2] bg-[#F3F7FC] px-3 py-1.5 text-[9px] font-extrabold uppercase tracking-[.14em] text-[#6F86A3]">Internal</span>
          </div>
          <h1 class="mt-4 font-display text-[25px] font-extrabold leading-[1.17] tracking-[-.055em] text-[#142C4B] sm:text-[28px]">System Administration<span class="text-[#6E88AC]">.</span></h1>
          <p class="mt-1.5 text-[12px] font-medium leading-[1.6] text-[#7288A0]">Restricted to platform operators.</p>
          <label for="ilEmail" class="mt-5 block text-[12px] font-extrabold text-[#263E59]">Email address</label>
          <input id="ilEmail" type="email" formControlName="email" autocomplete="username" placeholder="you@company.com" [class]="field" />
          <label for="ilPassword" class="mt-3 block text-[12px] font-extrabold text-[#263E59]">Password</label>
          <input id="ilPassword" type="password" formControlName="password" autocomplete="current-password" placeholder="Your password" [class]="field" />
          @if (problem(); as p) {
            <p class="mt-3 rounded-[12px] border border-[#F3D5D9] bg-[#FFF5F6] px-3 py-2.5 text-[12px] font-semibold text-[#B85567]" role="alert">{{ p.status === 403 ? 'This account does not have system administration access.' : p.detail }}</p>
          }
          <button type="submit" [disabled]="form.invalid || busy()"
            class="shimmer relative mt-5 inline-flex h-[45px] w-full items-center justify-center overflow-hidden rounded-[12px] bg-[#142C4B] px-6 text-[13px] font-extrabold text-white shadow-[0_13px_25px_rgba(28,60,97,.13)] transition hover:-translate-y-[2px] hover:bg-[#223E60] disabled:opacity-60 disabled:hover:translate-y-0">{{ busy() ? 'Signing in…' : 'Sign In' }}</button>
        </form>
      </main>
    </div>
  `,
})
export class InternalLoginPage {
  private readonly internalAuth = inject(InternalAuthService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly field = 'mt-1.5 h-[44px] w-full rounded-[12px] border border-[#DBE6F1] bg-[#F7FAFE] px-4 text-[13px] font-semibold text-[#1A3451] placeholder:font-medium placeholder:text-[#9AACC0] outline-none transition hover:border-[#BCCEDF] focus:border-[#99B9D7] focus:bg-white focus:ring-[4px] focus:ring-[#BCD7EF]/35';
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
