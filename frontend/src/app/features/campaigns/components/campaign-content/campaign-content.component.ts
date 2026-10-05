import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { IconComponent } from '../../../../shared/components/icon.component';
import { PlatformIconComponent } from '../../../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { MODE_LABELS, customerStatus, statusTone } from '../../../../shared/models/states';
import { ReviewApiService } from '../../../approvals/services/review-api.service';
import { AiGenerationRequest, ContentEvaluation, REWRITE_ACTIONS, RewriteAction, isActive } from '../../../content/models/ai.model';
import { Post, PostContent } from '../../../content/models/post.model';
import { AiApiService } from '../../../content/services/ai-api.service';
import { MediaApiService } from '../../../content/services/media-api.service';
import { PostApiService } from '../../../content/services/post-api.service';
import { PublicationApiService } from '../../../publications/services/publication-api.service';
import { SocialAccount } from '../../../social-accounts/models/social-account.model';
import { SocialAccountApiService } from '../../../social-accounts/services/social-account-api.service';
import { SocialPlatform } from '../../models/campaign.model';

interface PlatformCard {
  platform: SocialPlatform;
  post: Post | null;
  evaluation: ContentEvaluation | null;
  deliveryState: string | null;
  reviewId: string | null;
}

/**
 * Generated campaign content: one independently written post per platform, each with its own versions, review state
 * and publishing state. AI actions are durable requests (enqueue, then poll); every AI result is a new version, so a
 * reviewed version is never overwritten. Submission always covers the exact saved current version.
 */
@Component({
  selector: 'app-campaign-content',
  imports: [RouterLink, FormsModule, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  templateUrl: './campaign-content.component.html',
})
export class CampaignContentComponent {
  private readonly posts = inject(PostApiService);
  private readonly ai = inject(AiApiService);
  private readonly media = inject(MediaApiService);
  private readonly publications = inject(PublicationApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private destroyed = false;

  readonly orgId = input.required<string>();
  readonly campaignId = input.required<string>();
  readonly platforms = input.required<SocialPlatform[]>();
  readonly editable = input(false);
  /** Start generation as soon as the tab opens (from "Generate Content"). */
  readonly autoGenerate = input(false);

  protected readonly rewriteActions = REWRITE_ACTIONS.filter((a) => a.value !== 'Custom');
  protected readonly statusTone = statusTone;
  protected readonly modeLabels = MODE_LABELS;
  protected readonly cards = signal<PlatformCard[]>([]);
  protected readonly accounts = signal<SocialAccount[]>([]);
  protected readonly request = signal<AiGenerationRequest | null>(null);
  protected readonly cardBusy = signal<Record<string, string>>({});
  protected readonly destination = signal<Record<string, string>>({});
  protected readonly customOpen = signal<string | null>(null);
  protected readonly customText = signal('');
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly generating = computed(() => isActive(this.request()));
  protected readonly anyPost = computed(() => this.cards().some((c) => c.post));
  private autoStarted = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
    effect(() => void this.init(this.orgId(), this.campaignId(), this.platforms()));
  }

  protected status(card: PlatformCard): string {
    return card.post ? customerStatus(card.post.editorialState, card.deliveryState) : 'Not generated';
  }

  protected canWork(card: PlatformCard): boolean {
    const state = card.post?.editorialState;
    return this.editable() && (state === 'DRAFT' || state === 'CHANGES_REQUESTED');
  }

  /** Whether the platform offers AI image generation (configured by the platform operator). */
  protected readonly imageAvailable = signal(false);
  protected readonly imageText = signal<Record<string, string>>({});

  protected setImageText(platform: SocialPlatform, text: string): void {
    this.imageText.update((all) => ({ ...all, [platform]: text }));
  }

  /** Durable request; the image arrives as a new version (text unchanged) that is reviewed like any other change. */
  protected async generateImage(card: PlatformCard): Promise<void> {
    const post = card.post;
    if (!post) return;
    await this.onCard(post.id, 'Generating image… this can take up to a minute.', async () => {
      const instruction = (this.imageText()[card.platform] ?? '').trim() || null;
      const queued = await firstValueFrom(this.ai.requestImage(this.orgId(), post.id, instruction, post.rowVersion));
      const final = await this.ai.waitForCompletion(this.orgId(), queued, () => undefined, () => this.destroyed, 3000);
      if (final.status === 'FAILED') throw friendlyImageFailure(final);
      this.setImageText(card.platform, '');
      await this.refresh();
      this.notice.set(`${card.platform}: new image saved as Version ${this.cards().find((c) => c.platform === card.platform)?.post?.currentVersion.versionNumber}. It needs review before scheduling.`);
    });
  }

  protected setDestination(platform: SocialPlatform, accountId: string): void {
    this.destination.update((all) => ({ ...all, [platform]: accountId }));
  }

  protected destinationsFor(platform: SocialPlatform): SocialAccount[] {
    return this.accounts().filter((a) => a.platform === platform && a.isEligibleDestination);
  }

  protected destinationName(id: string | null): string | null {
    const account = this.accounts().find((a) => a.id === id);
    return account ? `${account.displayName}${account.executionMode === 'SIMULATED' ? ' (Demo Mode)' : ''}` : null;
  }

  protected imageUrl(mediaAssetId: string): string {
    return this.media.previewUrl(this.orgId(), mediaAssetId);
  }

  protected countOf(evaluation: ContentEvaluation | null, severity: 'CRITICAL' | 'WARNING'): number {
    return evaluation?.issues.filter((i) => i.severity === severity).length ?? 0;
  }

  /** Generates every platform without a post, or regenerates one platform's draft. */
  protected async generate(platform?: SocialPlatform): Promise<void> {
    this.problem.set(null);
    this.notice.set(null);
    try {
      const queued = await firstValueFrom(this.ai.requestGeneration(this.orgId(), this.campaignId(), platform ? [platform] : undefined));
      this.request.set(queued);
      await this.follow(queued);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected async rewrite(card: PlatformCard, action: RewriteAction): Promise<void> {
    const post = card.post;
    if (!post) return;
    const instruction = action === 'Custom' ? this.customText().trim() : null;
    if (action === 'Custom' && !instruction) return;
    await this.onCard(post.id, 'Rewriting…', async () => {
      const queued = await firstValueFrom(this.ai.requestRewrite(this.orgId(), post.id, action, instruction, post.rowVersion));
      const final = await this.ai.waitForCompletion(this.orgId(), queued, () => undefined, () => this.destroyed);
      if (final.status === 'FAILED') throw friendlyAiFailure(final);
      this.customOpen.set(null);
      this.customText.set('');
      await this.refresh();
      this.notice.set(`${card.platform}: saved as Version ${this.cards().find((c) => c.platform === card.platform)?.post?.currentVersion.versionNumber}. Earlier versions are kept.`);
    });
  }

  /** Assigns the chosen destination (a new version, because the destination is part of the approved content), then submits. */
  protected async submit(card: PlatformCard): Promise<void> {
    let post = card.post;
    if (!post) return;
    const chosen = this.destination()[card.platform] ?? '';
    await this.onCard(post.id, 'Submitting…', async () => {
      if (!post!.currentVersion.socialAccountId) {
        if (!chosen) throw { status: 0, code: 'DESTINATION_REQUIRED', detail: `Choose the ${card.platform} account this post is for.` } satisfies ApiProblem;
        post = await firstValueFrom(this.posts.saveNewVersion(this.orgId(), post!.id, { ...contentOf(post!), socialAccountId: chosen }, post!.rowVersion));
      }
      const review = await firstValueFrom(this.reviewsApi.submit(this.orgId(), post!.id, null, post!.rowVersion));
      await this.refresh();
      this.notice.set(`${card.platform} Version ${review.versionNumber} was sent for review.`);
    });
  }

  private async onCard(postId: string, label: string, work: () => Promise<void>): Promise<void> {
    this.cardBusy.update((all) => ({ ...all, [postId]: label }));
    this.problem.set(null);
    this.notice.set(null);
    try {
      await work();
    } catch (error) {
      this.problem.set(isProblem(error) ? error : toApiProblem(error));
    } finally {
      this.cardBusy.update(({ [postId]: _, ...rest }) => rest);
    }
  }

  private async init(orgId: string, campaignId: string, platforms: SocialPlatform[]): Promise<void> {
    this.loading.set(true);
    try {
      this.accounts.set(await firstValueFrom(this.accountsApi.list(orgId)));
      this.imageAvailable.set((await firstValueFrom(this.ai.capabilities(orgId)).catch(() => null))?.imageGeneration ?? false);
      await this.loadCards(orgId, campaignId, platforms);
      const latest = (await firstValueFrom(this.ai.listForCampaign(orgId, campaignId))).find((r) => r.kind === 'Generation') ?? null;
      this.request.set(latest);
      if (latest && isActive(latest)) {
        await this.follow(latest);
      } else if (this.autoGenerate() && !this.autoStarted && this.editable() && this.cards().some((c) => !c.post)) {
        this.autoStarted = true;
        await this.generate();
      }
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  private async follow(request: AiGenerationRequest): Promise<void> {
    const final = await this.ai.waitForCompletion(this.orgId(), request, (r) => this.request.set(r), () => this.destroyed);
    if (final.status === 'COMPLETED') await this.loadCards(this.orgId(), this.campaignId(), this.platforms());
  }

  private refresh(): Promise<void> {
    return this.loadCards(this.orgId(), this.campaignId(), this.platforms());
  }

  private async loadCards(orgId: string, campaignId: string, platforms: SocialPlatform[]): Promise<void> {
    const summaries = (await firstValueFrom(this.posts.listForCampaign(orgId, campaignId))).filter((p) => p.editorialState !== 'ARCHIVED');
    this.cards.set(await Promise.all(platforms.map(async (platform): Promise<PlatformCard> => {
      const summary = summaries.find((p) => p.platform === platform);
      if (!summary) return { platform, post: null, evaluation: null, deliveryState: null, reviewId: null };
      const [post, evaluations, jobs, reviews] = await Promise.all([
        firstValueFrom(this.posts.get(orgId, summary.id)),
        firstValueFrom(this.ai.evaluations(orgId, summary.id)),
        firstValueFrom(this.publications.list(orgId, { postId: summary.id })),
        firstValueFrom(this.reviewsApi.list(orgId, { postId: summary.id })),
      ]);
      return {
        platform,
        post,
        evaluation: evaluations.find((e) => e.postVersionId === post.currentVersionId) ?? null,
        deliveryState: jobs[0]?.deliveryState ?? null,
        reviewId: reviews[0]?.id ?? null,
      };
    })));
  }
}

function contentOf(post: Post): PostContent {
  const v = post.currentVersion;
  return {
    hook: v.hook, body: v.body, callToAction: v.callToAction, destinationUrl: v.destinationUrl, hashtags: v.hashtags,
    mediaAssetId: v.mediaAssetId, altText: v.altText, socialAccountId: v.socialAccountId,
  };
}

function isProblem(value: unknown): value is ApiProblem {
  return typeof value === 'object' && value !== null && 'detail' in value && 'code' in value;
}

/** Image failures in customer language (content-policy refusals and unusable images explain what to change). */
function friendlyImageFailure(request: AiGenerationRequest): ApiProblem {
  const detail = request.failureCode === 'AI_PROVIDER_REJECTED'
    ? 'The image service declined this request. Try a different image style.'
    : request.failureCode === 'AI_IMAGE_NOT_SUITABLE'
      ? 'The generated image does not fit this platform. Please contact support (image settings may need a change).'
      : friendlyAiFailure(request).detail;
  return { status: 0, code: request.failureCode ?? 'AI_GENERATION_FAILED', detail };
}

/** AI failures in customer language; provider and model details stay internal. */
export function friendlyAiFailure(request: AiGenerationRequest): ApiProblem {
  const unavailable = request.failureCode === 'AI_NOT_CONFIGURED' || request.failureCode === 'AI_PROVIDER_AUTH';
  return {
    status: 0,
    code: request.failureCode ?? 'AI_GENERATION_FAILED',
    detail: unavailable ? 'AI writing is temporarily unavailable. Please try again later or contact support.' : 'The AI could not finish this request. Please try again.',
  };
}
