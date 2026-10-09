import { Component, DestroyRef, ElementRef, ViewEncapsulation, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { ToastService } from '../../core/services/toast.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { ContentLength, EmojiMode, HashtagMode, SOCIAL_PLATFORMS, SocialPlatform } from '../../shared/models/platform.model';
import { BLOCKED_HINT, ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { AiCapabilities, AiGenerationRequest } from '../content/models/ai.model';
import { MediaAsset, Post, PostContent, PostVersion, postText } from '../content/models/post.model';
import { AiApiService } from '../content/services/ai-api.service';
import { MediaApiService } from '../content/services/media-api.service';
import { PostApiService } from '../content/services/post-api.service';
import { BRAND_TONES, BrandProfile, BrandTone } from '../settings/models/brand-profile.model';
import { BrandProfileApiService } from '../settings/services/brand-profile-api.service';
import { SocialAccountApiService } from '../social-accounts/services/social-account-api.service';
import { ScheduleDialogComponent } from '../publications/components/schedule-dialog.component';
import { LocalSchedule } from '../publications/models/publication.model';
import { formatInZone } from '../publications/models/time-zones';
import { PublicationApiService } from '../publications/services/publication-api.service';

const EXAMPLE = 'Create launch posts for our new AI reporting dashboard. Focus on helping small businesses save reporting time and encourage people to book a demo.';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;
/** Matches the server's limit for reference images analysed in one generation. */
const MAX_IMAGES = 4;

const STAGES = {
  ANALYZING_IMAGES: 'Analyzing images…',
  WRITING: 'Writing posts…',
  GENERATING_IMAGE: 'Generating image…',
} as const;

/** Quick additions to the extra instruction; clicking again removes them. */
const PRESETS = [
  { label: 'Product launch', icon: 'spark' },
  { label: 'Professional LinkedIn tone', icon: 'user' },
  { label: 'Customer pain point', icon: 'bulb' },
  { label: 'Engaging hook', icon: 'chat' },
  { label: 'Short promotional post', icon: 'doc' },
  { label: 'Thought leadership', icon: 'chart' },
] as const;

/** One-click ideas under the prompt box (they fill the prompt; nothing is sent until Generate). */
const SUGGESTIONS = [
  { icon: 'spark', text: 'Announce our latest product launch with a clear call to action' },
  { icon: 'bulb', text: 'Share a common customer pain point and how we solve it' },
  { icon: 'chart', text: 'Write a thought-leadership post about where our industry is heading' },
] as const;

/** Server timestamps may lack a zone suffix; they are UTC. */
const utcMs = (utc: string) => Date.parse(/(Z|[+-]\d{2}:\d{2})$/i.test(utc) ? utc : `${utc}Z`);

/** The prompt of a generation, kept in this tab so Back from Composer shows the bubble again (the API does not return it). */
function rememberAsked(requestId: string, text: string): void {
  try { sessionStorage.setItem(`ai-studio-asked:${requestId}`, text); } catch { /* bubble is cosmetic */ }
}
function recallAsked(requestId: string): string {
  try { return sessionStorage.getItem(`ai-studio-asked:${requestId}`) ?? ''; } catch { return ''; }
}

interface Result {
  post: Post;
  /** What is running for this result ('submit'). */
  busy: string | null;
  error: string | null;
  submitted: boolean;
  /** "Save draft" was clicked (the draft itself is already stored by the worker). */
  saved: boolean;
}

/** One earlier prompt of this chat with its finished answer; the newest turn lives in request/asked/results. */
interface Turn {
  asked: string;
  request: AiGenerationRequest;
  results: Result[];
}

/** The editable content of a version with some fields changed (everything else is kept as it is). */
function contentOf(v: PostVersion, change: Partial<PostContent>): PostContent {
  const { hook, body, callToAction, hashtags, destinationUrl, socialAccountId } = v;
  const images = v.images.map(({ mediaAssetId, altText }) => ({ mediaAssetId, altText }));
  return { hook, body, callToAction, hashtags, destinationUrl, images, socialAccountId, ...change };
}

/**
 * AI Studio: a prompt becomes one draft post per platform, written with the saved brand profile. Each draft can be sent
 * to review here or opened in Composer. Nothing is approved, scheduled or published from this page.
 */
@Component({
  selector: 'app-ai-studio-page',
  imports: [FormsModule, RouterLink, NgTemplateOutlet, ProblemBannerComponent, ScheduleDialogComponent],
  // Close open popovers when clicking anywhere else; Escape closes the preview.
  host: { '(document:click)': 'closeMenu($event)', '(document:keydown.escape)': 'preview.set(null)' },
  templateUrl: './ai-studio.page.html',
  styleUrl: './ai-studio.page.css',
  // A standalone design: Shadow DOM keeps the app's global element and class styles (e.g. Composer's .composer) out.
  encapsulation: ViewEncapsulation.ShadowDom,
})
export class AiStudioPage {
  private readonly ai = inject(AiApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly media = inject(MediaApiService);
  private readonly brandApi = inject(BrandProfileApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly publications = inject(PublicationApiService);
  private readonly dialog = viewChild.required(ScheduleDialogComponent);
  /** The approved post the open schedule dialog is for. */
  private scheduling: Post | null = null;
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly workspace = inject(WorkspaceService);
  private destroyed = false;

  /** Query parameter: the chat's generations, comma-separated, oldest first (shown again after Back from Composer). */
  readonly requestId = input<string>(undefined, { alias: 'request' });
  /** Query parameter: an idea typed on the Creator home, used as the prompt. */
  readonly initialPrompt = input<string>(undefined, { alias: 'prompt' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly tones = BRAND_TONES;
  protected readonly example = EXAMPLE;
  protected readonly presets = PRESETS;
  protected readonly suggestions = SUGGESTIONS;
  /** Small popovers in the prompt box: the + menu and the output-type menu. */
  protected readonly plusOpen = signal(false);
  protected readonly platOpen = signal(false);
  /** "All platforms": one post written to fit LinkedIn, Facebook and Instagram alike. */
  protected readonly allPlatforms = signal(false);
  /** The running generation was "All platforms": it ends in one shared draft, so loading shows one row. */
  protected readonly unifiedRun = signal(false);
  /** Earlier prompts of this chat, oldest first; each new prompt is answered below them, like a chat. */
  protected readonly history = signal<Turn[]>([]);
  /** No drafts or generation yet: the centred hero layout. */
  protected readonly hero = computed(() => !this.request() && !this.results().length && !this.history().length);
  protected readonly brand = signal<BrandProfile | null>(null);
  protected readonly brandLoaded = signal(false);
  /** What the platform offers (/internal/ai); model lists are the Hugging Face models a user may pick. */
  protected readonly caps = signal<AiCapabilities>({ textGeneration: false, imageGeneration: false, imageAnalysis: false, imageModels: [], visionModels: [] });
  protected readonly selected = signal<SocialPlatform[]>(['LinkedIn']);
  protected readonly withImage = signal(false);
  /** Reference images (up to 4): read by the vision model; with Text + Image all of them are attached to the drafts. */
  protected readonly images = signal<MediaAsset[]>([]);
  protected readonly maxImages = MAX_IMAGES;
  protected readonly visionModel = signal('');
  protected readonly imageModel = signal('');
  /** Text + Image without uploads: how many images the AI creates (1-4). */
  protected readonly imageCount = signal(1);
  protected readonly imageCounts = [1, 2, 3, 4];
  /** Text + Image needs an image source: an uploaded image, or image generation being available. */
  protected readonly imageOptionAvailable = computed(() => this.images().length > 0 || this.caps().imageGeneration);
  protected readonly stageText = computed(() => {
    const r = this.request();
    if (!r || r.status === 'PENDING' || !r.stage) return 'Understanding your request…';
    return STAGES[r.stage];
  });
  protected readonly uploading = signal(false);
  protected readonly promptSaved = signal(false);
  protected readonly brandOpen = signal(false);
  protected readonly advOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly request = signal<AiGenerationRequest | null>(null);
  /** The prompt behind the shown generation (the chat bubble); the prompt box is cleared for the next one. */
  protected readonly asked = signal('');
  /** "1m 7s" from queued to completed. */
  protected readonly workedFor = computed(() => {
    const r = this.request();
    if (!r?.completedAtUtc) return '';
    const s = Math.max(1, Math.round((utcMs(r.completedAtUtc) - utcMs(r.createdAtUtc)) / 1000));
    return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`;
  });
  protected readonly results = signal<Result[]>([]);
  protected readonly generating = computed(() => this.request()?.status === 'PENDING' || this.request()?.status === 'PROCESSING');
  protected readonly activeChips = signal<string[]>([]);
  /** The result shown in the Post Preview modal. */
  protected readonly preview = signal<Result | null>(null);
  protected prompt = '';
  protected extra = '';
  protected tone: BrandTone | '' = '';
  protected length: ContentLength = 'MEDIUM';
  protected hashtags: HashtagMode = 'AUTO';
  protected emojis: EmojiMode = 'AUTO';

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
    effect(() => {
      const orgId = this.workspace.orgId();
      if (!orgId) return;
      this.prompt = untracked(this.initialPrompt) || this.readSavedPrompt(orgId);
      void firstValueFrom(this.brandApi.get(orgId)).then((b) => this.brand.set(b), () => this.brand.set(null)).finally(() => this.brandLoaded.set(true));
      void firstValueFrom(this.ai.capabilities(orgId)).then((c) => {
        this.caps.set(c);
        this.visionModel.set(c.visionModels[0] ?? '');
        this.imageModel.set(c.imageModels[0] ?? '');
      }, () => undefined);
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

  /** Regenerate / Try again: the same prompt as the bubble, with the current options. */
  protected canRetry(): boolean {
    return !this.busy() && !this.generating() && !this.uploading() && !!this.asked() && !!this.selected().length && !!this.brand();
  }

  protected retry(): void {
    void this.generate(this.asked());
  }

  /** Short display name of a Hugging Face model id. */
  protected modelName(id: string): string {
    return id.split('/').pop() ?? id;
  }

  protected removeImage(id: string): void {
    this.images.update((list) => list.filter((a) => a.id !== id));
    if (!this.imageOptionAvailable()) this.withImage.set(false);
  }

  protected settingsHint(): string {
    return [this.tone || 'Use Brand Tone', this.length.charAt(0) + this.length.slice(1).toLowerCase(),
      this.hashtags === 'AUTO' ? 'Auto hashtags' : 'No hashtags', this.emojis === 'AUTO' ? 'Auto emojis' : 'No emojis'].join(' · ');
  }

  /** All platforms selects every platform for one shared post; picking a single platform leaves that mode. */
  protected chooseAll(): void {
    this.allPlatforms.set(true);
    this.selected.set([...SOCIAL_PLATFORMS]);
    this.platOpen.set(false);
  }

  /** At least one platform stays selected. */
  protected toggle(platform: SocialPlatform): void {
    if (this.allPlatforms()) {
      this.allPlatforms.set(false);
      return this.selected.set([platform]);
    }
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

  /** An "All platforms" answer: several platforms asked, one shared draft made. */
  protected shared(request: AiGenerationRequest | null): boolean {
    return !!request && request.platforms.length > 1 && request.outputs.length === 1;
  }

  protected text(post: Post): string {
    return postText(post.currentVersion);
  }

  /** The post text without hashtags (shown separately, in blue). */
  protected body(v: PostVersion): string {
    return postText({ ...v, hashtags: [] });
  }

  /** Card status: Draft until submitted, then In review, then Ready once approved. */
  protected chip(r: Result): { label: string; tone: 'draft' | 'review' | 'ready' } {
    const state = r.post.editorialState;
    if (state === 'APPROVED') return { label: 'Ready', tone: 'ready' };
    if (state === 'IN_REVIEW' || r.submitted) return { label: 'In review', tone: 'review' };
    return { label: state === 'CHANGES_REQUESTED' ? 'Changes' : 'Draft', tone: 'draft' };
  }

  protected closeMenu(event: Event): void {
    // Inside Shadow DOM a document listener sees the host as the target; the composed path has the real element.
    const inside = (cls: string) => event.composedPath().some((n) => n instanceof Element && n.classList.contains(cls));
    if (!inside('pop-wrap')) {
      this.plusOpen.set(false);
      this.platOpen.set(false);
    }
  }

  /** The post text (hook, body, call to action) as paragraphs; hashtags are shown separately. */
  protected paragraphs(v: PostVersion): string[] {
    return this.body(v).split(/\n{2,}/).map((p) => p.trim()).filter(Boolean);
  }

  /** Enter generates; Shift+Enter adds a new line. */
  protected onEnter(event: KeyboardEvent): void {
    if (event.shiftKey) return;
    event.preventDefault();
    void this.generate();
  }

  protected useSuggestion(text: string): void {
    this.prompt = text;
  }

  protected setOutput(withImage: boolean): void {
    this.withImage.set(withImage);
  }

  protected ago(utc: string): string {
    const minutes = Math.round((Date.now() - utcMs(utc)) / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.round(minutes / 60);
    return hours < 24 ? `${hours}h ago` : `${Math.round(hours / 24)}d ago`;
  }

  protected previewUrl(mediaAssetId: string): string {
    return this.media.previewUrl(this.workspace.orgId()!, mediaAssetId);
  }

  /** A useful failure message: configuration and key problems in plain words, otherwise the provider's reason. */
  protected failureText(request: AiGenerationRequest | null = this.request()): string {
    const r = request;
    const code = r?.failureCode ?? '';
    if (code === 'AI_NOT_CONFIGURED') return 'AI is not available right now. Please contact your administrator.';
    if (code === 'AI_PROVIDER_AUTH') return 'AI is not available right now. Please contact your administrator.';
    if ((code === 'AI_PROVIDER_REJECTED' || code === 'AI_PROVIDER_UNAVAILABLE') && this.images().length)
      return 'The vision model could not read the images. Try another vision model in Advanced Options, or try again later.';
    return r?.failureMessage ? `The posts could not be generated: ${r.failureMessage}` : 'The posts could not be generated. Please try again.';
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
    const files = Array.from(input.files ?? []);
    input.value = '';
    const orgId = this.workspace.orgId();
    if (!files.length || !orgId) return;
    // Early hints only; the server inspects the bytes and applies the authoritative rules.
    if (files.length > MAX_IMAGES - this.images().length) return this.toast.error(`Up to ${MAX_IMAGES} images can be used.`);
    if (files.some((f) => !['image/jpeg', 'image/png'].includes(f.type))) return this.toast.error('Choose JPEG or PNG images.');
    if (files.some((f) => f.size > MAX_IMAGE_BYTES)) return this.toast.error('Images must be at most 8 MB.');
    this.uploading.set(true);
    try {
      for (const file of files) {
        const asset = await firstValueFrom(this.media.upload(orgId, file));
        this.images.update((list) => [...list, asset]);
      }
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.uploading.set(false);
    }
  }

  protected async generate(text = this.fullPrompt()): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId || !text || this.busy() || this.generating() || this.uploading() || !this.selected().length || !this.brand()) return;
    this.busy.set(true);
    this.problem.set(null);
    const imageIds = this.images().map((a) => a.id);
    const withImage = this.withImage() && this.imageOptionAvailable();
    try {
      const queued = await firstValueFrom(this.ai.requestGeneration(orgId, {
        prompt: text,
        platforms: SOCIAL_PLATFORMS.filter((p) => this.selected().includes(p)),
        tone: this.tone || null,
        length: this.length,
        hashtags: this.hashtags,
        emojis: this.emojis,
        mediaAssetIds: imageIds,
        visionModel: imageIds.length ? this.visionModel() || null : null,
        withImage,
        imageModel: withImage && !imageIds.length && this.caps().imageModels.length ? this.imageModel() || null : null,
        imageCount: withImage && !imageIds.length ? this.imageCount() : 1,
        unified: this.allPlatforms(),
      }));
      // The previous answer stays on screen above the new one.
      const previous = this.request();
      if (previous) this.history.update((turns) => [...turns, { asked: this.asked(), request: previous, results: this.results() }]);
      this.results.set([]);
      this.unifiedRun.set(this.allPlatforms());
      this.request.set(queued);
      this.asked.set(text);
      rememberAsked(queued.id, text);
      if (text === this.fullPrompt()) this.prompt = '';
      // Keeps the whole chat reachable with Back after opening a draft in Composer.
      const ids = [...this.history().map((t) => t.request.id), queued.id].join(',');
      void this.router.navigate([], { queryParams: { request: ids }, queryParamsHandling: 'merge', replaceUrl: true });
      this.scrollToLatest();
      await this.follow(orgId, queued);
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
  protected async submit(result: Result, quiet = false): Promise<ReviewSummary | null> {
    const orgId = this.workspace.orgId();
    if (!orgId) return null;
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
      // The Admin's one-click approve (quiet) does not show the intermediate "In review" state.
      if (!quiet) {
        this.patch(post.id, { post: await firstValueFrom(this.postsApi.get(orgId, post.id)), submitted: true });
        this.toast.success('Submitted for review.');
      }
      return review;
    } catch (error) {
      const problem = error instanceof Error ? null : toApiProblem(error);
      const message = problem?.code === 'SUBMISSION_BLOCKED' ? `${problem.detail} ${BLOCKED_HINT}` : problem?.detail ?? (error as Error).message;
      this.patch(result.post.id, { error: message });
      return null;
    } finally {
      this.patch(result.post.id, { busy: null });
    }
  }

  /** AI drafts are stored as soon as they are generated; this confirms it and keeps them in Drafts. */
  protected saveDraft(result: Result): void {
    this.patch(result.post.id, { saved: true });
    this.toast.success(`${result.post.platform} draft saved in Drafts`);
  }

  /**
   * Admin, one action: a draft is submitted and approved by the Admin (the server still records a human approval of this
   * exact version, or refuses and the review page shows why), then the schedule dialog opens right here. The job is
   * created through the normal publication pipeline; nothing is published from the browser.
   */
  protected async schedule(result: Result): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    const state = result.post.editorialState;
    if (state === 'IN_REVIEW' || result.submitted) return void this.router.navigate(['/approvals']);
    if (state !== 'APPROVED') {
      const review = await this.submit(result, true);
      if (!review) return;
      this.patch(result.post.id, { busy: 'submit' });
      try {
        await firstValueFrom(this.reviewsApi.approve(orgId, review));
      } catch (error) {
        this.toast.error(toApiProblem(error).detail);
        return void this.router.navigate(['/approvals', review.id]);
      } finally {
        this.patch(result.post.id, { busy: null });
      }
    }
    try {
      const post = await firstValueFrom(this.postsApi.get(orgId, result.post.id));
      this.patch(post.id, { post });
      this.scheduling = post;
      this.dialog().open('Schedule post', 'Schedule', this.workspace.timeZone());
    } catch (error) {
      this.toast.error(toApiProblem(error).detail);
    }
  }

  /** Called by the schedule dialog; returns a problem to show there, or null when the job was saved. */
  protected readonly submitSchedule = async (when: LocalSchedule): Promise<ApiProblem | null> => {
    const orgId = this.workspace.orgId();
    const post = this.scheduling;
    if (!orgId || !post) return null;
    try {
      const saved = await firstValueFrom(this.publications.schedule(orgId, post.id, post.currentVersionId, when, post.rowVersion));
      this.toast.success(`Scheduled for ${formatInZone(saved.publishAtUtc, this.workspace.timeZone())}.`);
      this.patch(post.id, { post: await firstValueFrom(this.postsApi.get(orgId, post.id)) });
      return null;
    } catch (error) {
      return toApiProblem(error);
    }
  };

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

  /** Rebuilds the chat from the query parameter: earlier turns first, then the latest one (which may still be running). */
  private async restore(orgId: string, requestIds: string): Promise<void> {
    const ids = requestIds.split(',').filter(Boolean);
    const earlier: Turn[] = [];
    for (const id of ids.slice(0, -1)) {
      try {
        const request = await firstValueFrom(this.ai.get(orgId, id));
        if (request.kind === 'Generation') earlier.push({ asked: recallAsked(request.id), request, results: await this.loadResults(orgId, request) });
      } catch {
        // An old or foreign id: leave that turn out.
      }
    }
    this.history.set(earlier);
    try {
      const request = await firstValueFrom(this.ai.get(orgId, ids[ids.length - 1]));
      if (request.kind !== 'Generation') return;
      this.request.set(request);
      this.asked.set(recallAsked(request.id));
      await this.follow(orgId, request);
    } catch {
      // An old or foreign link: just show an empty studio.
      this.request.set(null);
    }
  }

  private async loadResults(orgId: string, request: AiGenerationRequest): Promise<Result[]> {
    if (request.status !== 'COMPLETED') return [];
    const posts = await Promise.all(request.outputs.map((o) => firstValueFrom(this.postsApi.get(orgId, o.postId))));
    return posts.map((post) => ({ post, busy: null, error: null, submitted: false, saved: false }));
  }

  /** Brings the newest prompt into view, as a chat does. */
  private scrollToLatest(): void {
    setTimeout(() => (this.host.nativeElement.shadowRoot ?? this.host.nativeElement)
      .querySelector('.turn-latest')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  /**
   * Waits for the drafts and shows them. The worker does everything (vision, text, image generation or attaching the
   * uploaded image), so the drafts arrive complete and the browser may close meanwhile.
   */
  private async follow(orgId: string, queued: AiGenerationRequest): Promise<void> {
    const final = await this.ai.waitForCompletion(orgId, queued, (r) => this.request.set(r), () => this.destroyed);
    this.request.set(final);
    this.results.set(await this.loadResults(orgId, final));
  }

  /** Updates a result wherever it is shown: the latest answer or an earlier turn. */
  private patch(postId: string, change: Partial<Result>): void {
    const apply = (all: Result[]) => all.map((r) => (r.post.id === postId ? { ...r, ...change } : r));
    this.results.update(apply);
    this.history.update((turns) => turns.map((t) => (t.results.some((r) => r.post.id === postId) ? { ...t, results: apply(t.results) } : t)));
  }
}
