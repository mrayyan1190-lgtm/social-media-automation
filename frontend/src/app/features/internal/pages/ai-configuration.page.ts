import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { AiConfiguration, HEALTH_LABELS, InternalApiService, tone, when } from '../internal-api.service';
import { NoticeComponent } from '../../../shared/components/notice.component';
import { ThemeSelectComponent } from '../../../shared/components/theme-select.component';

/**
 * The system LLM used by every workspace. The API key is write-only: after saving, the page only shows that a key is
 * configured and when it was replaced. The browser never receives the stored key.
 */
@Component({
  selector: 'app-internal-ai-page',
  imports: [ThemeSelectComponent, NoticeComponent, FormsModule, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>AI / LLM</h1><p class="muted">The hosted model all workspaces use for generation, rewrites and evaluation.</p></div></div>
    <app-problem-banner [problem]="problem()" />
    <app-notice [message]="notice()" />

    @if (c(); as cfg) {
      @if (cfg.source === 'ENVIRONMENT') {
        <p class="banner info">Currently using the deployment's environment configuration. Saving here stores platform settings, which take precedence.</p>
      }
      @let img = cfg.image;
      @let vis = cfg.vision;
      <div class="ai-grid">
        <form class="card form-grid" (ngSubmit)="save()">
          <div class="card-head"><h2><span class="head-ic"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="4.5" y="4.5" width="15" height="15" rx="4"/><path d="M9 1.5v3M15 1.5v3M9 19.5v3M15 19.5v3M1.5 9h3M1.5 15h3M19.5 9h3M19.5 15h3M9 13l3-5 3 5-3 3-3-3Z"/></svg></span>Text Generation</h2><span [class]="'badge delivery ' + tone(cfg.status.health)">{{ labels[cfg.status.health] }}</span></div>
          <label>Provider *
            <app-theme-select name="provider" [(ngModel)]="provider" required>
              <option value="OpenAI">OpenAI</option><option value="Groq">Groq</option><option value="HuggingFace">Hugging Face</option>
              <option value="OpenAICompatible">OpenAI-compatible endpoint</option>
            </app-theme-select>
          </label>
          @if (provider === 'OpenAICompatible') {
            <label>Base URL * <input name="baseUrl" type="url" [(ngModel)]="baseUrl" placeholder="https://…/v1" /></label>
          }

          <div class="secret-field">
            <span class="field-label">{{ provider === 'OpenAI' ? 'OpenAI ' : provider === 'HuggingFace' ? 'Hugging Face ' : '' }}API Key *</span>
            @if (cfg.apiKeyConfigured && cfg.source === 'PLATFORM' && !replacingKey()) {
              <div class="secret-set">
                <span class="badge delivery success">Configured ✓</span>
                <span class="hint">Last updated: {{ when(cfg.apiKeyUpdatedAtUtc) }}</span>
                <button type="button" (click)="replacingKey.set(true)">Replace API Key</button>
              </div>
            } @else {
              <input name="apiKey" type="password" autocomplete="new-password" [(ngModel)]="apiKey" placeholder="Paste the API key" spellcheck="false" />
              <span class="hint">Stored encrypted in the secret store. It is never shown again.</span>
              @if (cfg.source === 'ENVIRONMENT') { <span class="hint">An environment key is in use now; platform settings need their own key.</span> }
              @if (cfg.apiKeyConfigured && cfg.source === 'PLATFORM') { <button type="button" class="link" (click)="cancelKey()">Keep the current key</button> }
            }
          </div>

          <label>Default Model * <input name="model" [(ngModel)]="model" required placeholder="e.g. gpt-4o-mini" /></label>
          <label>Fallback Model <span class="hint">optional · used once if the provider refuses the default model</span>
            <input name="fallbackModel" [(ngModel)]="fallbackModel" />
          </label>
          <div class="two-inputs">
            <label>Max Output Tokens <input name="maxTokens" type="number" min="256" max="32000" [(ngModel)]="maxOutputTokens" placeholder="default" /></label>
            <label>Request Timeout Seconds <input name="timeout" type="number" min="5" max="600" [(ngModel)]="timeoutSeconds" placeholder="default" /></label>
          </div>
          <label class="checkbox"><input name="enabled" type="checkbox" [(ngModel)]="enabled" /> AI Enabled</label>

          <div>
            <div class="statline"><span>Configuration</span><span [class]="'badge delivery ' + tone(cfg.status.configured)">{{ labels[cfg.status.configured] }}</span></div>
          </div>
          @if (test(); as t) {
            <app-notice [message]="t.message" [tone]="t.succeeded && t.modelAvailable ? 'success' : 'error'" />
          }
          <div class="actions">
            <button type="button" (click)="testConnection()" [disabled]="busy()">{{ busy() === 'test' ? 'Testing…' : 'Test Connection' }}</button>
            <button type="submit" class="primary" [disabled]="busy() || !model.trim()">{{ busy() === 'save' ? 'Saving…' : 'Save Configuration' }}</button>
          </div>
        </form>

        <form class="card form-grid" (ngSubmit)="saveImage()">
          <div class="card-head"><h2><span class="head-ic purple"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m12 2 2.4 7.6L22 12l-7.6 2.4L12 22l-2.4-7.6L2 12l7.6-2.4L12 2Z"/></svg></span>Image Generation</h2><span [class]="'badge delivery ' + tone(img.status.health)">{{ labels[img.status.health] }}</span></div>
          <p class="hint">Creates images for posts. Each image becomes a new post version and is reviewed by a person before it can be scheduled.</p>
          <label>Provider *
            <app-theme-select name="imgProvider" [ngModel]="imgProvider" (ngModelChange)="chooseImgProvider($event)">
              <option value="HuggingFace">Hugging Face</option><option value="StabilityAI">Stability AI</option><option value="CloudflareWorkersAI">Cloudflare Workers AI</option><option value="OpenAI">OpenAI</option><option value="OpenAICompatible">OpenAI-compatible endpoint</option>
            </app-theme-select>
          </label>
          @if (imgProvider === 'OpenAICompatible') {
            <label>Base URL * <input name="imgBaseUrl" type="url" [(ngModel)]="imgBaseUrl" placeholder="https://…/v1" /></label>
          }
          @if (imgProvider === 'CloudflareWorkersAI') {
            <label>Account ID * <input name="imgAccountId" [(ngModel)]="imgBaseUrl" maxlength="32" spellcheck="false" placeholder="32-character Cloudflare Account ID" />
              <span class="hint">Cloudflare dashboard → Workers AI → REST API. Not secret; the API token goes in the key field below.</span>
            </label>
          }
          <div class="two-inputs">
            @if (imgProvider === 'HuggingFace') {
              <label>Image Model *
                <app-theme-select name="imgModel" [(ngModel)]="imgModel">
                  @for (m of hfModels; track m.id) { <option [value]="m.id">{{ m.id }}</option> }
                  @if (!isHfModel(imgModel)) { <option [value]="imgModel">{{ imgModel }} (custom)</option> }
                </app-theme-select>
                <span class="hint">{{ hfHint() }}</span>
              </label>
            } @else if (imgProvider === 'CloudflareWorkersAI') {
              <label>Image Model *
                <app-theme-select name="imgModel" [(ngModel)]="imgModel">
                  @for (m of cloudflareModels; track m.id) { <option [value]="m.id">{{ m.label }}</option> }
                </app-theme-select>
                <span class="hint">Returns a 1024x1024 JPEG, accepted by every platform. The size setting does not apply.</span>
              </label>
            } @else if (imgProvider === 'StabilityAI') {
              <label>Image Model *
                <app-theme-select name="imgModel" [(ngModel)]="imgModel">
                  @for (m of stabilityModels; track m.id) { <option [value]="m.id">{{ m.label }}</option> }
                </app-theme-select>
                <span class="hint">Returns JPEG at about 1.5 megapixels. Stable Image Core costs 3 credits per image.</span>
              </label>
            } @else {
              <label>Image Model * <input name="imgModel" [(ngModel)]="imgModel" placeholder="gpt-image-1" />
                <span class="hint">GPT image models return JPEG, which Instagram requires; DALL·E returns PNG (LinkedIn and Facebook only).</span>
              </label>
            }
            <label>Image Size *
              <app-theme-select name="imgSize" [(ngModel)]="imgSize">
                @for (s of img.sizes; track s) { <option [value]="s">{{ s }}{{ s === '1024x1024' ? ' (square)' : ' (landscape)' }}</option> }
              </app-theme-select>
            </label>
          </div>

          <div class="secret-field">
            <span class="field-label">{{ imgProvider === 'HuggingFace' ? 'Hugging Face Token' : imgProvider === 'StabilityAI' ? 'Stability AI API Key' : imgProvider === 'CloudflareWorkersAI' ? 'Cloudflare API Token' : 'Image API Key' }}
              <span class="hint">{{ imgProvider === 'StabilityAI' || imgProvider === 'CloudflareWorkersAI' || (imgProvider === 'HuggingFace' && provider !== 'HuggingFace') ? 'required' : 'optional' }}</span></span>
            @if (img.ownApiKey && !replacingImgKey()) {
              <div class="secret-set">
                <span class="badge delivery success">Configured ✓</span>
                <span class="hint">Last updated: {{ when(img.apiKeyUpdatedAtUtc) }}</span>
                <button type="button" (click)="replacingImgKey.set(true)">Replace API Key</button>
              </div>
            } @else {
              <input name="imgKey" type="password" autocomplete="new-password" [(ngModel)]="imgKey" spellcheck="false"
                [placeholder]="imgProvider === 'HuggingFace' ? 'hf_… token with Inference Providers permission'
                  : imgProvider === 'StabilityAI' ? 'sk-… key from platform.stability.ai'
                  : imgProvider === 'CloudflareWorkersAI' ? 'Workers AI API token'
                  : cfg.apiKeyConfigured ? 'Leave empty to use the AI / LLM API key' : 'Paste the API key'" />
              <span class="hint">Stored encrypted in the secret store. It is never shown again.</span>
              @if (img.ownApiKey) { <button type="button" class="link" (click)="imgKey = ''; replacingImgKey.set(false)">Keep the current key</button> }
            }
          </div>
          <label class="checkbox"><input name="imgEnabled" type="checkbox" [(ngModel)]="imgEnabled" /> Image Generation Enabled</label>

          <div>
            <div class="statline"><span>Configuration</span><span [class]="'badge delivery ' + tone(img.status.configured)">{{ labels[img.status.configured] }}</span></div>
          </div>
          @if (imgTest(); as t) { <app-notice [message]="t.message" [tone]="t.succeeded && t.modelAvailable ? 'success' : 'error'" /> }
          <div class="actions">
            <button type="button" (click)="testImage()" [disabled]="busy()">{{ busy() === 'img-test' ? 'Testing…' : 'Test Connection' }}</button>
            <button type="submit" class="primary" [disabled]="busy() || !imgModel.trim()">{{ busy() === 'img-save' ? 'Saving…' : 'Save Image Settings' }}</button>
          </div>
        </form>

        <form class="card form-grid" (ngSubmit)="saveVision()">
          <div class="card-head"><h2><span class="head-ic peach"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/></svg></span>Image Understanding (Vision)</h2><span [class]="'badge delivery ' + (vis.usable ? 'success' : 'neutral')">{{ vis.usable ? 'Configured' : vis.enabled ? 'Not Configured' : 'Disabled' }}</span></div>
          <p class="hint">Reads images users upload in Create with AI and Composer, so posts can be written about them. Uses the Hugging Face token saved above; users can pick another of these models in Advanced Options.</p>
          <label>Default Vision Model *
            <app-theme-select name="visModel" [(ngModel)]="visModel">
              @for (m of vis.models; track m) { <option [value]="m">{{ m }}</option> }
            </app-theme-select>
            <span class="hint">{{ visionHints[visModel] || '' }}</span>
          </label>
          <div class="secret-field">
            <span class="field-label">Hugging Face Token <span class="hint">optional</span></span>
            @if (vis.tokenSource !== 'NONE' && !replacingVisKey()) {
              <div class="secret-set">
                <span class="badge delivery success">Configured ✓</span>
                <span class="hint">{{ vis.hasOwnApiKey ? 'Last updated: ' + when(vis.apiKeyUpdatedAtUtc) : 'Uses the AI / LLM key (Hugging Face)' }}</span>
                <button type="button" (click)="replacingVisKey.set(true)">{{ vis.hasOwnApiKey ? 'Replace API Key' : 'Use a Separate Key' }}</button>
              </div>
            } @else {
              <input name="visKey" type="password" autocomplete="new-password" [(ngModel)]="visKey" spellcheck="false"
                placeholder="hf_… token with Inference Providers permission" />
              <span class="hint">Used only for image understanding. Stored encrypted in the secret store. It is never shown again.</span>
              @if (vis.tokenSource !== 'NONE') { <button type="button" class="link" (click)="visKey = ''; replacingVisKey.set(false)">Keep the current key</button> }
            }
          </div>
          <label class="checkbox"><input name="visEnabled" type="checkbox" [(ngModel)]="visEnabled" /> Image Understanding Enabled</label>

          <div>
            <div class="statline"><span>Token</span><span>{{ vis.tokenSource === 'VISION_KEY' ? 'Separate vision key' : vis.tokenSource === 'LLM_KEY' ? 'AI / LLM key' : 'None' }}</span></div>
            <div class="statline"><span>Images per Request</span><strong>up to 4</strong></div>
          </div>
          @if (visTest(); as t) { <app-notice [message]="t.message" [tone]="t.succeeded ? 'success' : 'error'" /> }
          <div class="actions">
            <button type="button" (click)="testVision()" [disabled]="busy() || vis.tokenSource === 'NONE'" title="Save a new token first; the test uses the saved token">{{ busy() === 'vis-test' ? 'Testing…' : 'Test Connection' }}</button>
            <button type="submit" class="primary" [disabled]="busy()">{{ busy() === 'vis-save' ? 'Saving…' : 'Save Vision Settings' }}</button>
          </div>
        </form>

      </div>
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class AiConfigurationPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly c = signal<AiConfiguration | null>(null);
  protected readonly busy = signal<'save' | 'test' | 'img-save' | 'img-test' | 'vis-save' | 'vis-test' | null>(null);
  protected readonly visTest = signal<{ succeeded: boolean; message: string } | null>(null);
  protected visModel = '';
  protected visEnabled = true;
  protected readonly visionHints: Record<string, string> = {
    'meta-llama/Llama-4-Scout-17B-16E-Instruct': 'Default: fast and served by several Hugging Face providers.',
    'Qwen/Qwen3-VL-30B-A3B-Instruct': 'Fast (small active size), good at reading screenshots and charts.',
    'google/gemma-3-27b-it': 'Alternative if the others are busy.',
  };
  protected readonly imgTest = signal<{ succeeded: boolean; message: string; modelAvailable: boolean } | null>(null);
  protected readonly replacingImgKey = signal(false);
  protected readonly replacingVisKey = signal(false);
  protected visKey = '';
  protected imgProvider = 'OpenAI';
  protected imgBaseUrl = '';
  protected imgModel = 'gpt-image-1';
  protected imgSize = '1024x1024';
  protected imgKey = '';
  protected imgEnabled = false;
  /** Suggested Hugging Face text-to-image models (any HF model served for text-to-image can be typed instead). */
  protected readonly stabilityModels = [
    { id: 'core', label: 'Stable Image Core (fast, 3 credits)' },
    { id: 'ultra', label: 'Stable Image Ultra (highest quality)' },
    { id: 'sd3', label: 'Stable Diffusion 3.5' },
  ];

  protected readonly cloudflareModels = [{ id: '@cf/black-forest-labs/flux-1-schnell', label: 'FLUX.1 [schnell] (Black Forest Labs)' }];

  protected readonly hfModels = [
    { id: 'Tongyi-MAI/Z-Image-Turbo', label: 'Z-Image-Turbo — default: fast, good quality and prompt following' },
    { id: 'black-forest-labs/FLUX.1-schnell', label: 'FLUX.1-schnell — strong general purpose, very fast' },
    { id: 'Efficient-Large-Model/SANA1.5_1.6B_1024px_diffusers', label: 'SANA 1.5 1.6B — lightweight, 1024px' },
  ];
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly test = signal<{ succeeded: boolean; message: string; modelAvailable: boolean } | null>(null);
  protected readonly replacingKey = signal(false);
  protected readonly labels = HEALTH_LABELS;
  protected readonly tone = tone;
  protected readonly when = when;

  protected provider = 'OpenAI';
  protected baseUrl = '';
  protected apiKey = '';
  protected model = '';
  protected fallbackModel = '';
  protected maxOutputTokens: number | null = null;
  protected timeoutSeconds: number | null = null;
  protected enabled = false;

  async ngOnInit(): Promise<void> {
    try {
      this.apply(await firstValueFrom(this.api.aiConfiguration()));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected cancelKey(): void {
    this.apiKey = '';
    this.replacingKey.set(false);
  }

  protected async testConnection(): Promise<void> {
    this.busy.set('test');
    this.problem.set(null);
    this.test.set(null);
    try {
      this.test.set(await firstValueFrom(this.api.testAi({
        provider: this.provider, baseUrl: this.baseUrl.trim() || null, model: this.model.trim() || null, apiKey: this.apiKey.trim() || null,
      })));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }

  /** A new key is stored first (separate, audited action), then the settings that may depend on it. */
  protected async save(): Promise<void> {
    const current = this.c();
    if (!current) return;
    this.busy.set('save');
    this.problem.set(null);
    this.notice.set(null);
    try {
      let rowVersion = current.rowVersion;
      if (this.apiKey.trim()) {
        rowVersion = (await firstValueFrom(this.api.replaceAiKey(this.apiKey.trim()))).rowVersion;
        this.apiKey = '';
      }
      this.apply(await firstValueFrom(this.api.saveAiConfiguration({
        provider: this.provider, baseUrl: this.baseUrl.trim() || null, model: this.model.trim(), fallbackModel: this.fallbackModel.trim() || null,
        maxOutputTokens: this.maxOutputTokens || null, requestTimeoutSeconds: this.timeoutSeconds || null, enabled: this.enabled, rowVersion,
      })));
      this.notice.set('AI configuration saved.');
    } catch (error) {
      this.problem.set(toApiProblem(error));
      // Show what was actually stored (e.g. the key may have been saved even if the settings were rejected).
      this.c.set(await firstValueFrom(this.api.aiConfiguration()).catch(() => current));
    } finally {
      this.busy.set(null);
    }
  }

  protected isHfModel(id: string): boolean {
    return this.hfModels.some((m) => m.id === id);
  }

  protected async testVision(): Promise<void> {
    this.busy.set('vis-test');
    this.problem.set(null);
    this.visTest.set(null);
    try {
      this.visTest.set(await firstValueFrom(this.api.testAiVision(this.visModel)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }

  protected async saveVision(): Promise<void> {
    const current = this.c();
    if (!current) return;
    this.busy.set('vis-save');
    this.problem.set(null);
    this.notice.set(null);
    try {
      // An optional separate vision token is stored first (audited on its own), then the vision settings.
      let rowVersion = current.rowVersion;
      if (this.visKey.trim()) {
        rowVersion = (await firstValueFrom(this.api.replaceAiVisionKey(this.visKey.trim()))).rowVersion;
        this.visKey = '';
      }
      this.apply(await firstValueFrom(this.api.saveAiVision({ model: this.visModel, enabled: this.visEnabled, rowVersion })));
      this.notice.set('Image understanding settings saved.');
    } catch (error) {
      this.problem.set(toApiProblem(error));
      this.c.set(await firstValueFrom(this.api.aiConfiguration()).catch(() => current));
    } finally {
      this.busy.set(null);
    }
  }

  protected hfHint(): string {
    return this.hfModels.find((m) => m.id === this.imgModel.trim())?.label ?? 'Any Hugging Face model served for text-to-image (owner/name).';
  }

  /** Switching provider swaps in that provider's default model unless the admin typed their own. */
  protected chooseImgProvider(provider: string): void {
    const defaults = ['', 'gpt-image-1', ...this.hfModels.map((m) => m.id), ...this.stabilityModels.map((m) => m.id), ...this.cloudflareModels.map((m) => m.id)];
    if (provider === 'StabilityAI' || provider === 'CloudflareWorkersAI' || defaults.includes(this.imgModel.trim()))
      this.imgModel = provider === 'HuggingFace' ? this.hfModels[0].id : provider === 'StabilityAI' ? this.stabilityModels[0].id
        : provider === 'CloudflareWorkersAI' ? this.cloudflareModels[0].id : 'gpt-image-1';
    if (provider !== this.imgProvider) this.imgBaseUrl = '';
    this.imgProvider = provider;
  }

  protected async testImage(): Promise<void> {
    this.busy.set('img-test');
    this.problem.set(null);
    this.imgTest.set(null);
    try {
      this.imgTest.set(await firstValueFrom(this.api.testAiImage({
        provider: this.imgProvider, baseUrl: this.imgBaseUrl.trim() || null, model: this.imgModel.trim() || null, apiKey: this.imgKey.trim() || null,
      })));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }

  /** An optional separate image key is stored first (audited on its own), then the image settings. */
  protected async saveImage(): Promise<void> {
    const current = this.c();
    if (!current) return;
    this.busy.set('img-save');
    this.problem.set(null);
    this.notice.set(null);
    try {
      let rowVersion = current.rowVersion;
      if (this.imgKey.trim()) {
        rowVersion = (await firstValueFrom(this.api.replaceAiImageKey(this.imgKey.trim()))).rowVersion;
        this.imgKey = '';
      }
      this.apply(await firstValueFrom(this.api.saveAiImage({
        provider: this.imgProvider, baseUrl: this.imgBaseUrl.trim() || null, model: this.imgModel.trim(), size: this.imgSize,
        enabled: this.imgEnabled, rowVersion,
      })));
      this.notice.set('Image generation settings saved.');
    } catch (error) {
      this.problem.set(toApiProblem(error));
      this.c.set(await firstValueFrom(this.api.aiConfiguration()).catch(() => current));
    } finally {
      this.busy.set(null);
    }
  }

  private apply(cfg: AiConfiguration): void {
    this.c.set(cfg);
    this.replacingKey.set(false);
    this.provider = cfg.provider;
    this.baseUrl = cfg.baseUrl ?? '';
    this.model = cfg.model ?? '';
    this.fallbackModel = cfg.fallbackModel ?? '';
    this.maxOutputTokens = cfg.maxOutputTokens;
    this.timeoutSeconds = cfg.requestTimeoutSeconds;
    this.enabled = cfg.source === 'NONE' ? true : cfg.enabled;
    this.replacingImgKey.set(false);
    this.imgProvider = cfg.image.provider ?? 'HuggingFace';
    // For Cloudflare the stored URL carries the Account ID; show just the ID.
    this.imgBaseUrl = cfg.image.provider === 'CloudflareWorkersAI'
      ? (cfg.image.baseUrl?.match(/accounts\/([0-9a-f]{32})/)?.[1] ?? '')
      : cfg.image.baseUrl ?? '';
    this.imgModel = cfg.image.model ?? (this.imgProvider === 'HuggingFace' ? this.hfModels[0].id : 'gpt-image-1');
    this.imgSize = cfg.image.size ?? '1024x1024';
    this.imgEnabled = cfg.image.model ? cfg.image.enabled : true;
    this.replacingVisKey.set(false);
    this.visModel = cfg.vision.model;
    this.visEnabled = cfg.vision.enabled;
  }
}
