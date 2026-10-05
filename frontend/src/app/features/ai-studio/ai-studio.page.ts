import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { CampaignContentComponent } from '../campaigns/components/campaign-content/campaign-content.component';
import { Campaign, ContentLength, EmojiMode, HashtagMode, SOCIAL_PLATFORMS, SocialPlatform } from '../campaigns/models/campaign.model';
import { CampaignApiService } from '../campaigns/services/campaign-api.service';
import { BRAND_TONES, BrandProfile, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';

const EXAMPLE = 'Create a launch campaign for our new AI reporting dashboard. Target small business owners and focus on reducing manual reporting. The CTA should encourage users to book a demo.';

/**
 * AI Studio: prompt-first creation. Brand description, audience and tone are applied automatically. Results go
 * through the same governed path as any campaign (saved as a draft campaign, one post per platform, new versions for
 * every rewrite, human review before scheduling); nothing is ever published from here.
 */
@Component({
  selector: 'app-ai-studio-page',
  imports: [FormsModule, RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent, CampaignContentComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>AI Studio</h1>
        <p class="muted">Describe what you need. We apply your brand profile automatically and write a separate post for each platform.</p>
      </div>
      @if (result()) {
        <button type="button" (click)="startOver()"><app-icon name="plus" [size]="16" />New prompt</button>
      }
    </div>
    <app-problem-banner [problem]="problem()" />

    @if (!result()) {
      <section class="studio card">
        <label class="studio-label" for="studio-prompt">What would you like to create?</label>
        <textarea id="studio-prompt" class="prompt" rows="6" [(ngModel)]="prompt" [placeholder]="example"></textarea>

        <div class="studio-controls">
          <div class="control">
            <span class="control-label">Platforms</span>
            <div class="chips">
              @for (p of platforms; track p) {
                <button type="button" class="chip" [class.on]="selected().includes(p)" [attr.aria-pressed]="selected().includes(p)" (click)="toggle(p)">
                  <app-platform-icon [platform]="p" [label]="true" />
                </button>
              }
            </div>
          </div>
          <label class="control">Tone
            <select [(ngModel)]="tone">
              <option value="">Use Brand Tone{{ brand()?.brandTone ? ' (' + brand()?.brandTone + ')' : '' }}</option>
              @for (t of tones; track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          <label class="control">Length
            <select [(ngModel)]="length"><option value="SHORT">Short</option><option value="MEDIUM">Medium</option><option value="LONG">Long</option></select>
          </label>
          <label class="control">Hashtags
            <select [(ngModel)]="hashtags"><option value="AUTO">Auto</option><option value="NONE">None</option></select>
          </label>
          <label class="control">Emojis
            <select [(ngModel)]="emojis"><option value="AUTO">Auto</option><option value="NONE">None</option></select>
          </label>
        </div>

        <div class="studio-footer">
          <span class="hint">
            Using brand: {{ brand()?.targetAudience ? 'audience “' + brand()?.targetAudience + '”' : 'your brand profile' }}.
            <a routerLink="/brand">Edit brand</a>
          </span>
          <button type="button" class="primary" (click)="create()" [disabled]="busy() || !prompt.trim() || !selected().length">
            <app-icon name="sparkles" [size]="16" />{{ busy() ? 'Starting…' : 'Generate' }}
          </button>
        </div>
      </section>
      <p class="hint">Results are saved as a draft campaign so you can edit, send for approval and schedule them. Nothing is published automatically.</p>
    } @else {
      @let c = result()!;
      <section class="card studio-result-head">
        <div>
          <p class="hint">Your prompt</p>
          <p class="pre">{{ c.description }}</p>
        </div>
        <div class="actions">
          <a class="button primary" [routerLink]="['/campaigns', c.id]">Use in Campaign</a>
        </div>
      </section>
      <app-campaign-content [orgId]="c.organizationId" [campaignId]="c.id" [platforms]="c.platforms" [editable]="true" [autoGenerate]="true" />
    }
  `,
})
export class AiStudioPage {
  private readonly campaigns = inject(CampaignApiService);
  private readonly brandApi = inject(BrandProfileApiService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly tones = BRAND_TONES;
  protected readonly example = EXAMPLE;
  protected readonly brand = signal<BrandProfile | null>(null);
  protected readonly selected = signal<SocialPlatform[]>(['LinkedIn']);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly result = signal<Campaign | null>(null);
  protected prompt = '';
  protected tone: BrandTone | '' = '';
  protected length: ContentLength = 'MEDIUM';
  protected hashtags: HashtagMode = 'AUTO';
  protected emojis: EmojiMode = 'AUTO';

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void firstValueFrom(this.brandApi.get(orgId)).then((b) => this.brand.set(b)).catch(() => this.brand.set(null));
    });
  }

  protected toggle(platform: SocialPlatform): void {
    this.selected.update((all) => (all.includes(platform) ? all.filter((p) => p !== platform) : [...all, platform]));
  }

  protected startOver(): void {
    this.result.set(null);
    this.prompt = '';
  }

  protected async create(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      const created = await firstValueFrom(this.campaigns.create(orgId, {
        name: nameFrom(this.prompt),
        description: this.prompt.trim(),
        objective: 'AUTO',
        targetAudience: null,
        callToAction: 'AUTO',
        callToActionUrl: null,
        toneOverride: this.tone || null,
        contentLength: this.length,
        hashtagMode: this.hashtags,
        emojiMode: this.emojis,
      }));
      const platforms = SOCIAL_PLATFORMS.filter((p) => this.selected().includes(p));
      this.result.set(await firstValueFrom(this.campaigns.setPlatforms(orgId, created.id, platforms, created.rowVersion)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/** A readable campaign name from the prompt's first words; it can be renamed in the campaign. */
function nameFrom(prompt: string): string {
  const words = prompt.trim().replace(/\s+/g, ' ').split(' ').slice(0, 8).join(' ');
  return `AI Studio: ${words.length > 80 ? words.slice(0, 80) + '…' : words}`;
}
