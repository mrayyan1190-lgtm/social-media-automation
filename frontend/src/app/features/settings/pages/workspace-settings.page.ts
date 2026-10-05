import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { Organization, OrganizationApiService } from '../../../core/services/organization-api.service';
import { WorkspaceService } from '../../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { supportedTimeZones } from '../../publications/models/time-zones';

/** Workspace name, default content language, time zone and the distinct-reviewer policy. Admins edit; others read. */
@Component({
  selector: 'app-workspace-settings-page',
  imports: [ReactiveFormsModule, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>Workspace Settings</h1><p class="muted">Name, time zone, language and approval rules for this workspace.</p></div></div>
    <app-problem-banner [problem]="problem()" (reload)="load()" />
    @if (saved()) { <p class="banner success" role="status">Workspace settings saved.</p> }
    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else if (organization()) {
      <form class="form-grid narrow" [formGroup]="form" (ngSubmit)="save()">
        <fieldset [disabled]="!workspace.isAdmin()">
          <legend>Workspace</legend>
          <label>Workspace Name
            <input formControlName="name" maxlength="200" />
            @if (form.controls.name.invalid && form.controls.name.touched) { <span class="field-error">A name is required.</span> }
          </label>
          <label>Default Language
            <input formControlName="defaultLanguage" placeholder="en or en-US" />
            <span class="hint">AI drafts are written in this language unless a campaign says otherwise.</span>
            @if (form.controls.defaultLanguage.invalid && form.controls.defaultLanguage.touched) { <span class="field-error">Use a language tag such as en or en-US.</span> }
          </label>
          <label>Time Zone
            <select formControlName="timeZoneId">
              @for (z of zones; track z) { <option [value]="z">{{ z }}</option> }
            </select>
            <span class="hint">Home and Planner times are shown in this zone. Scheduling starts from it.</span>
          </label>
        </fieldset>
        <fieldset [disabled]="!workspace.isAdmin()">
          <legend>Approval Settings</legend>
          <label class="checkbox wide">
            <input type="checkbox" formControlName="requireDistinctReviewer" />
            Require another team member to approve content
            <span class="hint">The person who wrote or last edited a post cannot approve it themselves.</span>
          </label>
        </fieldset>
        @if (workspace.isAdmin()) {
          <div class="actions">
            <button class="primary" type="submit" [disabled]="busy() || form.pristine">{{ busy() ? 'Saving…' : 'Save Settings' }}</button>
          </div>
        } @else {
          <p class="hint">Only Admins can change workspace settings.</p>
        }
      </form>
    }
  `,
})
export class WorkspaceSettingsPage {
  private readonly api = inject(OrganizationApiService);
  protected readonly workspace = inject(WorkspaceService);
  protected readonly zones = supportedTimeZones();
  protected readonly organization = signal<Organization | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    // Not shown: kept as saved (optional, used as brand context for AI).
    industry: [''],
    defaultLanguage: ['en', [Validators.required, Validators.pattern(/^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$/)]],
    timeZoneId: ['UTC'],
    requireDistinctReviewer: [true],
  });

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
    this.saved.set(false);
    this.problem.set(null);
    try {
      this.apply(await firstValueFrom(this.api.updateSettings(orgId, this.form.getRawValue(), current.rowVersion)));
      this.saved.set(true);
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
