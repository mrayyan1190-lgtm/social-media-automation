import { Location, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { ToastService } from '../../core/services/toast.service';
import { TopbarActionService } from '../../core/services/topbar-action.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../campaigns/models/campaign.model';
import { AiCapabilities, RewriteAction } from '../content/models/ai.model';
import { MediaAsset, PLATFORM_TEXT_LIMITS, Post, PostContent, PostVersion, postText } from '../content/models/post.model';
import { AiApiService } from '../content/services/ai-api.service';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { SocialAccount } from '../social-accounts/models/social-account.model';
import { SocialAccountApiService } from '../social-accounts/services/social-account-api.service';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

/**
 * "Improve with AI" menu. Each item is an AI rewrite of the current version, saved as a new version (the old one is
 * kept). Grammar and hashtags have no dedicated server action, so they are fixed Custom instructions.
 */
const IMPROVE_ACTIONS: { label: string; icon: string; action: RewriteAction; instruction: string | null }[] = [
  { label: 'Rephrase', icon: 'M21 12a9 9 0 1 1-2.6-6.4M21 3v6h-6', action: 'Rewrite', instruction: null },
  { label: 'Shorten', icon: 'M5 12h14', action: 'Shorten', instruction: null },
  { label: 'Lengthen', icon: 'M12 5v14M5 12h14', action: 'Expand', instruction: null },
  { label: 'Stronger hook', icon: 'M13 2 4.5 13.5H11L10 22l8.5-11.5H13L13 2z', action: 'ImproveHook', instruction: null },
  { label: 'More professional', icon: 'M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3z', action: 'MoreProfessional', instruction: null },
  { label: 'Less promotional', icon: 'M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M8 8l8 8', action: 'LessPromotional', instruction: null },
  { label: 'Fix grammar', icon: 'M5 13l4 4L19 7', action: 'Custom', instruction: 'Fix grammar, spelling and punctuation only. Keep the wording, tone and meaning.' },
  { label: 'Add hashtags', icon: 'M9 4 7 20M17 4l-2 16M4 9h17M3 15h17', action: 'Custom', instruction: 'Keep the text as it is and add 3 to 5 relevant hashtags.' },
];

/**
 * Composer: where a real post is prepared. A new post (/composer, optionally ?campaignId=) is created on Save Draft or
 * by Generate with AI; an existing one (/composer/{id}) is edited by saving a new immutable version. Review, approval
 * and publishing all apply to the exact current version; the server enforces every rule, the page only guides.
 */
@Component({
  selector: 'app-composer-page',
  imports: [FormsModule, RouterLink, NgTemplateOutlet, ProblemBannerComponent],
  templateUrl: './composer.page.html',
  styleUrl: './composer.page.css',
  // Close an open Improve / Image menu when clicking anywhere else; a pasted image is uploaded like a dropped one.
  host: { '(document:click)': 'closeMenus($event)', '(document:paste)': 'onPaste($event)' },
})
export class ComposerPage {
  private readonly router = inject(Router);
  private readonly postsApi = inject(PostApiService);
  private readonly ai = inject(AiApiService);
  private readonly media = inject(MediaApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly location = inject(Location);
  protected readonly workspace = inject(WorkspaceService);
  private readonly toast = inject(ToastService);
  private readonly topbar = inject(TopbarActionService);
  private destroyed = false;

  /** Route parameter (existing post) and query parameter (campaign to file a new post in). */
  readonly postId = input<string>();
  readonly campaignId = input<string>();

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly improveActions = IMPROVE_ACTIONS;
  /** The open toolbar menu (Publish to / Improve / Media). */
  protected readonly menu = signal<'publish' | 'improve' | 'image' | null>(null);
  /** Preview "See more" toggle and dropzone drag highlight. */
  protected readonly expanded = signal(false);
  protected readonly dragging = signal(false);

  // Loaded data
  protected readonly post = signal<Post | null>(null);
  protected readonly accounts = signal<SocialAccount[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);

  // Editor state. Link and alt text have no field here but are kept unchanged on every save.
  protected readonly platform = signal<SocialPlatform | null>(null);
  protected readonly accountId = signal<string>('');
  protected readonly text = signal('');
  protected readonly link = signal('');
  protected readonly image = signal<MediaAsset | null>(null);
  protected readonly altText = signal('');
  protected readonly campaign = signal<string>('');
  protected readonly aiInstruction = signal('');
  /** What the platform offers (/internal/ai); only working modes and model choices are shown. */
  protected readonly caps = signal<AiCapabilities>({ textGeneration: false, imageGeneration: false, imageAnalysis: false, imageModels: [], visionModels: [] });
  protected readonly imageAvailable = computed(() => this.caps().imageGeneration);
  protected readonly imageModel = signal('');

  // Activity
  protected readonly loading = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);

  private readonly original = signal<{ text: string; link: string; imageId: string | null; altText: string; accountId: string } | null>(null);

  protected readonly isNew = computed(() => !this.post());
  protected readonly version = computed(() => this.post()?.currentVersion ?? null);
  protected readonly state = computed(() => this.post()?.editorialState ?? 'DRAFT');
  protected readonly canEdit = computed(() => this.workspace.canManageCampaigns() && this.state() !== 'IN_REVIEW' && this.state() !== 'ARCHIVED');
  protected readonly dirty = computed(() => {
    const o = this.original();
    if (!o) return !!this.text().trim() || !!this.image();
    return o.text !== this.text() || o.link !== this.link().trim() || o.imageId !== (this.image()?.id ?? null)
      || o.altText !== this.altText().trim() || o.accountId !== this.accountId();
  });
  protected readonly limit = computed(() => (this.platform() ? PLATFORM_TEXT_LIMITS[this.platform()!] : null));
  protected readonly length = computed(() => this.text().length + (this.link().trim() ? this.link().trim().length + 2 : 0));
  protected readonly platformAccounts = computed(() => this.accounts().filter((a) => a.platform === this.platform()));
  protected readonly latestReview = computed(() => this.reviews().at(0) ?? null);
  /** A rewrite (any Improve with AI item) is running. */
  protected readonly isRewriting = computed(() => IMPROVE_ACTIONS.some((a) => a.action === this.busy()));
  protected readonly changesRequested = computed(() => this.reviews().find((r) => r.status === 'CHANGES_REQUESTED') ?? null);

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.topbar.action.set(null);
    });
    // Submit for Review lives in the navbar while this page is open.
    effect(() => this.topbar.action.set(this.canEdit() && this.state() !== 'APPROVED' ? {
      label: this.busy() === 'submit' ? 'Submitting…' : 'Submit for Review',
      disabled: !!this.busy() || !this.accountId(),
      title: this.accountId() ? '' : 'Choose an account first',
      run: () => void this.submitForReview(),
    } : null));
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.loadWorkspaceData(orgId);
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      const postId = this.postId();
      if (orgId && postId) void this.load(orgId, postId);
    });
    effect(() => {
      if (!this.postId() && this.campaignId()) this.campaign.set(this.campaignId()!);
    });
  }

  // ---- Platform and account -------------------------------------------------------------------------

  protected connected(platform: SocialPlatform): boolean {
    return this.accounts().some((a) => a.platform === platform && a.isEligibleDestination);
  }

  /** The platform is fixed once the post exists; a new post picks one and its first connected account. */
  protected choosePlatform(platform: SocialPlatform): void {
    if (!this.isNew() || !this.canEdit()) return;
    this.platform.set(platform);
    this.accountId.set(this.accounts().find((a) => a.platform === platform && a.isEligibleDestination)?.id ?? '');
  }

  // ---- Media ----------------------------------------------------------------------------------------

  protected previewUrl(mediaAssetId: string): string {
    return this.media.previewUrl(this.workspace.orgId()!, mediaAssetId);
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) void this.uploadFile(file);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files[0];
    if (file && this.canEdit() && !this.busy()) void this.uploadFile(file);
  }

  /** Only pasted image files are taken; pasted text keeps going into the focused field as usual. */
  protected onPaste(event: ClipboardEvent): void {
    const file = [...(event.clipboardData?.files ?? [])].find((f) => f.type.startsWith('image/'));
    if (!file || !this.canEdit() || this.busy()) return;
    event.preventDefault();
    void this.uploadFile(file);
  }

  private async uploadFile(file: File): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    // Early hints only; the server inspects the bytes and applies the authoritative rules.
    if (!['image/jpeg', 'image/png'].includes(file.type)) return this.fail('Choose a JPEG or PNG image.');
    if (file.size > MAX_IMAGE_BYTES) return this.fail('Images must be at most 8 MB.');
    await this.run('upload', async () => {
      this.setImage(await firstValueFrom(this.media.upload(orgId, file)));
      this.toast.success('Image attached. Save Draft to keep it.');
    });
  }

  protected removeImage(): void {
    this.setImage(null);
  }

  // ---- Improve with AI / Image menus -----------------------------------------------------------------

  protected toggleMenu(which: 'publish' | 'improve' | 'image'): void {
    this.menu.set(this.menu() === which ? null : which);
  }

  protected closeMenus(event: Event): void {
    if (this.menu() && !(event.target instanceof Element && event.target.closest('.dd-wrap'))) this.menu.set(null);
  }

  protected async improve(action: RewriteAction, instruction: string | null): Promise<void> {
    this.menu.set(null);
    await this.aiEdit(action, instruction);
  }

  /** Generates a (replacement) image that illustrates the post. */
  protected async newImage(instruction: string | null): Promise<void> {
    this.menu.set(null);
    await this.run('image', () => this.addAiImage(instruction));
  }

  // ---- Write with AI --------------------------------------------------------------------------------

  /**
   * With post text: an AI edit following the description (saved as a new version). Without text: the AI writes a new
   * draft for the chosen platform, which then opens here. On a locked post (in review) it always writes a new draft, so
   * the submitted version is never touched. Either way nothing is published or approved.
   */
  protected async generateWithAi(): Promise<void> {
    const instruction = this.aiInstruction().trim();
    if (!instruction || this.busy()) return;
    // Existing text is revised in place; images are added from the Image menu.
    if (this.canEdit() && this.text().trim()) {
      await this.aiEdit('Custom', instruction);
      if (!this.problem()) this.aiInstruction.set('');
      return;
    }
    const orgId = this.workspace.orgId();
    const platform = this.platform();
    await this.run('generate', async () => {
      if (!orgId || !platform) throw new Error('Choose where to publish first.');
      const queued = await firstValueFrom(this.ai.requestGeneration(orgId, {
        prompt: instruction, platforms: [platform], campaignId: this.campaign() || null,
        tone: null, length: 'MEDIUM', hashtags: 'AUTO', emojis: 'AUTO',
      }));
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed);
      const created = final.outputs.find((o) => o.platform === platform);
      if (final.status !== 'COMPLETED' || !created) throw new Error(this.aiFailure(final.failureCode));
      // Keep what was chosen on screen: the attached image and account become part of the next saved version. A locked
      // post's image belongs to that post, so only the account carries over from it.
      const [image, accountId] = [this.canEdit() ? this.image() : null, this.accountId()];
      await this.load(orgId, created.postId);
      this.location.replaceState(`/${this.workspace.slug()}/composer/${created.postId}`);
      if (image) this.setImage(image);
      if (accountId) this.accountId.set(accountId);
      this.aiInstruction.set('');
      this.toast.success('AI wrote a draft. Edit it if you like, then Save Draft or Submit for Review.');
    });
  }

  private async aiEdit(action: RewriteAction, instruction: string | null = null): Promise<void> {
    const orgId = this.workspace.orgId();
    await this.run(action, async () => {
      const post = await this.ensureSaved();
      if (!orgId || !post) return;
      const queued = await firstValueFrom(this.ai.requestRewrite(orgId, post.id, action, instruction, post.rowVersion));
      const imageBefore = post.currentVersion.mediaAssetId;
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed);
      await this.load(orgId, post.id);
      if (final.status === 'FAILED') {
        // An image-only request that failed explains why (e.g. the image model refused); other failures stay generic.
        throw new Error(final.failureCode === 'AI_PROVIDER_REJECTED' && final.failureMessage ? final.failureMessage : this.aiFailure(final.failureCode));
      }
      const newImage = this.version()?.mediaAssetId !== imageBefore;
      this.toast.success(`AI edit saved as version ${this.version()?.versionNumber}${newImage ? ' with a new image' : ''}. The previous version is kept.`
        + (final.warning ? ` ${final.warning}` : ''));
    });
  }

  /** Generates an image (with the chosen Hugging Face model, when a choice is offered) as a new version of the open post. */
  private async addAiImage(instruction: string | null): Promise<void> {
    const orgId = this.workspace.orgId();
    const post = await this.ensureSaved();
    if (!orgId || !post) return;
    const model = this.caps().imageModels.length ? this.imageModel() || null : null;
    const queued = await firstValueFrom(this.ai.requestImage(orgId, post.id, instruction, post.rowVersion, model));
    const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed, 3000);
    await this.load(orgId, post.id);
    if (final.status === 'FAILED') {
      throw new Error(final.failureCode === 'AI_NOT_CONFIGURED' || final.failureCode === 'AI_PROVIDER_AUTH'
        ? 'Image generation is temporarily unavailable. Please try again later.'
        : final.failureMessage || 'The image could not be generated. Please try again.');
    }
    this.toast.success(`AI image added as version ${this.version()?.versionNumber}. Add alt text if you like; the previous version is kept.`);
  }

  private aiFailure(code: string | null): string {
    return code === 'AI_NOT_CONFIGURED' || code === 'AI_PROVIDER_AUTH'
      ? 'AI writing is temporarily unavailable. Please try again later.' : 'The AI could not finish. Please try again.';
  }

  // ---- Save and review ------------------------------------------------------------------------------

  protected async saveDraft(): Promise<void> {
    await this.run('save', async () => {
      const before = this.post()?.currentVersionId;
      const post = await this.ensureSaved(true);
      if (post && !this.isNew()) {
        this.toast.success(post.currentVersionId !== before ? `Saved as version ${post.currentVersion.versionNumber}.` : 'No changes to save.');
      }
    });
  }

  protected async submitForReview(): Promise<void> {
    const orgId = this.workspace.orgId();
    await this.run('submit', async () => {
      const post = await this.ensureSaved();
      if (!orgId || !post) return;
      // No reviewer is picked: any Admin decides. An Admin goes straight to the review; a Creator gets a blank composer
      // for the next post, since the submitted one is locked until the Admin decides.
      const review = await firstValueFrom(this.reviewsApi.submit(orgId, post.id, null, null, post.rowVersion));
      if (this.workspace.isAdmin()) {
        await this.router.navigate(['/approvals', review.id]);
        return;
      }
      this.toast.success(`Version ${review.versionNumber} submitted for review. You can find it in Planner.`);
      await this.router.navigateByUrl(`/${this.workspace.slug()}/composer`);
    });
  }

  protected reload(): void {
    const orgId = this.workspace.orgId();
    const postId = this.post()?.id ?? this.postId();
    this.problem.set(null);
    if (orgId && postId) void this.load(orgId, postId);
  }

  // ---- Internals ------------------------------------------------------------------------------------

  /**
   * Makes sure the server has exactly what is on screen: creates the post (new) or saves a new version (changed).
   * Returns the saved post.
   */
  private async ensureSaved(explicit = false): Promise<Post | null> {
    const orgId = this.workspace.orgId();
    if (!orgId) return null;
    const current = this.post();
    if (current && !this.dirty()) return current;
    if (!this.platform()) throw new Error('Choose where to publish first.');
    if (!this.text().trim()) throw new Error('Write the post content first.');
    if (current && current.editorialState === 'APPROVED' && explicit
      && !confirm('This version is approved. Saving creates a new version that needs review again. Continue?')) return null;

    if (!current) {
      const created = await firstValueFrom(this.postsApi.create(orgId, this.platform()!, this.campaign() || null, this.content(null)));
      this.apply(created);
      // Same page instance keeps running (e.g. an AI edit that needed the draft saved first); only the URL changes.
      this.location.replaceState(`/${this.workspace.slug()}/composer/${created.id}`);
      this.toast.success('Draft saved.');
      return created;
    }
    const saved = await firstValueFrom(this.postsApi.saveNewVersion(orgId, current.id, this.content(current.currentVersion), current.rowVersion));
    await this.load(orgId, saved.id);
    return this.post();
  }

  /**
   * The editor shows one text. Unchanged text keeps the stored hook/body/CTA/hashtags as they are (so nothing changes
   * on the server); edited text is saved as the body.
   */
  private content(current: PostVersion | null): PostContent {
    const unchangedText = current && this.original()?.text === this.text();
    const image = this.image();
    return {
      hook: unchangedText ? current.hook : null,
      body: unchangedText ? current.body : this.text().trim(),
      callToAction: unchangedText ? current.callToAction : null,
      hashtags: unchangedText ? current.hashtags : [],
      destinationUrl: this.link().trim() || null,
      mediaAssetId: image?.id ?? null,
      altText: image ? this.altText().trim() || null : null,
      socialAccountId: this.accountId() || null,
    };
  }

  private setImage(asset: MediaAsset | null): void {
    // Alt text describes one specific image, so a replacement starts without the old description.
    if (asset?.id !== this.image()?.id) this.altText.set('');
    this.image.set(asset);
  }

  private async loadWorkspaceData(orgId: string): Promise<void> {
    void firstValueFrom(this.ai.capabilities(orgId)).then((c) => {
      this.caps.set(c);
      this.imageModel.set(c.imageModels[0] ?? '');
    }, () => undefined);
    const accounts = await firstValueFrom(this.accountsApi.list(orgId)).catch(() => []);
    this.accounts.set(accounts);
    if (this.isNew() && !this.platform()) {
      const first = SOCIAL_PLATFORMS.find((p) => this.connected(p));
      if (first) this.choosePlatform(first);
    }
    this.preselectAccount();
  }

  private async load(orgId: string, postId: string): Promise<void> {
    this.loading.set(true);
    try {
      const [post, reviews] = await Promise.all([
        firstValueFrom(this.postsApi.get(orgId, postId)),
        firstValueFrom(this.reviewsApi.list(orgId, { postId })).catch(() => []),
      ]);
      this.reviews.set(reviews);
      this.apply(post);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  private apply(post: Post): void {
    const v = post.currentVersion;
    this.post.set(post);
    this.platform.set(post.platform);
    this.accountId.set(v.socialAccountId ?? '');
    this.text.set(postText(v));
    this.link.set(v.destinationUrl ?? '');
    this.image.set(v.media);
    this.altText.set(v.altText ?? '');
    this.campaign.set(post.campaignId ?? '');
    this.original.set({ text: postText(v), link: v.destinationUrl ?? '', imageId: v.mediaAssetId, altText: v.altText ?? '', accountId: v.socialAccountId ?? '' });
    this.preselectAccount();
  }

  /** AI drafts start without an account: suggest the first connected one (saved with the next version). */
  private preselectAccount(): void {
    if (this.canEdit() && !this.accountId() && this.platform())
      this.accountId.set(this.accounts().find((a) => a.platform === this.platform() && a.isEligibleDestination)?.id ?? '');
  }

  private async run(what: string, action: () => Promise<void>): Promise<void> {
    this.busy.set(what);
    this.problem.set(null);
    try {
      await action();
    } catch (error) {
      this.problem.set(error instanceof Error ? { status: 0, code: 'UNEXPECTED_ERROR', detail: error.message } : toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }

  private fail(message: string): void {
    this.problem.set({ status: 0, code: 'VALIDATION_FAILED', detail: message });
  }
}
