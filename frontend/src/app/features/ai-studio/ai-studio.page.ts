import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { ToastService } from '../../core/services/toast.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { Campaign, ContentLength, EmojiMode, HashtagMode, SOCIAL_PLATFORMS, SocialPlatform } from '../campaigns/models/campaign.model';
import { CampaignApiService } from '../campaigns/services/campaign-api.service';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { AiGenerationRequest } from '../content/models/ai.model';
import { MediaAsset, Post, PostContent, PostVersion, postText } from '../content/models/post.model';
import { AiApiService } from '../content/services/ai-api.service';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { BRAND_TONES, BrandProfile, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';
import { SocialAccountApiService } from '../social-accounts/services/social-account-api.service';

const EXAMPLE = 'Create launch posts for our new AI reporting dashboard. Focus on helping small businesses save reporting time and encourage people to book a demo.';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

/** Quick additions to the extra instruction; clicking again removes them. */
const PRESETS = [
  { label: 'Product launch', icon: 'spark' },
  { label: 'Professional LinkedIn tone', icon: 'user' },
  { label: 'Customer pain point', icon: 'bulb' },
  { label: 'Engaging hook', icon: 'chat' },
  { label: 'Short promotional post', icon: 'doc' },
  { label: 'Thought leadership', icon: 'chart' },
] as const;

interface Result {
  post: Post;
  /** What is running for this result: 'image', 'attach' or 'submit'. */
  busy: string | null;
  imageFailed: boolean;
  error: string | null;
  submitted: boolean;
}

/** The editable content of a version with some fields changed (everything else is kept as it is). */
function contentOf(v: PostVersion, change: Partial<PostContent>): PostContent {
  const { hook, body, callToAction, hashtags, destinationUrl, mediaAssetId, altText, socialAccountId } = v;
  return { hook, body, callToAction, hashtags, destinationUrl, mediaAssetId, altText, socialAccountId, ...change };
}

/**
 * AI Studio: a prompt becomes one draft post per platform, written with the saved brand profile. Each draft can be sent
 * to review here or opened in Composer. Nothing is approved, scheduled or published from this page.
 */
@Component({
  selector: 'app-ai-studio-page',
  imports: [FormsModule, RouterLink, PlatformIconComponent, ProblemBannerComponent],
  templateUrl: './ai-studio.page.html',
  styleUrl: './ai-studio.page.css',
})
export class AiStudioPage {
  private readonly ai = inject(AiApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly media = inject(MediaApiService);
  private readonly campaignsApi = inject(CampaignApiService);
  private readonly brandApi = inject(BrandProfileApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  protected readonly workspace = inject(WorkspaceService);
  private destroyed = false;

  /** Query parameters: the campaign the drafts are filed in, and a generation to show again (after Back from Composer). */
  readonly campaignId = input<string>();
  readonly requestId = input<string>(undefined, { alias: 'request' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly tones = BRAND_TONES;
  protected readonly example = EXAMPLE;
  protected readonly presets = PRESETS;
  protected readonly brand = signal<BrandProfile | null>(null);
  protected readonly brandLoaded = signal(false);
  protected readonly campaign = signal<Campaign | null>(null);
  protected readonly imageAvailable = signal(false);
  protected readonly selected = signal<SocialPlatform[]>(['LinkedIn']);
  protected readonly withImage = signal(false);
  protected readonly image = signal<MediaAsset | null>(null);
  protected readonly uploading = signal(false);
  protected readonly promptSaved = signal(false);
  protected readonly brandOpen = signal(false);
  protected readonly advOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly request = signal<AiGenerationRequest | null>(null);
  protected readonly results = signal<Result[]>([]);
  protected readonly generating = computed(() => this.request()?.status === 'PENDING' || this.request()?.status === 'PROCESSING');
  protected readonly activeChips = signal<string[]>([]);
  protected prompt = '';
  protected extra = '';
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
      this.prompt = this.readSavedPrompt(orgId);
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
      // Only the query parameter triggers a restore; a new generation must not reload the old request.
      if (orgId && requestId && !untracked(this.request)) void this.restore(orgId, requestId);
    });
  }

  protected canGenerate(): boolean {
    return !this.busy() && !this.generating() && !this.uploading() && !!this.fullPrompt() && !!this.selected().length && !!this.brand();
  }

  protected settingsHint(): string {
    return [this.tone || 'Use Brand Tone', this.length.charAt(0) + this.length.slice(1).toLowerCase(),
      this.hashtags === 'AUTO' ? 'Auto hashtags' : 'No hashtags', this.emojis === 'AUTO' ? 'Auto emojis' : 'No emojis'].join(' · ');
  }

  /** At least one platform stays selected. */
  protected toggle(platform: SocialPlatform): void {
    const all = this.selected();
    if (all.includes(platform) && all.length === 1) return this.toast.error('Pick at least one platform');
    this.selected.set(all.includes(platform) ? all.filter((p) => p !== platform) : [...all, platform]);
  }

  protected toggleChip(label: string): void {
    const parts = this.extra.split(' · ').map((p) => p.trim()).filter(Boolean);
    const next = parts.includes(label) ? parts.filter((p) => p !== label) : [...parts, label];
    this.extra = next.join(' · ');
    this.activeChips.set(next.filter((p) => this.presets.some((c) => c.label === p)));
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

  /** Keeps the prompt in this browser only (it is not a post until generated). */
  protected savePrompt(): void {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    if (!this.prompt.trim()) return this.toast.error('Nothing to save yet');
    try {
      localStorage.setItem(`ai-studio-prompt:${orgId}`, this.prompt);
    } catch {
      return this.toast.error('Could not save the prompt in this browser');
    }
    this.promptSaved.set(true);
    this.toast.success('Prompt saved');
    setTimeout(() => this.promptSaved.set(false), 2000);
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const orgId = this.workspace.orgId();
    if (!file || !orgId) return;
    // Early hints only; the server inspects the bytes and applies the authoritative rules.
    if (!['image/jpeg', 'image/png'].includes(file.type)) return this.toast.error('Choose a JPEG or PNG image.');
    if (file.size > MAX_IMAGE_BYTES) return this.toast.error('Images must be at most 8 MB.');
    this.uploading.set(true);
    try {
      this.image.set(await firstValueFrom(this.media.upload(orgId, file)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.uploading.set(false);
    }
  }

  protected async generate(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !this.canGenerate()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      const queued = await firstValueFrom(this.ai.requestGeneration(orgId, {
        prompt: this.fullPrompt(),
        platforms: SOCIAL_PLATFORMS.filter((p) => this.selected().includes(p)),
        campaignId: this.campaign()?.id ?? null,
        tone: this.tone || null,
        length: this.length,
        hashtags: this.hashtags,
        emojis: this.emojis,
      }));
      this.results.set([]);
      this.request.set(queued);
      // Keeps the results reachable with Back after opening a draft in Composer.
      void this.router.navigate([], { queryParams: { request: queued.id }, queryParamsHandling: 'merge', replaceUrl: true });
      await this.follow(orgId, queued, this.withImage(), this.image());
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(post: Post): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.text(post));
      this.toast.success('Copied to clipboard');
    } catch {
      this.toast.error('Copy not available');
    }
  }

  /**
   * Sends the exact current version to review. An AI draft has no account yet, so the first connected account for its
   * platform is saved first as a new version (Composer does the same); the server checks everything again.
   */
  protected async submit(result: Result): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.patch(result.post.id, { busy: 'submit', error: null });
    try {
      let post = result.post;
      if (!post.currentVersion.socialAccountId) {
        const accounts = await firstValueFrom(this.accountsApi.list(orgId));
        const account = accounts.find((a) => a.platform === post.platform && a.isEligibleDestination);
        if (!account) throw new Error(this.workspace.isAdmin()
          ? `Connect a ${post.platform} account first (Social Accounts).` : `Ask your Admin to connect a ${post.platform} account first.`);
        post = await firstValueFrom(this.postsApi.saveNewVersion(orgId, post.id, contentOf(post.currentVersion, { socialAccountId: account.id }), post.rowVersion));
      }
      const review = await firstValueFrom(this.reviewsApi.submit(orgId, post.id, null, null, post.rowVersion));
      this.patch(post.id, { post: await firstValueFrom(this.postsApi.get(orgId, post.id)), submitted: true });
      this.toast.success(`Version ${review.versionNumber} submitted for review.`);
    } catch (error) {
      this.patch(result.post.id, { error: error instanceof Error ? error.message : toApiProblem(error).detail });
    } finally {
      this.patch(result.post.id, { busy: null });
    }
  }

  private fullPrompt(): string {
    return [this.prompt.trim(), this.extra.trim()].filter(Boolean).join('\n\n');
  }

  private readSavedPrompt(orgId: string): string {
    try {
      return localStorage.getItem(`ai-studio-prompt:${orgId}`) ?? '';
    } catch {
      return '';
    }
  }

  private async restore(orgId: string, requestId: string): Promise<void> {
    try {
      const request = await firstValueFrom(this.ai.get(orgId, requestId));
      if (request.kind !== 'Generation') return;
      this.request.set(request);
      await this.follow(orgId, request, false, null);
    } catch {
      // An old or foreign link: just show an empty studio.
      this.request.set(null);
    }
  }

  /**
   * Waits for the drafts and shows them. An uploaded image is attached to each draft as a new version; otherwise
   * (Text + Image) one image is generated per draft.
   */
  private async follow(orgId: string, queued: AiGenerationRequest, withImage: boolean, image: MediaAsset | null): Promise<void> {
    const final = await this.ai.waitForCompletion(orgId, queued, (r) => this.request.set(r), () => this.destroyed);
    this.request.set(final);
    if (final.status !== 'COMPLETED') return;
    const posts = await Promise.all(final.outputs.map((o) => firstValueFrom(this.postsApi.get(orgId, o.postId))));
    this.results.set(posts.map((post) => ({ post, busy: null, imageFailed: false, error: null, submitted: false })));
    if (image) await Promise.all(posts.map((post) => this.attachImage(orgId, post, image)));
    else if (withImage) await Promise.all(posts.map((post) => this.generateImage(orgId, post)));
  }

  private async attachImage(orgId: string, post: Post, image: MediaAsset): Promise<void> {
    this.patch(post.id, { busy: 'attach' });
    try {
      await firstValueFrom(this.postsApi.saveNewVersion(orgId, post.id, contentOf(post.currentVersion, { mediaAssetId: image.id, altText: null }), post.rowVersion));
      await this.reloadPost(orgId, post.id);
    } catch (error) {
      this.patch(post.id, { error: toApiProblem(error).detail });
    } finally {
      this.patch(post.id, { busy: null });
    }
  }

  private async generateImage(orgId: string, post: Post): Promise<void> {
    this.patch(post.id, { busy: 'image' });
    try {
      const queued = await firstValueFrom(this.ai.requestImage(orgId, post.id, this.imagePrompt.trim() || null, post.rowVersion));
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
