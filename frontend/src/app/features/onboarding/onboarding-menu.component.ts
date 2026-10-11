import { Component, ElementRef, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/** The ⋮ menu in the onboarding top bar: lets someone who is part-way through onboarding sign out. */
@Component({
  selector: 'app-onboarding-menu',
  host: {
    class: 'relative',
    id: 'accountMenuRoot',
    '(document:pointerdown)': 'closeOutside($event)',
    '(document:keydown.escape)': 'open.set(false)',
  },
  template: `
    <button id="accountMenuButton" type="button" aria-label="Account menu" aria-controls="accountMenu" [attr.aria-expanded]="open()" aria-haspopup="true" (click)="open.set(!open())" class="grid h-11 w-11 place-items-center rounded-full border border-white/75 bg-white/70 text-[#455e7c] shadow-[0_6px_20px_rgba(62,83,111,.07)] transition hover:bg-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#779cc4]">
      <svg viewBox="0 0 24 24" class="h-[19px] w-[19px] fill-current" aria-hidden="true"><circle cx="12" cy="5" r="1.75"/><circle cx="12" cy="12" r="1.75"/><circle cx="12" cy="19" r="1.75"/></svg>
    </button>
    @if (open()) {
      <div id="accountMenu" class="menu-enter absolute right-0 top-[54px] w-[282px] max-w-[calc(100vw-32px)] rounded-[18px] border border-white/90 bg-white/95 p-2 shadow-[0_22px_55px_rgba(40,62,94,.15)] backdrop-blur-xl">
        <div class="px-3 pb-3 pt-3">
          <p class="text-[11px] font-bold uppercase tracking-[.1em] text-[#8b9db4]">Signed in as</p>
          <p class="mt-1.5 break-all text-[13px] font-semibold text-[#324b68]">{{ auth.me()?.email }}</p>
        </div>
        <div class="border-t border-[#e8ecf2] p-1 pt-2">
          <button type="button" id="signOutButton" (click)="signOut()" [disabled]="busy()" class="flex w-full items-center gap-2.5 rounded-xl px-3 py-2.5 text-left text-[13px] font-bold text-[#c54f54] transition hover:bg-[#fff2f0]">
            <svg viewBox="0 0 24 24" class="h-[17px] w-[17px] fill-none stroke-current stroke-[1.8]" aria-hidden="true"><path d="M10 17l5-5-5-5m5 5H3m9-8h6a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-6" stroke-linecap="round" stroke-linejoin="round"/></svg>{{ busy() ? 'Signing out…' : 'Sign out' }}
          </button>
        </div>
      </div>
    }
  `,
})
export class OnboardingMenuComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly open = signal(false);
  protected readonly busy = signal(false);

  protected closeOutside(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) this.open.set(false);
  }

  protected async signOut(): Promise<void> {
    this.busy.set(true);
    try {
      await this.auth.logout();
    } finally {
      this.busy.set(false);
      await this.router.navigateByUrl('/login');
    }
  }
}
