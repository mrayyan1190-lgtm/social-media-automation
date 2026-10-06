import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { Member, OrganizationApiService } from '../../../core/services/organization-api.service';
import { ToastService } from '../../../core/services/toast.service';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';

const MIN_PASSWORD = 8;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const NO_ERRORS = { name: false, email: false, password: false, confirm: false };

/**
 * Members (Admin only). The Admin creates Creator logins and hands the credentials over; there is no invitation email
 * in the MVP. The API enforces the Admin permission, the unique email and the last-Admin rule.
 */
@Component({
  selector: 'app-team-page',
  imports: [FormsModule, ProblemBannerComponent],
  templateUrl: './team.page.html',
  styleUrl: './team.page.css',

})
export class TeamPage {
  private readonly api = inject(OrganizationApiService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly toast = inject(ToastService);
  private readonly dialogRef = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly minPassword = MIN_PASSWORD;
  protected readonly members = signal<Member[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  /** Server-side reason the creator could not be created (e.g. the email is taken). */
  protected readonly formProblem = signal<string | null>(null);
  protected readonly errors = signal(NO_ERRORS);
  protected readonly activeCount = computed(() => this.members().filter((m) => m.status === 'Active').length);
  protected fullName = '';
  protected email = '';
  protected password = '';
  protected confirmPassword = '';

  constructor() {
    effect(() => {
      if (this.workspace.orgId()) void this.load();
    });
  }

  async load(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.members.set(await firstValueFrom(this.api.members(orgId)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected initial(m: Member): string {
    return (m.displayName || m.email || '?').trim().charAt(0).toUpperCase();
  }

  protected openAdd(): void {
    this.fullName = this.email = this.password = this.confirmPassword = '';
    this.resetErrors();
    this.dialogRef().nativeElement.showModal();
  }

  protected resetErrors(): void {
    this.errors.set(NO_ERRORS);
    this.formProblem.set(null);
  }

  /** A click on the dimmed backdrop (the dialog element itself, outside the form) closes it. */
  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialogRef().nativeElement) this.dialogRef().nativeElement.close();
  }

  protected async addCreator(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    const fullName = this.fullName.trim();
    const email = this.email.trim();
    // Convenience checks only; the API validates name, email, password length and uniqueness again.
    const errors = {
      name: fullName.length < 2,
      email: !EMAIL.test(email),
      password: this.password.length < MIN_PASSWORD,
      confirm: !this.confirmPassword || this.confirmPassword !== this.password,
    };
    this.errors.set(errors);
    this.formProblem.set(null);
    if (Object.values(errors).some(Boolean)) return;

    this.busy.set('add');
    this.formProblem.set(null);
    try {
      const created = await firstValueFrom(this.api.createCreator(orgId, { fullName, email, password: this.password }));
      this.dialogRef().nativeElement.close();
      this.toast.success(`${created.displayName} added as Creator. They sign in with ${created.email}.`);
      await this.load();
    } catch (error) {
      this.formProblem.set(toApiProblem(error).detail);
    } finally {
      this.busy.set(null);
    }
  }

  protected async deactivate(member: Member): Promise<void> {
    if (!confirm(`Deactivate ${member.displayName || member.email}? They lose access to this workspace. Their content stays.`)) return;
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.busy.set(member.memberId);
    this.problem.set(null);
    try {
      const updated = await firstValueFrom(this.api.deactivate(orgId, member));
      this.toast.success(`${updated.displayName || updated.email} deactivated`);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
      await this.load();
    }
  }
}
