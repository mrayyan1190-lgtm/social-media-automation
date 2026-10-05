import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { fromList, toList } from '../../../../shared/utilities/list-text';
import { httpUrlValidator } from '../../../../shared/validators/http-url.validator';
import { CTA_TEXT, SocialPlatform } from '../../../campaigns/models/campaign.model';
import { CampaignApiService } from '../../../campaigns/services/campaign-api.service';
import { AiGenerationRequest, ContentEvaluation, REWRITE_ACTIONS, RewriteAction, isActive } from '../../models/ai.model';
import { MediaAsset, PLATFORM_TEXT_LIMITS, Post, PostContent, PostVersion } from '../../models/post.model';
import { AiApiService } from '../../services/ai-api.service';
import { ReviewSummary } from '../../../approvals/models/review.model';
import { ReviewApiService } from '../../../approvals/services/review-api.service';
import { SocialAccount } from '../../../social-accounts/models/social-account.model';
import { SocialAccountApiService } from '../../../social-accounts/services/social-account-api.service';
import { MediaApiService } from '../../services/media-api.service';
import { PostApiService } from '../../services/post-api.service';
import { PublicationPanelComponent } from '../../../publications/components/publication-panel.component';
import { DEMO_MODE_NOTE, EDITORIAL_LABELS, MODE_LABELS } from '../../../../shared/models/states';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

/**
 * Edits one platform post. Saving never changes the shown version: it creates the next immutable version.
 * Route forms: /posts/:postId, or /posts/new?campaignId=…&platform=… to create the post with Version 1.
 */
@Component({
  selector: 'app-post-editor-page',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, ProblemBannerComponent, PublicationPanelComponent],
  templateUrl: './post-editor.page.html',
})
export class PostEditorPage {
  protected readonly editorialLabels = EDITORIAL_LABELS;
  protected readonly sourceLabels: Record<string, string> = { Manual: 'Edited', AiGeneration: 'AI generated', AiRewrite: 'AI rewrite', AiImage: 'AI image' };
  protected readonly reviewLabels: Record<string, string> = { PENDING: 'In Review', APPROVED: 'Approved', CHANGES_REQUESTED: 'Changes Requested' };
  protected readonly modeLabels = MODE_LABELS;
  protected readonly demoNote = DEMO_MODE_NOTE;
  private readonly posts = inject(PostApiService);
  private readonly media = inject(MediaApiService);
  private readonly campaigns = inject(CampaignApiService);
  private readonly router = inject(Router);
  private readonly ai = inject(AiApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private destroyed = false;
  protected readonly workspace = inject(WorkspaceService);

  /** Route parameter; absent in "new" mode. */
  readonly postId = input<string>();
  /** Query parameters used in "new" mode. */
  readonly campaignId = input<string>();
  readonly platform = input<SocialPlatform>();

  protected readonly post = signal<Post | null>(null);
  protected readonly versions = signal<PostVersion[]>([]);
  protected readonly viewing = signal<PostVersion | null>(null);
  protected readonly image = signal<MediaAsset | null>(null);
  protected readonly busy = signal(false);
  protected readonly uploading = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly uploadError = signal<string | null>(null);
  protected readonly evaluations = signal<ContentEvaluation[]>([]);
  protected readonly rewriteRequest = signal<AiGenerationRequest | null>(null);
  protected readonly rewriteActions = REWRITE_ACTIONS;
  protected readonly rewriteAction = signal<RewriteAction>('Shorten');
  protected readonly customInstruction = signal('');
  protected readonly rewriting = computed(() => isActive(this.rewriteRequest()));
  /** Findings for the version being inspected in the history, or for the current version. */
  protected readonly destinations = signal<SocialAccount[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly submissionNote = signal('');
  protected readonly canSubmit = computed(() => this.editable() && this.post()?.editorialState === 'DRAFT');
  protected readonly selectedDestination = computed(() => this.destinations().find((d) => d.id === this.formValue().socialAccountId) ?? null);
  protected readonly shownEvaluation = computed(() => {
    const versionId = this.viewing()?.id ?? this.post()?.currentVersionId;
    return this.evaluations().find((e) => e.postVersionId === versionId) ?? null;
  });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    hook: ['', Validators.maxLength(300)],
    body: ['', Validators.required],
    callToAction: ['', Validators.maxLength(200)],
    destinationUrl: ['', httpUrlValidator],
    hashtags: [''],
    altText: ['', Validators.maxLength(1000)],
    socialAccountId: [''],
  });

  protected readonly currentPlatform = computed(() => this.post()?.platform ?? this.platform() ?? null);
  protected readonly ownerCampaignId = computed(() => this.post()?.campaignId ?? this.campaignId() ?? null);
  protected readonly isNew = computed(() => !this.postId());
  protected readonly editable = computed(() => {
    const state = this.post()?.editorialState;
    return this.workspace.canManageCampaigns() && state !== 'IN_REVIEW' && state !== 'ARCHIVED';
  });

  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly textLength = computed(() => {
    const v = this.formValue();
    const tags = toList(v.hashtags ?? '').map((t) => (t.startsWith('#') ? t : `#${t}`)).join(' ');
    return [v.hook, v.body, v.callToAction, v.destinationUrl, tags].filter((p) => !!p?.trim()).reduce((n, p) => n + p!.length + 2, -2);
  });
  protected readonly textLimit = computed(() => {
    const platform = this.currentPlatform();
    return platform ? PLATFORM_TEXT_LIMITS[platform] : null;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
    effect(() => {
      const orgId = this.workspace.orgId();
      const postId = this.postId();
      if (orgId && postId) void this.load(orgId, postId);
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      const campaignId = this.campaignId();
      if (orgId && !this.postId() && campaignId) void this.prefillFromCampaign(orgId, campaignId);
    });
    effect(() => (this.editable() ? this.form.enable() : this.form.disable()));
  }

  protected previewUrl(asset: MediaAsset): string {
    return this.media.previewUrl(this.workspace.orgId()!, asset.id);
  }

  async load(orgId = this.workspace.orgId(), postId = this.postId()): Promise<void> {
    if (!orgId || !postId) return;
    this.problem.set(null);
    try {
      const [post, versions, evaluations] = await Promise.all([
        firstValueFrom(this.posts.get(orgId, postId)),
        firstValueFrom(this.posts.versions(orgId, postId)),
        firstValueFrom(this.ai.evaluations(orgId, postId)),
      ]);
      this.evaluations.set(evaluations);
      this.imageAvailable.set((await firstValueFrom(this.ai.capabilities(orgId)).catch(() => null))?.imageGeneration ?? false);
      this.reviews.set(await firstValueFrom(this.reviewsApi.list(orgId, { postId })));
      this.destinations.set(await firstValueFrom(this.accountsApi.list(orgId, post.platform)));
      this.apply(post, versions);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const orgId = this.workspace.orgId();
    if (!file || !orgId) return;

    this.uploadError.set(null);
    // Early hints only; the server inspects the bytes and applies the authoritative rules.
    if (!['image/jpeg', 'image/png'].includes(file.type)) {
      this.uploadError.set('Choose a JPEG or PNG image.');
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      this.uploadError.set('Images must be at most 8 MB.');
      return;
    }
    this.uploading.set(true);
    try {
      this.image.set(await firstValueFrom(this.media.upload(orgId, file)));
      // Alt text describes a specific image, so a replacement starts without the old description.
      this.form.controls.altText.setValue('');
      this.notice.set('Image uploaded. Save to create a new version with it.');
    } catch (error) {
      this.uploadError.set(toApiProblem(error).detail);
    } finally {
      this.uploading.set(false);
    }
  }

  protected removeImage(): void {
    this.image.set(null);
    this.form.controls.altText.setValue('');
  }

  protected async save(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    this.notice.set(null);
    try {
      const current = this.post();
      if (!current) {
        const created = await firstValueFrom(this.posts.create(orgId, this.campaignId()!, this.platform()!, this.content()));
        await this.router.navigate(['/posts', created.id], { replaceUrl: true });
        return;
      }
      const saved = await firstValueFrom(this.posts.saveNewVersion(orgId, current.id, this.content(), current.rowVersion));
      const changed = saved.currentVersionId !== current.currentVersionId;
      this.apply(saved, await firstValueFrom(this.posts.versions(orgId, saved.id)));
      this.notice.set(changed ? `Saved as version ${saved.currentVersion.versionNumber}.` : 'No public-facing changes, so no new version was created.');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** Submits the saved current version for human review; the server re-checks destination, media and blockers. */
  protected async submitForReview(): Promise<void> {
    const orgId = this.workspace.orgId();
    const post = this.post();
    if (!orgId || !post) return;
    if (this.form.dirty) {
      this.problem.set({ status: 0, code: 'UNSAVED_CHANGES', detail: 'Save your edits as a new version first; review always covers a saved version.' });
      return;
    }
    this.busy.set(true);
    this.problem.set(null);
    this.notice.set(null);
    try {
      const review = await firstValueFrom(this.reviewsApi.submit(orgId, post.id, this.submissionNote().trim() || null, post.rowVersion));
      this.submissionNote.set('');
      await this.load(orgId, post.id);
      this.notice.set(`Version ${review.versionNumber} was submitted for review. Editing is locked until a reviewer decides.`);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected readonly imageAvailable = signal(false);
  protected readonly imageBusy = signal(false);

  /** Queues an AI image for the saved current version; it arrives as a new version (text unchanged) that needs review. */
  protected async generateImage(): Promise<void> {
    const orgId = this.workspace.orgId();
    const post = this.post();
    if (!orgId || !post) return;
    this.imageBusy.set(true);
    this.problem.set(null);
    this.notice.set(null);
    try {
      const queued = await firstValueFrom(this.ai.requestImage(orgId, post.id, null, post.rowVersion));
      const final = await this.ai.waitForCompletion(orgId, queued, () => undefined, () => this.destroyed, 3000);
      if (final.status === 'FAILED') {
        this.problem.set({ status: 0, code: final.failureCode ?? 'AI_GENERATION_FAILED',
          detail: final.failureCode === 'AI_PROVIDER_REJECTED' ? 'The image service declined this request.' : 'The image could not be generated. Please try again.' });
        return;
      }
      await this.load(orgId, post.id);
      this.notice.set(`AI image saved as version ${this.post()?.currentVersion.versionNumber}. Add alt text, then submit it for review.`);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.imageBusy.set(false);
    }
  }

  /** Queues a durable AI rewrite of the saved current version; the result arrives as a new version. */
  protected async rewrite(): Promise<void> {
    const orgId = this.workspace.orgId();
    const post = this.post();
    if (!orgId || !post) return;
    if (this.form.dirty) {
      this.problem.set({ status: 0, code: 'UNSAVED_CHANGES', detail: 'Save or discard your edits first; the rewrite starts from the saved current version.' });
      return;
    }
    this.problem.set(null);
    this.notice.set(null);
    try {
      const action = this.rewriteAction();
      const queued = await firstValueFrom(
        this.ai.requestRewrite(orgId, post.id, action, action === 'Custom' ? this.customInstruction() : null, post.rowVersion));
      this.rewriteRequest.set(queued);
      const final = await this.ai.waitForCompletion(orgId, queued, (r) => this.rewriteRequest.set(r), () => this.destroyed);
      if (final.status === 'COMPLETED') {
        await this.load(orgId, post.id);
        this.notice.set(`AI rewrite saved as version ${this.post()?.currentVersion.versionNumber}. The previous version is unchanged in the history.`);
      }
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  private content(): PostContent {
    const v = this.form.getRawValue();
    const image = this.image();
    return {
      hook: v.hook.trim() || null,
      body: v.body,
      callToAction: v.callToAction.trim() || null,
      destinationUrl: v.destinationUrl.trim() || null,
      hashtags: toList(v.hashtags),
      mediaAssetId: image?.id ?? null,
      altText: image ? v.altText.trim() || null : null,
      socialAccountId: v.socialAccountId || null,
    };
  }

  private apply(post: Post, versions: PostVersion[]): void {
    this.post.set(post);
    this.versions.set(versions);
    this.viewing.set(null);
    const v = post.currentVersion;
    this.image.set(v.media);
    this.form.reset({
      hook: v.hook ?? '',
      body: v.body,
      callToAction: v.callToAction ?? '',
      destinationUrl: v.destinationUrl ?? '',
      hashtags: fromList(v.hashtags),
      altText: v.altText ?? '',
      socialAccountId: v.socialAccountId ?? '',
    });
  }

  private async prefillFromCampaign(orgId: string, campaignId: string): Promise<void> {
    try {
      const platform = this.platform();
      if (platform) this.destinations.set(await firstValueFrom(this.accountsApi.list(orgId, platform)));
      const campaign = await firstValueFrom(this.campaigns.get(orgId, campaignId));
      this.form.patchValue({ callToAction: CTA_TEXT[campaign.callToAction], destinationUrl: campaign.callToActionUrl ?? '' });
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }
}
