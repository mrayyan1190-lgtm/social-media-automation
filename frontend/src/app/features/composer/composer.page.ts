import { DatePipe, Location, NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { ToastService } from '../../core/services/toast.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { BLOCKED_HINT, ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { SOCIAL_PLATFORMS, SocialPlatform } from '../../shared/models/platform.model';
import { AiCapabilities, RewriteAction } from '../content/models/ai.model';
import { MAX_POST_IMAGES, MediaAsset, PLATFORM_TEXT_LIMITS, Post, PostContent, PostVersion, postText } from '../content/models/post.model';
import { AiApiService } from '../content/services/ai-api.service';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { SocialAccount } from '../social-accounts/models/social-account.model';
import { SocialAccountApiService } from '../social-accounts/services/social-account-api.service';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

/** One attached image on screen; alt text has no field here but is kept unchanged on every save. */
interface AttachedImage {
  asset: MediaAsset;
  altText: string;
}

/** Identity of the attached images (order and alt text included), to tell whether they changed. */
const imagesKey = (images: { mediaAssetId: string; altText: string | null }[]): string =>
  images.map((i) => `${i.mediaAssetId}|${(i.altText ?? '').trim()}`).join(',');

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
  { label: 'Remove unsupported claims', icon: 'M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14', action: 'Custom',
    instruction: 'Delete every statistic, customer result, testimonial, quote, guarantee, price, discount, award, ranking or partnership claim that the supplied facts do not state. Do not replace them with other claims or figures; keep the rest of the post, its tone and its CTA.' },
  { label: 'Add hashtags', icon: 'M9 4 7 20M17 4l-2 16M4 9h17M3 15h17', action: 'Custom', instruction: 'Keep the text as it is and add 3 to 5 relevant hashtags.' },
];

/**
 * Composer: where a real post is prepared. A new post (/composer) is created on Save Draft or
 * by Generate with AI; an existing one (/composer/{id}) is edited by saving a new immutable version. Review, approval
 * and publishing all apply to the exact current version; the server enforces every rule, the page only guides.
 */
@Component({
  selector: 'app-composer-page',
  imports: [FormsModule, RouterLink, NgTemplateOutlet, DatePipe, ProblemBannerComponent],
  templateUrl: './composer.page.html',
  styleUrl: './composer.page.css',
  // Close an open Improve / Image menu when clicking anywhere else; a pasted image is uploaded like a dropped one.
  host: { '(document:click)': 'closeMenus($event)', '(document:paste)': 'onPaste($event)', '(document:keydown)': 'onViewerKey($event)' },
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
  private destroyed = false;

  /** Route parameter (existing post). */
  readonly postId = input<string>();

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly improveActions = IMPROVE_ACTIONS;
  /** The open toolbar menu (Publish to / Improve / Media). */
  protected readonly menu = signal<'publish' | 'improve' | 'changes' | null>(null);
  /** Preview "See more" toggle and dropzone drag highlight. */
  protected readonly expanded = signal(false);
  protected readonly dragging = signal(false);
  /** The Live preview tab; null follows the post's platform. */
  protected readonly previewTab = signal<SocialPlatform | null>(null);
  protected readonly shownPreview = computed(() => this.previewTab() ?? this.platform() ?? 'LinkedIn');
  /** The "Approve this draft?" / "Send for review?" confirmation. */
  protected readonly confirming = signal(false);

  // Loaded data
  protected readonly post = signal<Post | null>(null);
  protected readonly accounts = signal<SocialAccount[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);

  // Editor state. Link and alt text have no field here but are kept unchanged on every save.
  protected readonly platform = signal<SocialPlatform | null>(null);
  /**
   * Where Submit / Approve sends the post (one or several platforms). The post's own platform is fixed; every other
   * chosen platform gets its own copy of what is on screen, with its own review and approval.
   */
  protected readonly targets = signal<SocialPlatform[]>([]);
  protected readonly accountId = signal<string>('');
  protected readonly text = signal('');
  protected readonly link = signal('');
  /** Attached images in publishing order (the first is the cover). */
  protected readonly images = signal<AttachedImage[]>([]);
  /** Index of the image open in the full-screen viewer, or null. */
  protected readonly viewing = signal<number | null>(null);
  protected readonly maxImages = MAX_POST_IMAGES;
  protected readonly aiInstruction = signal('');
  /** What the platform offers (/internal/ai); only working modes and model choices are shown. */
  protected readonly caps = signal<AiCapabilities>({ textGeneration: false, imageGeneration: false, imageAnalysis: false, imageModels: [], visionModels: [] });

  // Activity
  protected readonly loading = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);

  private readonly original = signal<{ text: string; link: string; images: string; accountId: string } | null>(null);

  protected readonly isNew = computed(() => !this.post());
  protected readonly version = computed(() => this.post()?.currentVersion ?? null);
  protected readonly state = computed(() => this.post()?.editorialState ?? 'DRAFT');
  protected readonly canEdit = computed(() => this.workspace.canManageContent() && this.state() !== 'IN_REVIEW' && this.state() !== 'ARCHIVED');
  protected readonly dirty = computed(() => {
    const o = this.original();
    if (!o) return !!this.text().trim() || this.images().length > 0;
    return o.text !== this.text() || o.link !== this.link().trim() || o.images !== imagesKey(this.content(null).images)
      || o.accountId !== this.accountId();
  });
  /** The strictest limit of the chosen platforms, so the same text fits all of them. */
  protected readonly limit = computed(() => (this.targets().length ? Math.min(...this.targets().map((p) => PLATFORM_TEXT_LIMITS[p])) : null));
  /** Chosen platforms without a connected account (Submit / Approve waits for them). */
  protected readonly missingTargets = computed(() => this.targets().filter((p) => (p === this.platform() ? !this.accountId() : !this.connected(p))));
  protected readonly length = computed(() => this.text().length + (this.link().trim() ? this.link().trim().length + 2 : 0));
  protected readonly platformAccounts = computed(() => this.accounts().filter((a) => a.platform === this.platform()));
  protected readonly latestReview = computed(() => this.reviews().at(0) ?? null);
  /** A rewrite (any Improve with AI item) is running. */
  protected readonly isRewriting = computed(() => IMPROVE_ACTIONS.some((a) => a.action === this.busy()));
  protected readonly changesRequested = computed(() => this.reviews().find((r) => r.status === 'CHANGES_REQUESTED') ?? null);
  /** Header chip: "Draft · v02", "In review · v01", … */
  protected readonly stateLabel = computed(() => {
    const v = this.version();
    if (!v) return 'New draft';
    const label = { DRAFT: 'Draft', IN_REVIEW: 'In review', CHANGES_REQUESTED: 'Changes requested', APPROVED: 'Approved', ARCHIVED: 'Archived' }[this.state()] ?? 'Draft';
    return `${label} · v${String(v.versionNumber).padStart(2, '0')}`;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.loadWorkspaceData(orgId);
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      const postId = this.postId();
      if (orgId && postId) void this.load(orgId, postId);
    });
  }

  // ---- Platform and account -------------------------------------------------------------------------

  protected connected(platform: SocialPlatform): boolean {
    return this.accounts().some((a) => a.platform === platform && a.isEligibleDestination);
  }

  /** Connected platforms other than this post's (offered when this post's platform has no account). */
  protected readonly otherConnected = computed(() => SOCIAL_PLATFORMS.filter((p) => p !== this.platform() && this.connected(p)));

  /**
   * A post's platform is fixed, so "Post to X instead" creates a new draft for X with what is on screen and its connected
   * account, then opens it. The original draft is left untouched (it can be deleted from Drafts).
   */
  protected async copyTo(platform: SocialPlatform): Promise<void> {
    const orgId = this.workspace.orgId();
    const account = this.accounts().find((a) => a.platform === platform && a.isEligibleDestination);
    if (!orgId || !account) return;
    await this.run('save', async () => {
      const created = await firstValueFrom(this.postsApi.create(orgId, platform, { ...this.content(null), socialAccountId: account.id }));
      this.toast.success(`Created a ${platform} draft.`);
      await this.router.navigateByUrl(`/${this.workspace.slug()}/composer/${created.id}`);
    });
  }

  /** Full-screen viewer: Escape closes, arrow keys step through the post's images. */
  protected onViewerKey(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.confirming.set(false);
      this.menu.set(null);
    }
    const i = this.viewing();
    if (i === null) return;
    const count = this.images().length;
    if (event.key === 'Escape') this.viewing.set(null);
    else if (event.key === 'ArrowRight') this.viewing.set((i + 1) % count);
    else if (event.key === 'ArrowLeft') this.viewing.set((i - 1 + count) % count);
  }

  /** The platform is fixed once the post exists; a new post picks one and its first connected account. */
  protected choosePlatform(platform: SocialPlatform): void {
    if (!this.isNew() || !this.canEdit()) return;
    this.platform.set(platform);
    this.accountId.set(this.accounts().find((a) => a.platform === platform && a.isEligibleDestination)?.id ?? '');
  }

  /** Publish to: one or several platforms; at least one stays chosen. A new post is saved under the first of them. */
  protected toggleTarget(platform: SocialPlatform): void {
    if (!this.canEdit()) return;
    const all = this.targets();
    if (all.includes(platform) && all.length === 1) return;
    const next = SOCIAL_PLATFORMS.filter((p) => (p === platform ? !all.includes(p) : all.includes(p)));
    this.targets.set(next);
    if (this.isNew()) this.choosePlatform(next[0]);
  }

  // ---- Media ----------------------------------------------------------------------------------------

  protected previewUrl(mediaAssetId: string): string {
    return this.media.previewUrl(this.workspace.orgId()!, mediaAssetId);
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = [...(input.files ?? [])];
    input.value = '';
    if (files.length) void this.uploadFiles(files);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const files = [...(event.dataTransfer?.files ?? [])];
    if (files.length && this.canEdit() && !this.busy()) void this.uploadFiles(files);
  }

  /** Only pasted image files are taken; pasted text keeps going into the focused field as usual. */
  protected onPaste(event: ClipboardEvent): void {
    const files = [...(event.clipboardData?.files ?? [])].filter((f) => f.type.startsWith('image/'));
    if (!files.length || !this.canEdit() || this.busy()) return;
    event.preventDefault();
    void this.uploadFiles(files);
  }

  /** Uploads and attaches the files after the current images, in order (up to MAX_POST_IMAGES in total). */
  private async uploadFiles(files: File[]): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    // Early hints only; the server inspects the bytes and applies the authoritative rules.
    if (this.images().length + files.length > MAX_POST_IMAGES) return this.fail(`A post can have at most ${MAX_POST_IMAGES} images.`);
    if (files.some((f) => !['image/jpeg', 'image/png'].includes(f.type))) return this.fail('Choose JPEG or PNG images.');
    if (files.some((f) => f.size > MAX_IMAGE_BYTES)) return this.fail('Images must be at most 8 MB.');
    await this.run('upload', async () => {
      for (const file of files) {
        const asset = await firstValueFrom(this.media.upload(orgId, file));
        this.images.update((list) => [...list, { asset, altText: '' }]);
      }
      this.toast.success(`${files.length === 1 ? 'Image' : `${files.length} images`} attached. Save Draft to keep ${files.length === 1 ? 'it' : 'them'}.`);
    });
  }

  protected removeImage(id: string): void {
    this.images.update((list) => list.filter((i) => i.asset.id !== id));
  }

  /** Moves an image one place earlier (the first image is the cover). */
  protected moveImageLeft(index: number): void {
    if (index <= 0) return;
    this.images.update((list) => {
      const next = [...list];
      [next[index - 1], next[index]] = [next[index], next[index - 1]];
      return next;
    });
  }

  // ---- Improve with AI / Image menus -----------------------------------------------------------------

  protected toggleMenu(which: 'publish' | 'improve' | 'changes'): void {
    this.menu.set(this.menu() === which ? null : which);
  }

  protected closeMenus(event: Event): void {
    if (this.menu() && !(event.target instanceof Element && event.target.closest('.dd-wrap'))) this.menu.set(null);
  }

  protected async improve(action: RewriteAction, instruction: string | null): Promise<void> {
    this.menu.set(null);
    await this.aiEdit(action, instruction);
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
        prompt: instruction, platforms: [platform],
        tone: null, length: 'MEDIUM', hashtags: 'AUTO', emojis: 'AUTO',
      }));
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed);
      const created = final.outputs.find((o) => o.platform === platform);
      if (final.status !== 'COMPLETED' || !created) throw new Error(this.aiFailure(final.failureCode));
      // Keep what was chosen on screen: the attached image and account become part of the next saved version. A locked
      // post's image belongs to that post, so only the account carries over from it.
      const [images, accountId] = [this.canEdit() ? this.images() : [], this.accountId()];
      await this.load(orgId, created.postId);
      this.location.replaceState(`/${this.workspace.slug()}/composer/${created.postId}`);
      if (images.length) this.images.set(images);
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
      const imagesBefore = imagesKey(post.currentVersion.images);
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed);
      await this.load(orgId, post.id);
      if (final.status === 'FAILED') {
        // An image-only request that failed explains why (e.g. the image model refused); other failures stay generic.
        throw new Error(final.failureCode === 'AI_PROVIDER_REJECTED' && final.failureMessage ? final.failureMessage : this.aiFailure(final.failureCode));
      }
      const newImage = imagesKey(this.version()?.images ?? []) !== imagesBefore;
      this.toast.success(`AI edit saved${newImage ? ' with new images' : ''}.`
        + (final.warning ? ` ${final.warning}` : ''));
    });
  }

  private aiFailure(code: string | null): string {
    return code === 'AI_NOT_CONFIGURED' || code === 'AI_PROVIDER_AUTH'
      ? 'AI writing is temporarily unavailable. Please try again later.' : 'The AI could not finish. Please try again.';
  }

  // ---- Save and review ------------------------------------------------------------------------------

  protected async saveDraft(): Promise<void> {
    await this.run('save', async () => {
      const before = this.post()?.currentVersionId;
      const post = await this.ensureSaved();
      // ensureSaved is silent, so the AI edit / Submit flows that also call it show only their own message.
      if (post) this.toast.success(!before ? 'Draft saved.' : post.currentVersionId !== before ? 'Saved.' : 'No changes to save.');
    });
  }

  /** Approve (Admin) / Submit (Creator) asks first, as in the design; the top-bar button submits directly. */
  protected confirmSubmit(): void {
    this.confirming.set(true);
  }

  protected async submitForReview(): Promise<void> {
    this.confirming.set(false);
    const orgId = this.workspace.orgId();
    const targets = this.targets();
    await this.run('submit', async () => {
      if (this.missingTargets().length) throw new Error(`No ${this.missingTargets().join(' / ')} account connected.`);
      const post = await this.ensureSaved();
      if (!orgId || !post) return;
      // Every other chosen platform gets its own post with the same content and that platform's first connected account.
      const posts = targets.includes(post.platform) ? [post] : [];
      for (const platform of targets.filter((p) => p !== post.platform)) {
        const account = this.accounts().find((a) => a.platform === platform && a.isEligibleDestination)!;
        posts.push(await firstValueFrom(this.postsApi.create(orgId, platform, { ...this.content(post.currentVersion), socialAccountId: account.id }, post.id)));
      }
      // No reviewer is picked: any Admin decides. A Creator gets a blank composer for the next post, since the submitted
      // one is locked until the Admin decides.
      for (const target of posts) {
        let review: ReviewSummary;
        try {
          review = await firstValueFrom(this.reviewsApi.submit(orgId, target.id, null, null, target.rowVersion));
        } catch (error) {
          const problem = toApiProblem(error);
          if (problem.code !== 'SUBMISSION_BLOCKED') throw error;
          this.problem.set({ ...problem, detail: `${target.platform}: ${problem.detail} ${BLOCKED_HINT}` });
          return;
        }
        if (this.workspace.isAdmin()) {
          // An Admin approves their own post in the same click; the server still records a human approval of this exact
          // version and refuses it (distinct-reviewer policy, blocking claims), in which case the review page shows why.
          try {
            await firstValueFrom(this.reviewsApi.approve(orgId, review));
          } catch (error) {
            this.toast.error(toApiProblem(error).detail);
            await this.router.navigate(['/approvals', review.id]);
            return;
          }
        }
      }
      if (this.workspace.isAdmin()) {
        this.toast.success('Approved. Schedule or publish it from here.');
        await this.router.navigate(['/approvals'], { queryParams: { tab: 'approved' } });
        return;
      }
      this.toast.success(`Submitted for review${posts.length > 1 ? ` for ${posts.map((p) => p.platform).join(', ')}` : ''}. You can find it in Planner.`);
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
  private async ensureSaved(): Promise<Post | null> {
    const orgId = this.workspace.orgId();
    if (!orgId) return null;
    const current = this.post();
    if (current && !this.dirty()) return current;
    if (!this.platform()) throw new Error('Choose where to publish first.');
    if (!this.text().trim()) throw new Error('Write the post content first.');

    if (!current) {
      const created = await firstValueFrom(this.postsApi.create(orgId, this.platform()!, this.content(null)));
      this.apply(created);
      // Same page instance keeps running (e.g. an AI edit that needed the draft saved first); only the URL changes.
      this.location.replaceState(`/${this.workspace.slug()}/composer/${created.id}`);
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
    return {
      hook: unchangedText ? current.hook : null,
      body: unchangedText ? current.body : this.text().trim(),
      callToAction: unchangedText ? current.callToAction : null,
      hashtags: unchangedText ? current.hashtags : [],
      destinationUrl: this.link().trim() || null,
      images: this.images().map((i) => ({ mediaAssetId: i.asset.id, altText: i.altText.trim() || null })),
      socialAccountId: this.accountId() || null,
    };
  }

  private async loadWorkspaceData(orgId: string): Promise<void> {
    void firstValueFrom(this.ai.capabilities(orgId)).then((c) => {
      this.caps.set(c);
    }, () => undefined);
    const accounts = await firstValueFrom(this.accountsApi.list(orgId)).catch(() => []);
    this.accounts.set(accounts);
    if (this.isNew() && !this.platform()) {
      const first = SOCIAL_PLATFORMS.find((p) => this.connected(p));
      if (first) {
        this.choosePlatform(first);
        this.targets.set([first]);
      }
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
    // The chosen platforms survive saving the same post again (or creating it); another post starts with its own platform.
    const previous = this.post();
    if (previous ? previous.id !== post.id : !this.targets().includes(post.platform)) this.targets.set([post.platform]);
    this.post.set(post);
    this.platform.set(post.platform);
    this.accountId.set(v.socialAccountId ?? '');
    this.text.set(postText(v));
    this.link.set(v.destinationUrl ?? '');
    this.images.set(v.images.map((i) => ({ asset: i.media, altText: i.altText ?? '' })));
    this.original.set({ text: postText(v), link: v.destinationUrl ?? '', images: imagesKey(v.images), accountId: v.socialAccountId ?? '' });
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
