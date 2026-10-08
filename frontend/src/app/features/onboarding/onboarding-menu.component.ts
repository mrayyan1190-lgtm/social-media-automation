import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/** The ⋮ menu in the onboarding top bar: lets someone who is part-way through onboarding sign out. */
@Component({
  selector: 'app-onboarding-menu',
  host: { '(document:click)': 'close($event)' },
  template: `
    <button class="kebab" type="button" aria-label="More options" [attr.aria-expanded]="open()" (click)="open.set(!open())">
      <svg viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="5" r="1.7"/><circle cx="12" cy="12" r="1.7"/><circle cx="12" cy="19" r="1.7"/></svg>
    </button>
    @if (open()) {
      <div class="menu" role="menu">
        <span class="who">{{ auth.me()?.email }}</span>
        <button type="button" role="menuitem" class="item danger" (click)="signOut()" [disabled]="busy()">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="M16 17l5-5-5-5M21 12H9"/></svg>
          {{ busy() ? 'Signing out…' : 'Sign out' }}
        </button>
      </div>
    }
  `,
  styles: `
    :host { position: relative; display: inline-block; }
    .kebab { width: 32px; height: 32px; border: 0; background: none; border-radius: 8px; display: grid; place-items: center; cursor: pointer;
      color: #475467; padding: 0; min-height: 0; }
    .kebab:hover, .kebab[aria-expanded='true'] { background: #f1f4f9; }
    .kebab svg { width: 16px; height: 16px; }
    .menu { position: absolute; right: 0; top: calc(100% + 6px); z-index: 50; min-width: 220px; background: #fff; border: 1px solid #e4e8ef;
      border-radius: 12px; box-shadow: 0 12px 32px -12px rgba(16, 24, 40, .28); padding: 6px; }
    .who { display: block; padding: 8px 10px 9px; font-size: 12px; color: #667085; border-bottom: 1px solid #eef1f5; margin-bottom: 4px;
      overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .item { width: 100%; display: flex; align-items: center; gap: 9px; padding: 9px 10px; border: 0; background: none; border-radius: 8px;
      font: inherit; font-size: 13.5px; font-weight: 600; cursor: pointer; text-align: left; min-height: 0; }
    .item svg { width: 15px; height: 15px; flex: none; }
    .item.danger { color: #d92d20; }
    .item:hover { background: #fef3f2; }
    .item:disabled { opacity: .6; cursor: default; }
  `,
})
export class OnboardingMenuComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly open = signal(false);
  protected readonly busy = signal(false);

  protected close(event: Event): void {
    // Inside Shadow DOM pages a document listener sees the page host as the target; the composed path has the real element.
    if (this.open() && !event.composedPath().some((n) => n instanceof HTMLElement && n.tagName === 'APP-ONBOARDING-MENU')) this.open.set(false);
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
