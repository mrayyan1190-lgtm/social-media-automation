import { Component, effect, input, output, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { inject } from '@angular/core';
import { PlatformIconComponent } from '../../../../shared/components/platform-icon.component';
import { httpUrlValidator } from '../../../../shared/validators/http-url.validator';
import { BRAND_TONES, BrandProfile, BrandTone } from '../../../settings/models/brand-profile.model';
import {
  CALL_TO_ACTIONS,
  CAMPAIGN_OBJECTIVES,
  CTA_LABELS,
  CallToActionType,
  Campaign,
  CampaignInput,
  CampaignObjective,
  ContentLength,
  EmojiMode,
  HashtagMode,
  OBJECTIVE_LABELS,
  SOCIAL_PLATFORMS,
  SocialPlatform,
  ctaNeedsUrl,
} from '../../models/campaign.model';

export interface CampaignFormValue {
  input: CampaignInput;
  platforms: SocialPlatform[];
}

/** Prefill for a new campaign (e.g. from the empty state or AI Studio). */
export interface CampaignDraft {
  name?: string;
  prompt?: string;
  platforms?: SocialPlatform[];
}

function atLeastOnePlatform(group: AbstractControl): ValidationErrors | null {
  return Object.values(group.value as Record<string, boolean>).some(Boolean) ? null : { platformRequired: true };
}

/**
 * AI-first campaign input: name, prompt and platforms. Audience and tone come from the saved brand profile unless the
 * user chooses "Change for this campaign"; objective, CTA and style options stay under Advanced Options.
 * Mirrors the API rules for early feedback only; the API stays authoritative.
 */
@Component({
  selector: 'app-campaign-form',
  imports: [ReactiveFormsModule, PlatformIconComponent],
  template: `
    <form class="campaign-form" [formGroup]="form" (ngSubmit)="submit()">
      <label>Campaign Name *
        <input formControlName="name" placeholder="e.g. AI Reporting Dashboard Launch" />
        @if (form.controls.name.touched && form.controls.name.invalid) { <span class="field-error">Campaign name is required.</span> }
      </label>

      <label>Campaign Prompt *
        <span class="hint">Tell AI what you want to communicate: what, for whom, key details and what readers should do.</span>
        <textarea class="prompt" rows="6" formControlName="description"
          placeholder="We are launching our AI reporting dashboard on October 20. It automatically generates weekly reports and supports PDF and Excel. Target small businesses and encourage them to book a demo."></textarea>
        @if (form.controls.description.touched && form.controls.description.invalid) { <span class="field-error">Describe what this campaign should communicate.</span> }
      </label>

      <fieldset class="plain" formGroupName="platforms">
        <legend>Platforms *</legend>
        <div class="platform-picker">
          @for (p of platforms; track p) {
            <label class="platform-option">
              <input type="checkbox" [formControlName]="p" />
              <app-platform-icon [platform]="p" [label]="true" />
            </label>
          }
        </div>
        @if (form.controls.platforms.touched && form.controls.platforms.invalid) { <span class="field-error">Choose at least one platform.</span> }
      </fieldset>

      <section class="brand-context">
        <div class="card-head">
          <strong>Using Brand Profile</strong>
          @if (!overrideOpen()) {
            <button type="button" class="link" (click)="overrideOpen.set(true)" [disabled]="readonly()">Change for this campaign</button>
          } @else {
            <button type="button" class="link" (click)="useBrand()" [disabled]="readonly()">Use brand profile</button>
          }
        </div>
        @if (!overrideOpen()) {
          <dl class="facts-grid">
            <dt>Audience</dt><dd>{{ brand()?.targetAudience || 'Not set' }}</dd>
            <dt>Tone</dt><dd>{{ brand()?.brandTone || 'Not set' }}</dd>
          </dl>
        } @else {
          <label>Audience for this campaign
            <textarea rows="2" formControlName="targetAudience"></textarea>
          </label>
          <label>Tone for this campaign
            <select formControlName="toneOverride">
              <option value="">Use brand tone ({{ brand()?.brandTone }})</option>
              @for (t of tones; track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
        }
      </section>

      <details class="advanced" [open]="advancedOpen()">
        <summary>Advanced Options</summary>
        <div class="advanced-grid">
          <label>Objective
            <select formControlName="objective">
              @for (o of objectives; track o) { <option [value]="o">{{ objectiveLabels[o] }}</option> }
            </select>
          </label>
          <label>CTA
            <select formControlName="callToAction">
              @for (c of ctas; track c) { <option [value]="c">{{ ctaLabels[c] }}</option> }
            </select>
          </label>
          @if (ctaNeedsUrl(form.controls.callToAction.value)) {
            <label class="wide">CTA URL *
              <input type="url" formControlName="callToActionUrl" placeholder="https://" />
              @if (form.controls.callToActionUrl.hasError('httpUrl')) { <span class="field-error">Enter a full address such as https://example.com.</span> }
            </label>
          }
          <label>Content Length
            <select formControlName="contentLength">
              <option value="SHORT">Short</option><option value="MEDIUM">Medium</option><option value="LONG">Long</option>
            </select>
          </label>
          <label>Hashtags
            <select formControlName="hashtagMode"><option value="AUTO">Auto</option><option value="NONE">None</option></select>
          </label>
          <label>Emojis
            <select formControlName="emojiMode"><option value="AUTO">Auto</option><option value="NONE">None</option></select>
          </label>
        </div>
      </details>

      @if (!readonly()) {
        <div class="actions end">
          <ng-content />
          <button class="primary" type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : submitLabel() }}</button>
        </div>
      }
    </form>
  `,
})
export class CampaignFormComponent {
  readonly initial = input<Campaign | null>(null);
  readonly draft = input<CampaignDraft | null>(null);
  readonly brand = input<BrandProfile | null>(null);
  readonly readonly = input(false);
  readonly saving = input(false);
  readonly submitLabel = input('Save');
  readonly save = output<CampaignFormValue>();

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly objectives = CAMPAIGN_OBJECTIVES;
  protected readonly objectiveLabels = OBJECTIVE_LABELS;
  protected readonly ctas = CALL_TO_ACTIONS;
  protected readonly ctaLabels = CTA_LABELS;
  protected readonly tones = BRAND_TONES;
  protected readonly ctaNeedsUrl = ctaNeedsUrl;
  protected readonly overrideOpen = signal(false);
  protected readonly advancedOpen = signal(false);

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.required, Validators.maxLength(2000)]],
    platforms: this.fb.group({ LinkedIn: [false], Facebook: [false], Instagram: [false] }, { validators: atLeastOnePlatform }),
    targetAudience: ['', Validators.maxLength(2000)],
    toneOverride: ['' as BrandTone | ''],
    objective: ['AUTO' as CampaignObjective],
    callToAction: ['AUTO' as CallToActionType],
    callToActionUrl: ['', httpUrlValidator],
    contentLength: ['MEDIUM' as ContentLength],
    hashtagMode: ['AUTO' as HashtagMode],
    emojiMode: ['AUTO' as EmojiMode],
  });

  constructor() {
    effect(() => {
      const campaign = this.initial();
      if (!campaign) return;
      const audienceChanged = !!this.brand() && campaign.targetAudience !== this.brand()?.targetAudience;
      this.overrideOpen.set(audienceChanged || !!campaign.toneOverride);
      this.advancedOpen.set(campaign.objective !== 'AUTO' || campaign.callToAction !== 'AUTO' || campaign.contentLength !== 'MEDIUM'
        || campaign.hashtagMode !== 'AUTO' || campaign.emojiMode !== 'AUTO');
      this.form.reset({
        name: campaign.name,
        description: campaign.description,
        platforms: Object.fromEntries(SOCIAL_PLATFORMS.map((p) => [p, campaign.platforms.includes(p)])),
        targetAudience: campaign.targetAudience,
        toneOverride: campaign.toneOverride ?? '',
        objective: campaign.objective,
        callToAction: campaign.callToAction,
        callToActionUrl: campaign.callToActionUrl ?? '',
        contentLength: campaign.contentLength,
        hashtagMode: campaign.hashtagMode,
        emojiMode: campaign.emojiMode,
      });
    });
    effect(() => {
      const draft = this.draft();
      if (!draft || this.initial()) return;
      this.form.patchValue({
        name: draft.name ?? '',
        description: draft.prompt ?? '',
        platforms: Object.fromEntries(SOCIAL_PLATFORMS.map((p) => [p, draft.platforms?.includes(p) ?? false])),
      });
    });
    effect(() => {
      // The override starts from the brand audience so the user edits rather than retypes it.
      if (this.overrideOpen() && !this.form.controls.targetAudience.value) this.form.controls.targetAudience.setValue(this.brand()?.targetAudience ?? '');
    });
    effect(() => (this.readonly() ? this.form.disable() : this.form.enable()));
  }

  protected useBrand(): void {
    this.overrideOpen.set(false);
    this.form.patchValue({ targetAudience: '', toneOverride: '' });
  }

  protected submit(): void {
    const v = this.form.getRawValue();
    if (this.form.invalid || (ctaNeedsUrl(v.callToAction) && !v.callToActionUrl.trim())) {
      this.form.markAllAsTouched();
      if (ctaNeedsUrl(v.callToAction) && !v.callToActionUrl.trim()) this.form.controls.callToActionUrl.setErrors({ required: true });
      return;
    }
    const override = this.overrideOpen();
    this.save.emit({
      input: {
        name: v.name,
        description: v.description,
        objective: v.objective,
        // Null means "use the brand profile's audience"; the server resolves it.
        targetAudience: override ? v.targetAudience.trim() || null : null,
        toneOverride: override ? v.toneOverride || null : null,
        callToAction: v.callToAction,
        callToActionUrl: ctaNeedsUrl(v.callToAction) ? v.callToActionUrl.trim() || null : null,
        contentLength: v.contentLength,
        hashtagMode: v.hashtagMode,
        emojiMode: v.emojiMode,
      },
      platforms: SOCIAL_PLATFORMS.filter((p) => v.platforms[p]),
    });
  }
}
