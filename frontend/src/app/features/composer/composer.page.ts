import { DatePipe, Location } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { Member, OrganizationApiService } from '../../core/services/organization-api.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { DEMO_MODE_NOTE, customerStatus, statusTone } from '../../shared/models/states';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { CampaignSummary, SOCIAL_PLATFORMS, SocialPlatform } from '../campaigns/models/campaign.model';
import { CampaignApiService } from '../campaigns/services/campaign-api.service';
import { ContentEvaluation, REWRITE_ACTIONS, RewriteAction } from '../content/models/ai.model';
import { MediaAsset, MediaLibraryItem, PLATFORM_TEXT_LIMITS, Post, PostContent, PostVersion, postText } from '../content/models/post.model';
import { AiApiService } from '../content/services/ai-api.service';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { PublicationPanelComponent } from '../publications/components/publication-panel.component';
import { PublicationJob } from '../publications/models/publication.model';
import { SocialAccount } from '../social-accounts/models/social-account.model';
import { SocialAccountApiService } from '../social-accounts/services/social-account-api.service';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

/**
 * Composer: where a real post is prepared. A new post (/composer, optionally ?campaignId=) is created on Save Draft;
 * an existing one (/composer/{id}) is edited by saving a new immutable version. Review, approval and publishing all
 * apply to the exact current version; the server enforces every rule, the page only guides.
 */
@Component({
  selector: 'app-composer-page',
  imports: [FormsModule, RouterLink, DatePipe, IconComponent, PlatformIconComponent, ProblemBannerComponent, PublicationPanelComponent],
  templateUrl: './composer.page.html',
})
export class ComposerPage {
  private readonly postsApi = inject(PostApiService);
  private readonly ai = inject(AiApiService);
  private readonly media = inject(MediaApiService);
  private readonly campaignsApi = inject(CampaignApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly organizations = inject(OrganizationApiService);
  private readonly auth = inject(AuthService);
  private readonly location = inject(Location);
  protected readonly workspace = inject(WorkspaceService);
  private destroyed = false;

  /** Route parameter (existing post) and query parameter (campaign to file a new post in). */
  readonly postId = input<string>();
  readonly campaignId = input<string>();

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly aiActions = REWRITE_ACTIONS;
  protected readonly demoNote = DEMO_MODE_NOTE;
  protected readonly tone = statusTone;
  protected readonly reviewLabels: Record<string, string> = { PENDING: 'In Review', APPROVED: 'Approved', CHANGES_REQUESTED: 'Changes Requested' };
  protected readonly sourceLabels: Record<string, string> = { Manual: 'Edited', AiGeneration: 'AI generated', AiRewrite: 'AI edit', AiImage: 'AI image' };

  // Loaded data
  protected readonly post = signal<Post | null>(null);
  protected readonly versions = signal<PostVersion[]>([]);
  protected readonly accounts = signal<SocialAccount[]>([]);
  protected readonly campaigns = signal<CampaignSummary[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly members = signal<Member[]>([]);
  protected readonly evaluations = signal<ContentEvaluation[]>([]);
  protected readonly jobs = signal<PublicationJob[]>([]);
  protected readonly library = signal<MediaLibraryItem[] | null>(null);
  protected readonly imageAvailable = signal(false);

  // Editor state
  protected readonly platform = signal<SocialPlatform | null>(null);
  protected readonly accountId = signal<string>('');
  protected readonly text = signal('');
  protected readonly link = signal('');
  protected readonly image = signal<MediaAsset | null>(null);
  protected readonly altText = signal('');
  protected readonly campaign = signal<string>('');
  protected readonly reviewerId = signal<string>('');
  protected readonly reviewNote = signal('');
  protected readonly imagePromptOpen = signal(false);
  protected readonly imagePrompt = signal('');

  // Activity
  protected readonly loading = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
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
  protected readonly status = computed(() => customerStatus(this.state(), this.jobs().at(0)?.deliveryState ?? null));
  protected readonly limit = computed(() => (this.platform() ? PLATFORM_TEXT_LIMITS[this.platform()!] : null));
  protected readonly length = computed(() => this.text().length + (this.link().trim() ? this.link().trim().length + 2 : 0));
  protected readonly platformAccounts = computed(() => this.accounts().filter((a) => a.platform === this.platform()));
  protected readonly selectedAccount = computed(() => this.accounts().find((a) => a.id === this.accountId()) ?? null);
  protected readonly selectedCampaign = computed(() => this.campaigns().find((c) => c.id === this.campaign()) ?? null);
  protected readonly reviewers = computed(() => {
    const me = this.auth.me()?.userId;
    const distinct = this.workspace.organization()?.requireDistinctReviewer ?? true;
    return this.members().filter((m) => m.status === 'Active' && (m.role === 'Reviewer' || m.role === 'Admin') && !(distinct && m.userId === me));
  });
  protected readonly latestReview = computed(() => this.reviews().at(0) ?? null);
  protected readonly changesRequested = computed(() => this.reviews().find((r) => r.status === 'CHANGES_REQUESTED') ?? null);
  protected readonly evaluation = computed(() => this.evaluations().find((e) => e.postVersionId === this.post()?.currentVersionId) ?? null);
  protected readonly blockingIssues = computed(() => (this.evaluation()?.issues ?? []).filter((i) => i.severity !== 'INFO'));

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
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

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const orgId = this.workspace.orgId();
    if (!file || !orgId) return;
    // Early hints only; the server inspects the bytes and applies the authoritative rules.
    if (!['image/jpeg', 'image/png'].includes(file.type)) return this.fail('Choose a JPEG or PNG image.');
    if (file.size > MAX_IMAGE_BYTES) return this.fail('Images must be at most 8 MB.');
    await this.run('upload', async () => {
      this.setImage(await firstValueFrom(this.media.upload(orgId, file)));
      this.notice.set('Image added. Save Draft to keep it.');
    });
  }

  protected async openLibrary(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    if (this.library()) return this.library.set(null);
    await this.run('library', async () => this.library.set(await firstValueFrom(this.media.list(orgId))));
  }

  protected pick(asset: MediaAsset): void {
    this.setImage(asset);
    this.library.set(null);
  }

  protected removeImage(): void {
    this.setImage(null);
  }

  /** Saves first if needed, then queues an AI image; it arrives as a new version of the post. */
  protected async generateImage(): Promise<void> {
    const orgId = this.workspace.orgId();
    await this.run('image', async () => {
      const post = await this.ensureSaved();
      if (!orgId || !post) return;
      const queued = await firstValueFrom(this.ai.requestImage(orgId, post.id, this.imagePrompt().trim() || null, post.rowVersion));
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed, 3000);
      await this.load(orgId, post.id);
      if (final.status === 'FAILED') throw new Error(final.failureCode === 'AI_PROVIDER_REJECTED' ? 'The image service declined this request.' : 'The image could not be generated. Please try again.');
      this.imagePromptOpen.set(false);
      this.notice.set(`AI image added as version ${this.version()?.versionNumber}. Add alt text if you like, then submit for review.`);
    });
  }

  // ---- AI edits -------------------------------------------------------------------------------------

  protected async aiEdit(action: RewriteAction): Promise<void> {
    const orgId = this.workspace.orgId();
    await this.run(action, async () => {
      const post = await this.ensureSaved();
      if (!orgId || !post) return;
      const queued = await firstValueFrom(this.ai.requestRewrite(orgId, post.id, action, null, post.rowVersion));
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed);
      await this.load(orgId, post.id);
      if (final.status === 'FAILED') {
        throw new Error(final.failureCode === 'AI_NOT_CONFIGURED' || final.failureCode === 'AI_PROVIDER_AUTH'
          ? 'AI writing is temporarily unavailable. Please try again later.' : 'The AI edit did not complete. Please try again.');
      }
      this.notice.set(`AI edit saved as version ${this.version()?.versionNumber}. The previous version is kept in the history.`);
    });
  }

  // ---- Save, campaign, review -----------------------------------------------------------------------

  protected async saveDraft(): Promise<void> {
    await this.run('save', async () => {
      const before = this.post()?.currentVersionId;
      const post = await this.ensureSaved(true);
      if (post && !this.isNew()) {
        this.notice.set(post.currentVersionId !== before ? `Saved as version ${post.currentVersion.versionNumber}.` : 'No changes to save.');
      }
    });
  }

  /** Campaign is a folder: moving the post creates no version and keeps any approval. */
  protected async changeCampaign(campaignId: string): Promise<void> {
    this.campaign.set(campaignId);
    const orgId = this.workspace.orgId();
    const post = this.post();
    if (!orgId || !post || (post.campaignId ?? '') === campaignId) return;
    await this.run('campaign', async () => {
      const moved = await firstValueFrom(this.postsApi.moveToCampaign(orgId, post.id, campaignId || null, post.rowVersion));
      this.post.set(moved);
      this.notice.set(campaignId ? `Moved to ${this.selectedCampaign()?.name}.` : 'Removed from its campaign.');
    });
  }

  protected async submitForReview(): Promise<void> {
    const orgId = this.workspace.orgId();
    await this.run('submit', async () => {
      const post = await this.ensureSaved();
      if (!orgId || !post) return;
      const review = await firstValueFrom(this.reviewsApi.submit(orgId, post.id, this.reviewNote().trim() || null, this.reviewerId() || null, post.rowVersion));
      this.reviewNote.set('');
      await this.load(orgId, post.id);
      this.notice.set(`Version ${review.versionNumber} sent for review${review.assignedReviewerName ? ' to ' + review.assignedReviewerName : ''}. Editing is locked until a decision.`);
    });
  }

  protected reload(): void {
    const orgId = this.workspace.orgId();
    const postId = this.postId();
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
      this.apply(created, [created.currentVersion]);
      // Same page instance keeps running (e.g. an AI edit that needed the draft saved first); only the URL changes.
      this.location.replaceState(`/${this.workspace.slug()}/composer/${created.id}`);
      this.notice.set('Draft saved.');
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
    const [accounts, campaigns, members, capabilities] = await Promise.all([
      firstValueFrom(this.accountsApi.list(orgId)).catch(() => []),
      firstValueFrom(this.campaignsApi.list(orgId)).catch(() => []),
      firstValueFrom(this.organizations.members(orgId)).catch(() => []),
      firstValueFrom(this.ai.capabilities(orgId)).catch(() => null),
    ]);
    this.accounts.set(accounts);
    this.campaigns.set(campaigns);
    this.members.set(members);
    this.imageAvailable.set(capabilities?.imageGeneration ?? false);
    if (!this.reviewerId()) this.reviewerId.set(this.reviewers().find((m) => m.role === 'Reviewer')?.userId ?? this.reviewers()[0]?.userId ?? '');
    if (this.isNew() && !this.platform()) {
      const first = SOCIAL_PLATFORMS.find((p) => this.connected(p));
      if (first) this.choosePlatform(first);
    }
    this.preselectAccount();
  }

  private async load(orgId: string, postId: string): Promise<void> {
    this.loading.set(true);
    try {
      const [post, versions, evaluations, reviews] = await Promise.all([
        firstValueFrom(this.postsApi.get(orgId, postId)),
        firstValueFrom(this.postsApi.versions(orgId, postId)),
        firstValueFrom(this.ai.evaluations(orgId, postId)).catch(() => []),
        firstValueFrom(this.reviewsApi.list(orgId, { postId })).catch(() => []),
      ]);
      this.evaluations.set(evaluations);
      this.reviews.set(reviews);
      this.apply(post, versions);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  private apply(post: Post, versions: PostVersion[]): void {
    const v = post.currentVersion;
    this.post.set(post);
    this.versions.set(versions);
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
    this.notice.set(null);
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
