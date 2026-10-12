import { Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { ToastService } from '../../core/services/toast.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { DateTimePickerComponent } from '../../shared/components/date-time-picker.component';
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
import { formatInZone } from '../publications/models/time-zones';
import { PublicationApiService } from '../publications/services/publication-api.service';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;
/** Matches the server's limit for reference images analysed in one generation. */
const MAX_IMAGES = 4;

const STAGES = {
  ANALYZING_IMAGES: 'Analyzing images…',
  WRITING: 'Writing posts…',
  GENERATING_IMAGE: 'Generating image…',
} as const;

/** One-click ideas under the prompt box (they fill the prompt; nothing is sent until the arrow is clicked). */
const SUGGESTIONS = [
  { icon: 'wand', label: 'Introduce a new product', prompt: 'Announce our latest product launch with a clear call to action', bg: '#E4F1FF', fg: '#538BC9' },
  { icon: 'message', label: 'Solve a customer challenge', prompt: 'Share a customer challenge and how our team helps solve it', bg: '#EEE9FE', fg: '#8879C4' },
  { icon: 'spark', label: 'Share an industry insight', prompt: 'Write an insightful social post about the future of our industry', bg: '#FDEBE9', fg: '#CE8697' },
] as const;

/** The design's length names for the API's lengths. */
const LENGTHS: { value: ContentLength; label: string }[] = [{ value: 'SHORT', label: 'Short' }, { value: 'MEDIUM', label: 'Balanced' }, { value: 'LONG', label: 'Detailed' }];

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
  imports: [FormsModule, RouterLink, NgTemplateOutlet, ProblemBannerComponent, DateTimePickerComponent],
  // Close open popovers when clicking anywhere else; Escape closes menus and dialogs.
  host: { '(document:click)': 'closeMenu($event)', '(document:keydown.escape)': 'closeAll()' },
  templateUrl: './ai-studio.page.html',
  styleUrl: './ai-studio.page.css',
})
export class AiStudioPage {
  private readonly ai = inject(AiApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly media = inject(MediaApiService);
  private readonly brandApi = inject(BrandProfileApiService);
  private readonly accountsApi = inject(SocialAccountApiService);
  private readonly reviewsApi = inject(ReviewApiService);
  private readonly publications = inject(PublicationApiService);
  /** The approved post the open "Plan your post" dialog is for. */
  protected readonly scheduling = signal<Post | null>(null);
  /** The dialog's datetime-local value, in the workspace time zone. */
  protected scheduleAt = '';
  protected readonly scheduleProblem = signal<ApiProblem | null>(null);
  protected readonly scheduleBusy = signal(false);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly workspace = inject(WorkspaceService);
  private destroyed = false;

  /** Query parameter: the chat's generations, comma-separated, oldest first (shown again after Back from Composer). */
  readonly requestId = input<string>(undefined, { alias: 'request' });
  /** Query parameter: an idea typed on the Creator home, used as the prompt. */
  readonly initialPrompt = input<string>(undefined, { alias: 'prompt' });
  /** Query parameters from the Admin home "Let's make a post" dialog: the platform and tone picked there. */
  readonly initialPlatform = input<string>(undefined, { alias: 'platform' });
  readonly initialTone = input<string>(undefined, { alias: 'tone' });

  protected readonly platforms = SOCIAL_PLATFORMS;
  protected readonly tones = BRAND_TONES;
  protected readonly lengths = LENGTHS;
  protected readonly suggestions = SUGGESTIONS;
  /** Small popovers in the prompt box: the + menu and the platform menu. */
  protected readonly plusOpen = signal(false);
  protected readonly platOpen = signal(false);
  /**
   * "All platforms" mode: one shared post for every platform (Composer then copies it to the platforms picked there).
   * Its own choice, not "every platform ticked": ticking all three gives a separate version for each.
   */
  protected readonly allPlatforms = signal(false);
  /** The running generation was "All platforms": it ends in one shared draft, so loading shows one card. */
  protected readonly unifiedRun = signal(false);
  /** Captions expanded with "Show more". */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());
  /** "Brand tone · Balanced" per generation, for the cards (the API does not return it, so only for this visit). */
  private readonly settingsLabels = new Map<string, string>();
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
  /** The "Fine-tune your post" tray. */
  protected readonly settingsOpen = signal(false);
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
  /** Drafts shown in this chat (every turn). */
  protected readonly draftCount = computed(() => this.results().length + this.history().reduce((n, t) => n + t.results.length, 0));
  protected readonly generating = computed(() => this.request()?.status === 'PENDING' || this.request()?.status === 'PROCESSING');
  /** The result shown in the Post Preview modal. */
  protected readonly preview = signal<Result | null>(null);
  protected prompt = '';
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
      const platform = SOCIAL_PLATFORMS.find((p) => p === untracked(this.initialPlatform));
      if (platform) this.selected.set([platform]);
      const tone = BRAND_TONES.find((t) => t === untracked(this.initialTone));
      if (tone) this.tone = tone;
      void firstValueFrom(this.brandApi.get(orgId)).then((b) => this.brand.set(b), () => this.brand.set(null)).finally(() => this.brandLoaded.set(true));
      void firstValueFrom(this.ai.capabilities(orgId)).then((c) => {
        this.caps.set(c);
        // Image-first: an image post is the default whenever images can be made.
        if (c.imageGeneration) this.withImage.set(true);
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

  /** Regenerate a turn: the same prompt, written again with the current options. */
  protected retry(text = this.asked()): void {
    if (text) void this.generate(text);
  }

  /** New session: an empty studio. The drafts made so far stay saved in Drafts. */
  protected reset(): void {
    if (this.busy() || this.generating()) return;
    this.history.set([]);
    this.request.set(null);
    this.results.set([]);
    this.asked.set('');
    this.prompt = '';
    this.images.set([]);
    this.closeAll();
    void this.router.navigate([], { queryParams: { request: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected closeAll(): void {
    this.plusOpen.set(false);
    this.platOpen.set(false);
    this.preview.set(null);
    this.scheduling.set(null);
  }

  protected toggleExpanded(postId: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (!next.delete(postId)) next.add(postId);
      return next;
    });
  }

  protected settingsLabel(request: AiGenerationRequest | null): string {
    return (request && this.settingsLabels.get(request.id)) ?? '';
  }

  /** The prompt box grows with its text, up to the design's limit. */
  protected autosize(area: HTMLTextAreaElement): void {
    // Grows with the text up to the CSS max-height, then scrolls; min-height keeps the empty size.
    area.style.height = 'auto';
    area.style.height = `${area.scrollHeight}px`;
  }

  /** Short display name of a Hugging Face model id. */
  protected modelName(id: string): string {
    return id.split('/').pop() ?? id;
  }

  protected removeImage(id: string): void {
    this.images.update((list) => list.filter((a) => a.id !== id));
    if (!this.imageOptionAvailable()) this.withImage.set(false);
  }

  private settingsHint(): string {
    return `${this.tone || 'Brand tone'} · ${LENGTHS.find((l) => l.value === this.length)!.label}`;
  }

  /**
   * One or more platforms (a separate version, and card, for each; all three ticked is still three versions). "All
   * platforms" is the shared-post mode and closes the menu; a platform toggles in and out (the menu stays open), never
   * leaving none; from "All platforms", a platform picks just that one.
   */
  protected choosePlatform(platform: SocialPlatform | 'All'): void {
    if (platform === 'All') {
      this.allPlatforms.set(true);
      this.selected.set([...SOCIAL_PLATFORMS]);
      this.platOpen.set(false);
      return;
    }
    const current = this.selected();
    if (this.allPlatforms()) {
      this.allPlatforms.set(false);
      this.selected.set([platform]);
    }
    else if (!current.includes(platform)) this.selected.set(SOCIAL_PLATFORMS.filter((p) => p === platform || current.includes(p)));
    else if (current.length > 1) this.selected.set(current.filter((p) => p !== platform));
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
    if (!(event.target as Element).closest?.('#composer')) {
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
    setTimeout(() => {
      const area = this.host.nativeElement.querySelector('#postPrompt') as HTMLTextAreaElement | null;
      if (!area) return;
      this.autosize(area);
      area.focus();
    });
  }

  /** Pasted prompts often carry Markdown **bold**: odd parts are the bold runs (shown as text, never as HTML). */
  protected boldParts(text: string): string[] {
    return text.split(/\*\*(.+?)\*\*/s);
  }

  /** "01" style version numbers, as in the design. */
  protected pad(n: number): string {
    return String(n).padStart(2, '0');
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
        // "All platforms": one shared draft; Composer sends a copy to each platform picked. Two platforms: a version each.
        unified: this.allPlatforms(),
      }));
      // The previous answer stays on screen above the new one.
      const previous = this.request();
      if (previous) this.history.update((turns) => [...turns, { asked: this.asked(), request: previous, results: this.results() }]);
      this.results.set([]);
      this.settingsLabels.set(queued.id, this.settingsHint());
      this.request.set(queued);
      this.unifiedRun.set(this.allPlatforms());
      this.asked.set(text);
      rememberAsked(queued.id, text);
      if (text === this.fullPrompt()) {
        this.prompt = '';
        document.getElementById('postPrompt')?.style.removeProperty('height'); // back to the empty size
      }
      this.images.set([]);
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
      // Tomorrow, on the hour, in the workspace time zone (the dialog's starting value).
      const parts = new Intl.DateTimeFormat('en-CA', { timeZone: this.workspace.timeZone(), year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', hourCycle: 'h23' })
        .formatToParts(Date.now() + 864e5);
      const part = (type: string) => parts.find((p) => p.type === type)?.value ?? '00';
      this.scheduleAt = `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:00`;
      this.scheduleProblem.set(null);
      this.scheduling.set(post);
    } catch (error) {
      this.toast.error(toApiProblem(error).detail);
    }
  }

  /**
   * "Plan your post": the local date and time in the workspace time zone go to the server, which resolves the UTC instant
   * and validates it; the job is created through the normal publication pipeline.
   */
  protected async saveSchedule(): Promise<void> {
    const orgId = this.workspace.orgId();
    const post = this.scheduling();
    if (!orgId || !post || !this.scheduleAt) return;
    const [localDate, time] = this.scheduleAt.split('T');
    this.scheduleBusy.set(true);
    this.scheduleProblem.set(null);
    try {
      const saved = await firstValueFrom(this.publications.schedule(orgId, post.id, post.currentVersionId,
        { localDate, localTime: `${time}:00`, timeZone: this.workspace.timeZone(), utcOffsetMinutes: null }, post.rowVersion));
      this.toast.success(`Scheduled for ${formatInZone(saved.publishAtUtc, this.workspace.timeZone())}.`);
      this.patch(post.id, { post: await firstValueFrom(this.postsApi.get(orgId, post.id)) });
      this.scheduling.set(null);
    } catch (error) {
      this.scheduleProblem.set(toApiProblem(error));
    } finally {
      this.scheduleBusy.set(false);
    }
  }

  private fullPrompt(): string {
    return this.prompt.trim();
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
    setTimeout(() => this.host.nativeElement.querySelector('.turn-latest')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
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
