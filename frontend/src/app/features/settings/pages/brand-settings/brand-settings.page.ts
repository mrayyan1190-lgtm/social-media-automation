import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { ThemeOption, ThemeSelectComponent } from '../../../../shared/components/theme-select.component';
import { toList } from '../../../../shared/utilities/list-text';
import { httpUrlValidator } from '../../../../shared/validators/http-url.validator';
import { BRAND_TONES, BrandProfile, BrandProfileInput, BrandTone } from '../../models/brand-profile.model';
import { BrandProfileApiService } from '../../services/brand-profile-api.service';

@Component({
  selector: 'app-brand-settings-page',
  imports: [ReactiveFormsModule, ThemeSelectComponent],
  templateUrl: './brand-settings.page.html',
  styleUrl: './brand-settings.page.css',
})
export class BrandSettingsPage {
  private readonly api = inject(BrandProfileApiService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly toneOptions: ThemeOption[] = BRAND_TONES.map((t) => ({ value: t, label: t }));
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly toastText = signal<string | null>(null);
  private toastTimer?: ReturnType<typeof setTimeout>;
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

  /** Words to avoid as chips under the field. */
  private readonly avoidText = signal('');
  protected readonly avoidList = computed(() => toList(this.avoidText()));

  constructor() {
    this.form.controls.prohibitedTerms.valueChanges.subscribe((v) => this.avoidText.set(v));
    inject(DestroyRef).onDestroy(() => clearTimeout(this.toastTimer));
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

  protected setTone(tone: string): void {
    if (!this.workspace.canManageBrand()) return;
    this.form.controls.brandTone.setValue(tone as BrandTone);
    this.form.markAsDirty();
  }

  /** Drops unsaved edits: back to the profile as last loaded (or an empty form). */
  protected reset(): void {
    const profile = this.current();
    if (profile) this.apply(profile);
    else this.form.reset();
    this.toast('Unsaved changes reset');
  }

  private toast(message: string): void {
    clearTimeout(this.toastTimer);
    this.toastText.set(message);
    this.toastTimer = setTimeout(() => this.toastText.set(null), 2600);
  }

  async save(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || this.form.invalid) return;
    this.saving.set(true);
    this.problem.set(null);
    try {
      this.apply(await firstValueFrom(this.api.save(orgId, this.toInput(), this.current()?.rowVersion ?? null)));
      this.toast('Brand profile saved');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.saving.set(false);
    }
  }

  private apply(profile: BrandProfile): void {
    this.current.set(profile);
    this.form.reset({ ...profile, website: profile.website ?? '', prohibitedTerms: profile.prohibitedTerms.join(', ') });
  }

  private toInput(): BrandProfileInput {
    const v = this.form.getRawValue();
    return { ...v, website: v.website || null, prohibitedTerms: toList(v.prohibitedTerms) };
  }
}
