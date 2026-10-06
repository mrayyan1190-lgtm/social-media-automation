import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { Campaign, ContentLength, EmojiMode, HashtagMode, SOCIAL_PLATFORMS, SocialPlatform } from '../campaigns/models/campaign.model';
import { CampaignApiService } from '../campaigns/services/campaign-api.service';
import { AiGenerationRequest, RewriteAction } from '../content/models/ai.model';
import { Post, postText } from '../content/models/post.model';
import { AiApiService } from '../content/services/ai-api.service';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { BRAND_TONES, BrandProfile, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';

const EXAMPLE = 'Create launch posts for our new AI reporting dashboard. Focus on helping small businesses save reporting time and encourage people to book a demo.';

const RESULT_ACTIONS: { value: RewriteAction; label: string }[] = [
  { value: 'Rewrite', label: 'Rewrite' },
  { value: 'Shorten', label: 'Shorten' },
  { value: 'ImproveHook', label: 'Improve Hook' },
  { value: 'Regenerate', label: 'Regenerate' },
];

interface Result {
  post: Post;
  /** What is running for this result: an AI edit label, or 'image'. */
  busy: string | null;
  imageFailed: boolean;
  error: string | null;
}

/**
 * AI Studio: a prompt becomes one draft post per platform, written with the saved brand profile. Each draft can be
 * refined here or opened in Composer. Nothing is approved, scheduled or published from this page.
 */
@Component({
  selector: 'app-ai-studio-page',
  imports: [FormsModule, RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>AI Studio</h1>
        <p class="muted">Describe what you need. AI writes a separate post for each platform using your brand profile.</p>
      </div>
      @if (results().length || request()) {
        <button type="button" (click)="startOver()"><app-icon name="plus" [size]="16" />New prompt</button>
      }
    </div>
    <app-problem-banner [problem]="problem()" />

    @if (!request()) {
      <section class="card studio">
        <label class="studio-label" for="studio-prompt">What would you like to create? *</label>
        <textarea id="studio-prompt" class="prompt" rows="5" maxlength="2000" [(ngModel)]="prompt" [placeholder]="example"></textarea>

        <div class="studio-row">
          <div class="field">
            <span class="field-label">Platforms *</span>
            <div class="chips">
              @for (p of platforms; track p) {
                <button type="button" class="chip lg" [class.on]="selected().includes(p)" [attr.aria-pressed]="selected().includes(p)" (click)="toggle(p)">
                  <app-platform-icon [platform]="p" [label]="true" />
                </button>
              }
            </div>
          </div>
          <div class="field">
            <span class="field-label">Output Type</span>
            <div class="segmented" role="group" aria-label="Output type">
              <button type="button" [class.on]="!withImage()" (click)="withImage.set(false)">Text</button>
              <button type="button" [class.on]="withImage()" [disabled]="!imageAvailable()" [title]="imageAvailable() ? '' : 'Image generation is not available right now'"
                (click)="withImage.set(true)"><app-icon name="image" [size]="15" />Text + Image</button>
            </div>
          </div>
        </div>

        @if (withImage()) {
          @if (customImage()) {
            <label>Image Prompt <span class="hint">optional · describe the visual you want</span>
              <textarea rows="2" maxlength="500" [(ngModel)]="imagePrompt" placeholder="A clean dashboard on a laptop in a bright office"></textarea>
            </label>
          } @else {
            <button type="button" class="link" (click)="customImage.set(true)">Customize Image Prompt</button>
            <p class="hint tight">The image is generated from your prompt and the written post.</p>
          }
        }

        <div class="brand-strip">
          @if (brand(); as b) {
            <span class="ok-mark"><app-icon name="check" [size]="14" />Using Brand Profile</span>
            <span><span class="hint">Audience:</span> {{ b.targetAudience }}</span>
            <span><span class="hint">Tone:</span> {{ tone || b.brandTone }}</span>
            <a routerLink="/brand" class="hint">Edit</a>
          } @else if (brandLoaded()) {
            <span class="field-error">Complete your <a routerLink="/brand">Brand profile</a> first; AI uses it for every post.</span>
          }
        </div>

        @if (campaign(); as c) {
          <p class="campaign-chip"><span class="color-dot" [style.background]="c.color"></span>Posts will be added to <strong>{{ c.name }}</strong></p>
        }

        <details class="advanced">
          <summary>Advanced Options</summary>
          <div class="advanced-grid">
            <label>Tone
              <select [(ngModel)]="tone">
                <option value="">Use Brand Tone</option>
                @for (t of tones; track t) { <option [value]="t">{{ t }}</option> }
              </select>
            </label>
            <label>Length
              <select [(ngModel)]="length"><option value="SHORT">Short</option><option value="MEDIUM">Medium</option><option value="LONG">Long</option></select>
            </label>
            <label>Hashtags
              <select [(ngModel)]="hashtags"><option value="AUTO">Auto</option><option value="NONE">None</option></select>
            </label>
            <label>Emojis
              <select [(ngModel)]="emojis"><option value="AUTO">Auto</option><option value="NONE">None</option></select>
            </label>
          </div>
        </details>

        <div class="studio-footer">
          <span class="hint">Nothing is published automatically. Every post goes through review.</span>
          <button type="button" class="primary" (click)="generate()" [disabled]="busy() || !prompt.trim() || !selected().length || !brand()">
            <app-icon name="sparkles" [size]="16" />{{ busy() ? 'Starting…' : 'Generate' }}
          </button>
        </div>
      </section>
    } @else {
      <section class="card prompt-recap">
        <span class="hint">Prompt</span>
        <p class="pre">{{ promptShown() }}</p>
      </section>

      @if (generating()) {
        <section class="card generating" role="status">
          <span class="spinner" aria-hidden="true"></span>
          Writing {{ request()?.platforms?.join(', ') }} posts… this usually takes a few seconds.
        </section>
      } @else if (request()?.status === 'FAILED') {
        <section class="banner error" role="alert">
          {{ failureText() }}
          <button type="button" (click)="retry()">Try again</button>
        </section>
      }

      <div class="result-grid">
        @for (r of results(); track r.post.id) {
          <article class="card result-card">
            <header class="result-head">
              <app-platform-icon [platform]="r.post.platform" [label]="true" />
              <span class="badge muted">Draft · v{{ r.post.currentVersion.versionNumber }}</span>
            </header>
            <p class="pre result-text">{{ text(r.post) }}</p>
            @if (r.post.currentVersion.media; as m) {
              <img class="result-image" [src]="previewUrl(m.id)" [alt]="r.post.currentVersion.altText || 'Generated image'" />
            } @else if (r.busy === 'image') {
              <div class="image-placeholder"><span class="spinner" aria-hidden="true"></span>Generating image…</div>
            } @else if (r.imageFailed) {
              <p class="hint">The image could not be generated. You can try again in Composer.</p>
            }
            @if (r.error) { <p class="field-error" role="alert">{{ r.error }}</p> }
            <div class="ai-actions">
              @for (a of actions; track a.value) {
                <button type="button" class="chip" [disabled]="!!r.busy" (click)="refine(r, a.value)">{{ r.busy === a.label ? a.label + '…' : a.label }}</button>
              }
            </div>
            <div class="result-foot">
              <a class="button primary" [routerLink]="['/composer', r.post.id]">Create Post</a>
            </div>
          </article>
        }
      </div>
      @if (results().length) {
        <p class="hint">Each result is saved as a draft. “Create Post” opens it in Composer to edit, pick the account and send for review.</p>
      }
    }
  `,
})
export class AiStudioPage {
  private readonly ai = inject(AiApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly media = inject(MediaApiService);
  private readonly campaignsApi = inject(CampaignApiService);
  private readonly brandApi = inject(BrandProfileApiService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);
  private destroyed = false;

  /** Query parameters: the campaign the drafts are filed in, and a generation to show again (after Back from Composer). */
  readonly campaignId = input<string>();
  readonly requestId = input<string>(undefined, { alias: 'request' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly tones = BRAND_TONES;
  protected readonly example = EXAMPLE;
  protected readonly actions = RESULT_ACTIONS;
  protected readonly brand = signal<BrandProfile | null>(null);
  protected readonly brandLoaded = signal(false);
  protected readonly campaign = signal<Campaign | null>(null);
  protected readonly imageAvailable = signal(false);
  protected readonly selected = signal<SocialPlatform[]>(['LinkedIn']);
  protected readonly withImage = signal(false);
  protected readonly customImage = signal(false);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly request = signal<AiGenerationRequest | null>(null);
  protected readonly results = signal<Result[]>([]);
  protected readonly promptShown = signal('');
  protected readonly generating = computed(() => this.request()?.status === 'PENDING' || this.request()?.status === 'PROCESSING');
  protected prompt = '';
  protected imagePrompt = '';
  protected tone: BrandTone | '' = '';
  protected length: ContentLength = 'MEDIUM';
  protected hashtags: HashtagMode = 'AUTO';
  protected emojis: EmojiMode = 'AUTO';

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
    effect(() => {
      const orgId = this.workspace.orgId();
      if (!orgId) return;
      void firstValueFrom(this.brandApi.get(orgId)).then((b) => this.brand.set(b), () => this.brand.set(null)).finally(() => this.brandLoaded.set(true));
      void firstValueFrom(this.ai.capabilities(orgId)).then((c) => this.imageAvailable.set(c.imageGeneration), () => this.imageAvailable.set(false));
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      const campaignId = this.campaignId();
      if (orgId && campaignId) void firstValueFrom(this.campaignsApi.get(orgId, campaignId)).then((c) => this.campaign.set(c), () => this.campaign.set(null));
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      const requestId = this.requestId();
      // Only the query parameter triggers a restore; clearing the results must not reload the old request.
      if (orgId && requestId && !untracked(this.request)) void this.restore(orgId, requestId);
    });
  }

  protected toggle(platform: SocialPlatform): void {
    this.selected.update((all) => (all.includes(platform) ? all.filter((p) => p !== platform) : [...all, platform]));
  }

  protected text(post: Post): string {
    return postText(post.currentVersion);
  }

  protected previewUrl(mediaAssetId: string): string {
    return this.media.previewUrl(this.workspace.orgId()!, mediaAssetId);
  }

  protected failureText(): string {
    const code = this.request()?.failureCode ?? '';
    return code === 'AI_NOT_CONFIGURED' || code === 'AI_PROVIDER_AUTH'
      ? 'AI writing is temporarily unavailable. Please try again later.'
      : 'The posts could not be generated. Please try again.';
  }

  protected startOver(): void {
    this.request.set(null);
    this.results.set([]);
    this.problem.set(null);
    void this.router.navigate([], { queryParams: { request: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected retry(): void {
    this.prompt = this.promptShown();
    this.startOver();
  }

  protected async generate(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      const queued = await firstValueFrom(this.ai.requestGeneration(orgId, {
        prompt: this.prompt.trim(),
        platforms: SOCIAL_PLATFORMS.filter((p) => this.selected().includes(p)),
        campaignId: this.campaign()?.id ?? null,
        tone: this.tone || null,
        length: this.length,
        hashtags: this.hashtags,
        emojis: this.emojis,
      }));
      this.promptShown.set(this.prompt.trim());
      this.results.set([]);
      this.request.set(queued);
      // Keeps the results reachable with Back after opening a draft in Composer.
      void this.router.navigate([], { queryParams: { request: queued.id }, queryParamsHandling: 'merge', replaceUrl: true });
      await this.follow(orgId, queued, this.withImage());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** AI edit of one draft: a new version of the same post (the previous version stays in its history). */
  protected async refine(result: Result, action: RewriteAction): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    const label = this.actions.find((a) => a.value === action)?.label ?? action;
    this.patch(result.post.id, { busy: label, error: null });
    try {
      const queued = await firstValueFrom(this.ai.requestRewrite(orgId, result.post.id, action, null, result.post.rowVersion));
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed);
      if (final.status === 'FAILED') this.patch(result.post.id, { error: 'That edit did not complete. Please try again.' });
      await this.reloadPost(orgId, result.post.id);
    } catch (error) {
      this.patch(result.post.id, { error: toApiProblem(error).detail });
    } finally {
      this.patch(result.post.id, { busy: null });
    }
  }

  private async restore(orgId: string, requestId: string): Promise<void> {
    try {
      const request = await firstValueFrom(this.ai.get(orgId, requestId));
      if (request.kind !== 'Generation') return;
      this.request.set(request);
      await this.follow(orgId, request, false);
    } catch {
      // An old or foreign link: just show an empty studio.
      this.request.set(null);
    }
  }

  /** Waits for the drafts, shows them, and (Text + Image) asks for one image per draft. */
  private async follow(orgId: string, queued: AiGenerationRequest, withImage: boolean): Promise<void> {
    const final = await this.ai.waitForCompletion(orgId, queued, (r) => this.request.set(r), () => this.destroyed);
    this.request.set(final);
    if (final.status !== 'COMPLETED') return;
    const posts = await Promise.all(final.outputs.map((o) => firstValueFrom(this.postsApi.get(orgId, o.postId))));
    this.results.set(posts.map((post) => ({ post, busy: null, imageFailed: false, error: null })));
    if (withImage) await Promise.all(posts.map((post) => this.generateImage(orgId, post)));
  }

  private async generateImage(orgId: string, post: Post): Promise<void> {
    this.patch(post.id, { busy: 'image' });
    try {
      const instruction = this.customImage() ? this.imagePrompt.trim() || null : null;
      const queued = await firstValueFrom(this.ai.requestImage(orgId, post.id, instruction, post.rowVersion));
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed, 3000);
      this.patch(post.id, { imageFailed: final.status === 'FAILED' });
      await this.reloadPost(orgId, post.id);
    } catch {
      this.patch(post.id, { imageFailed: true });
    } finally {
      this.patch(post.id, { busy: null });
    }
  }

  private async reloadPost(orgId: string, postId: string): Promise<void> {
    const post = await firstValueFrom(this.postsApi.get(orgId, postId));
    this.patch(postId, { post });
  }

  private patch(postId: string, change: Partial<Result>): void {
    this.results.update((all) => all.map((r) => (r.post.id === postId ? { ...r, ...change } : r)));
  }
}
