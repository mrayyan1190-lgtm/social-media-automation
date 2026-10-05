import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { fromList, toList } from '../../../../shared/utilities/list-text';
import { httpUrlValidator } from '../../../../shared/validators/http-url.validator';
import { BRAND_TONES, BrandProfile, BrandProfileInput, BrandTone } from '../../models/brand-profile.model';
import { BrandProfileApiService } from '../../services/brand-profile-api.service';

@Component({
  selector: 'app-brand-settings-page',
  imports: [ReactiveFormsModule, ProblemBannerComponent],
  templateUrl: './brand-settings.page.html',
})
export class BrandSettingsPage {
  private readonly api = inject(BrandProfileApiService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly toneOptions = BRAND_TONES;
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  /** Null until a profile exists; sent as If-Match on every update. */
  protected readonly current = signal<BrandProfile | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    businessDescription: ['', [Validators.required, Validators.maxLength(2000)]],
    targetAudience: ['', [Validators.required, Validators.maxLength(2000)]],
    brandTone: ['Professional' as BrandTone, Validators.required],
    website: ['', httpUrlValidator],
    prohibitedTerms: [''],
  });

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) {
        void this.load(orgId);
      }
    });
    effect(() => (this.workspace.canManageBrand() ? this.form.enable() : this.form.disable()));
  }

  async load(orgId = this.workspace.orgId()): Promise<void> {
    if (!orgId) return;
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.apply(await firstValueFrom(this.api.get(orgId)));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.current.set(null);
        this.form.reset();
      } else {
        this.problem.set(toApiProblem(error));
      }
    } finally {
      this.loading.set(false);
    }
  }

  async save(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || this.form.invalid) return;
    this.saving.set(true);
    this.saved.set(false);
    this.problem.set(null);
    try {
      this.apply(await firstValueFrom(this.api.save(orgId, this.toInput(), this.current()?.rowVersion ?? null)));
      this.saved.set(true);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  private apply(profile: BrandProfile): void {
    this.current.set(profile);
    this.form.reset({ ...profile, website: profile.website ?? '', prohibitedTerms: fromList(profile.prohibitedTerms) });
  }

  private toInput(): BrandProfileInput {
    const v = this.form.getRawValue();
    return { ...v, website: v.website || null, prohibitedTerms: toList(v.prohibitedTerms) };
  }
}
