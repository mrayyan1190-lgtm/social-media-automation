import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';

/** The signed-in user: name (editable), email, time zone and workspaces. */
@Component({
  selector: 'app-profile-page',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="profile-page tw-page">
    <main class="relative mx-auto max-w-[860px] px-4 pb-14 pt-7 sm:px-6 lg:px-9 lg:pt-8">
      <header class="mb-5 flex items-center gap-3.5 lg:mb-6">
        <span class="grid h-[49px] w-[49px] shrink-0 place-items-center rounded-[17px] bg-[#1B3B5F] text-white shadow-[0_13px_26px_#284d7733]">
          <svg class="h-6 w-6" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/></svg>
        </span>
        <div><p class="text-[10px] font-extrabold uppercase tracking-[.20em] text-[#7389AA]">Your account</p><h1 class="heading-font text-[27px] font-extrabold leading-[1.15] tracking-[-.065em] text-[#193552] sm:text-[31px]">Profile<span class="text-[#9D8CC6]">.</span></h1></div>
      </header>

      @if (problem(); as p) { <p class="problem mb-4 rounded-[16px] px-4 py-3 text-[12px] font-bold leading-5" role="alert">{{ p.detail }}</p> }
      @if (saved()) { <p class="ok mb-4 rounded-[16px] px-4 py-3 text-[12px] font-bold" role="status">Profile saved.</p> }

      @if (auth.me(); as me) {
        <form class="glass overflow-hidden rounded-[28px]" (ngSubmit)="save()">
          <div class="head px-5 py-4 sm:px-6"><h2 class="heading-font text-[16px] font-extrabold tracking-[-.035em] text-[#2C496A] sm:text-[17px]">Personal details</h2></div>
          <div class="grid gap-4 px-5 py-5 sm:grid-cols-2 sm:px-6">
            <label class="sm:col-span-2"><span class="lbl">Full Name</span><input class="fld" name="fullName" required maxlength="200" [(ngModel)]="fullName" /></label>
            <label><span class="lbl">Email</span><input class="fld" [value]="me.email" disabled /></label>
            <label><span class="lbl">Time Zone</span><input class="fld" [value]="workspace.timeZone()" disabled /></label>
            <p class="text-[11px] font-semibold text-[#8D9BB1] sm:col-span-2">Times follow your workspace time zone.@if (workspace.isAdmin()) { <a routerLink="/settings/workspace" class="ml-1 font-extrabold text-[#4579aa] underline">Change it in Workspace Settings</a>. }</p>
          </div>
          <div class="flex justify-end border-t border-[#E7EDF6] px-5 py-4 sm:px-6">
            <button class="primary-btn inline-flex h-[42px] items-center rounded-full px-6 text-[12px] font-extrabold disabled:opacity-60" type="submit" [disabled]="busy() || !fullName.trim() || !workspace.organization()">{{ busy() ? 'Saving…' : 'Save' }}</button>
          </div>
        </form>

        <section class="glass mt-5 overflow-hidden rounded-[28px]">
          <div class="head flex items-center gap-2.5 px-5 py-4 sm:px-6"><h2 class="heading-font text-[16px] font-extrabold tracking-[-.035em] text-[#2C496A] sm:text-[17px]">Workspaces</h2><span class="rounded-full border border-white/90 bg-white/85 px-2.5 py-1 text-[10px] font-extrabold text-[#6885A6]">{{ me.memberships.length }}</span></div>
          <ul class="px-5 py-2 sm:px-6">
            @for (m of me.memberships; track m.organizationId) {
              <li class="flex items-center gap-3 border-b border-[#EEF2F7] py-3 last:border-b-0">
                <span class="grid h-9 w-9 place-items-center rounded-[12px] bg-[linear-gradient(140deg,#dcecff,#ece6fb)] text-[13px] font-extrabold text-[#476d9b]">{{ m.organizationName.charAt(0) }}</span>
                <span class="grow text-[13px] font-bold text-[#28466a]">{{ m.organizationName }}</span>
                <span class="rounded-full bg-[#EAF2FC] px-2.5 py-1 text-[10px] font-extrabold text-[#4D75A6]">{{ m.role }}</span>
              </li>
            }
          </ul>
        </section>

        <button type="button" (click)="signOut()" class="mt-5 inline-flex h-[42px] items-center gap-2 rounded-full border border-[#F0D5DA] bg-white/80 px-5 text-[12px] font-extrabold text-[#B85567] transition hover:bg-[#FFF4F5]">Sign Out</button>
      }
    </main>
    </div>
  `,
  styles: `
    .heading-font { font-family: Manrope, 'DM Sans', sans-serif; }
    .glass { background: rgba(255, 255, 255, .84); border: 1px solid rgba(255, 255, 255, .93); box-shadow: 0 16px 46px rgba(51, 75, 118, .085); backdrop-filter: blur(18px); }
    .head { border-bottom: 1px solid #E7EDF6; background: linear-gradient(110deg, #F1F8FF 0%, #F6F1FC 54%, #FFF6F2 100%); }
    .lbl { display: block; margin-bottom: 6px; font-size: 12px; font-weight: 800; color: #263E59; }
    .fld { width: 100%; height: 44px; padding: 0 14px; border: 1px solid #DBE6F1; border-radius: 12px; background: #F7FAFE; font: inherit; font-size: 13px; font-weight: 600; color: #1A3451; outline: none; transition: border-color .2s, box-shadow .2s; }
    .fld:focus { border-color: #99B9D7; background: #fff; box-shadow: 0 0 0 4px #BCD7EF59; }
    .fld:disabled { color: #7d91ab; background: #F1F5FA; cursor: not-allowed; }
    .primary-btn { background: linear-gradient(113deg, #234b80, #456fbc 62%, #756ab9); color: #fff; box-shadow: 0 9px 22px rgba(54, 91, 158, .20); transition: transform .18s; }
    .primary-btn:hover:not(:disabled) { transform: translateY(-2px); }
    .problem { color: #BD5066; background: #FFF4F5; border: 1px solid #F5D6DB; }
    .ok { color: #287f67; background: #eaf9f2; border: 1px solid #d8f3e5; }
  `,
})
export class ProfilePage {
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected fullName = '';

  constructor() {
    effect(() => (this.fullName = this.auth.me()?.displayName ?? ''));
  }

  protected async save(): Promise<void> {
    const organization = this.workspace.organization();
    if (!organization) return;
    this.busy.set(true);
    this.saved.set(false);
    this.problem.set(null);
    try {
      // The profile endpoint also carries the workspace time zone; send the current one so it never changes from here.
      await this.auth.completeProfile({ organizationId: organization.id, fullName: this.fullName.trim(), timeZoneId: organization.timeZoneId });
      this.saved.set(true);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
