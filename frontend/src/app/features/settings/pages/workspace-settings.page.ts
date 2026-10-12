import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { Organization, OrganizationApiService } from '../../../core/services/organization-api.service';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { supportedTimeZones } from '../../publications/models/time-zones';
import { SocialConnectionsComponent } from '../../social-accounts/components/social-connections.component';

/** Offered content languages; a saved tag outside this list is still shown and kept. */
const LANGUAGES = [
  { code: 'en', name: 'English' },
  { code: 'ur', name: 'Urdu' },
  { code: 'ar', name: 'Arabic' },
  { code: 'es', name: 'Spanish' },
];

/**
 * Settings: workspace name, default content language, time zone and the social accounts.
 * Admins edit; others read. Provider sign-in returns here (via /social-accounts) with ?status=connected|failed.
 */
@Component({
  selector: 'app-workspace-settings-page',
  imports: [ReactiveFormsModule, SocialConnectionsComponent],
  templateUrl: './workspace-settings.page.html',
  styleUrl: './workspace-settings.page.css',
})
export class WorkspaceSettingsPage {
  private readonly api = inject(OrganizationApiService);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  protected readonly oauthStatus = this.query.get('status');
  protected readonly oauthMessage = this.query.get('message');
  /** Provider sign-in that came back unsuccessful (?status=failed). */
  protected readonly oauthFailure = this.oauthStatus === 'failed' ? `The connection did not complete: ${this.oauthMessage ?? 'please try again.'}` : null;
  protected readonly workspace = inject(WorkspaceService);
  protected readonly zones = supportedTimeZones();
  protected readonly organization = signal<Organization | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly savedOnce = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly toastText = signal<string | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    // Not shown: kept as saved (optional, used as brand context for AI).
    industry: [''],
    defaultLanguage: ['en', [Validators.required, Validators.pattern(/^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$/)]],
    timeZoneId: ['UTC'],
    // Not shown: the distinct-reviewer policy is kept as saved (no longer edited from Settings).
    requireDistinctReviewer: [true],
  });

  protected readonly languages = computed(() => {
    const current = this.organization()?.defaultLanguage;
    return current && !LANGUAGES.some((l) => l.code === current) ? [...LANGUAGES, { code: current, name: current }] : LANGUAGES;
  });

  constructor() {
    effect(() => {
      if (this.workspace.orgId()) void this.load();
    });
    effect(() => (this.workspace.isAdmin() ? this.form.enable() : this.form.disable()));
    inject(DestroyRef).onDestroy(() => clearTimeout(this.toastTimer));
  }

  protected toast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toastText.set(message);
    this.toastTimer = setTimeout(() => this.toastText.set(null), 2800);
  }

  /** Drops unsaved edits: back to the workspace as last loaded. */
  protected reset(): void {
    const organization = this.organization();
    if (!organization) return;
    this.apply(organization);
    this.toast('Unsaved changes reset');
  }

  async load(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.apply(await firstValueFrom(this.api.get(orgId)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    const orgId = this.workspace.orgId();
    const current = this.organization();
    this.form.markAllAsTouched();
    if (!orgId || !current || this.form.invalid) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      this.apply(await firstValueFrom(this.api.updateSettings(orgId, this.form.getRawValue(), current.rowVersion)));
      this.savedOnce.set(true);
      this.toast('Settings saved');
      await this.workspace.refreshOrganization(orgId);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  private apply(organization: Organization): void {
    this.organization.set(organization);
    const zone = this.zones.includes(organization.timeZoneId) ? organization.timeZoneId : this.zones[0];
    this.form.reset({
      name: organization.name,
      industry: organization.industry,
      defaultLanguage: organization.defaultLanguage,
      timeZoneId: zone,
      requireDistinctReviewer: organization.requireDistinctReviewer,
    });
  }
}
