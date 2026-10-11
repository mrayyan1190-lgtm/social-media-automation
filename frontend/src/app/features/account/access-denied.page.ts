import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-access-denied-page',
  imports: [RouterLink],
  styleUrl: '../../core/auth/auth-page.css',
  template: `
    <div class="tw-page m-0 font-sans antialiased text-[#142D4B]">
      <main class="auth-canvas relative isolate grid min-h-[100svh] place-items-center px-4 py-8">
        <section class="card-enter relative w-full max-w-[430px] rounded-[26px] border border-white/90 bg-white/88 px-7 py-8 text-center shadow-[0_25px_75px_rgba(49,77,109,.12)] backdrop-blur-2xl">
          <span class="mx-auto grid h-14 w-14 place-items-center rounded-[18px] bg-[#FCE6E4] text-[#C1707A] shadow-[inset_0_1px_0_#fff,0_8px_20px_#c1707a22]">
            <svg class="h-6 w-6" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="4" y="10" width="16" height="12" rx="3"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>
          </span>
          <h1 class="mt-4 font-display text-[26px] font-extrabold leading-tight tracking-[-.055em] text-[#142C4B]">Access denied<span class="text-[#6E88AC]">.</span></h1>
          <p class="mt-2 text-[13px] font-medium leading-6 text-[#7288A0]">You do not have permission to open this page.</p>
          <a routerLink="/home" class="mt-5 inline-flex h-[44px] items-center justify-center rounded-[12px] bg-[#142C4B] px-6 text-[13px] font-extrabold text-white no-underline shadow-[0_13px_25px_rgba(28,60,97,.13)] transition hover:-translate-y-[2px] hover:bg-[#223E60]">Back to Home</a>
        </section>
      </main>
    </div>
  `,
})
export class AccessDeniedPage {}
