import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { AiConfiguration, HEALTH_LABELS, InternalApiService, tone, when } from '../internal-api.service';

/**
 * The system LLM used by every workspace. The API key is write-only: after saving, the page only shows that a key is
 * configured and when it was replaced. The browser never receives the stored key.
 */
@Component({
  selector: 'app-internal-ai-page',
  imports: [FormsModule, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>AI / LLM</h1><p class="muted">The hosted model all workspaces use for generation, rewrites and evaluation.</p></div></div>
    <app-problem-banner [problem]="problem()" />
    @if (notice(); as n) { <p class="banner success" role="status">{{ n }}</p> }

    @if (c(); as cfg) {
      @if (cfg.source === 'ENVIRONMENT') {
        <p class="banner info">Currently using the deployment's environment configuration. Saving here stores platform settings, which take precedence.</p>
      }
      <div class="admin-grid wide-left">
        <form class="card form-grid" (ngSubmit)="save()">
          <h2>Configuration</h2>
          <label>Provider *
            <select name="provider" [(ngModel)]="provider" required>
              <option value="OpenAI">OpenAI</option><option value="Groq">Groq</option><option value="OpenAICompatible">OpenAI-compatible endpoint</option>
            </select>
          </label>
          @if (provider === 'OpenAICompatible') {
            <label>Base URL * <input name="baseUrl" type="url" [(ngModel)]="baseUrl" placeholder="https://…/v1" /></label>
          }

          <div class="secret-field">
            <span class="field-label">{{ provider === 'OpenAI' ? 'OpenAI ' : '' }}API Key *</span>
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

          @if (test(); as t) {
            <p [class]="'banner ' + (t.succeeded && t.modelAvailable ? 'success' : 'error')">{{ t.message }}</p>
          }
          <div class="actions">
            <button type="button" (click)="testConnection()" [disabled]="busy()">{{ busy() === 'test' ? 'Testing…' : 'Test Connection' }}</button>
            <button type="submit" class="primary" [disabled]="busy() || !model.trim()">{{ busy() === 'save' ? 'Saving…' : 'Save Configuration' }}</button>
          </div>
        </form>

        <section class="card">
          <h2>Provider Status</h2>
          <ul class="kv">
            <li><span>Configuration</span><span [class]="'badge delivery ' + tone(cfg.status.configured)">{{ labels[cfg.status.configured] }}</span></li>
            <li><span>Health</span><span [class]="'badge delivery ' + tone(cfg.status.health)">{{ labels[cfg.status.health] }}</span></li>
            <li><span>Default Model</span><span>{{ cfg.status.defaultModel || '—' }}</span></li>
            <li><span>Last Successful Request</span><span>{{ when(cfg.status.lastSuccessAtUtc) }}</span></li>
            <li><span>Last Failure</span><span>{{ cfg.status.lastFailure ? cfg.status.lastFailure + ' · ' + when(cfg.status.lastFailureAtUtc) : '—' }}</span></li>
            <li><span>Requests Today</span><strong>{{ cfg.status.requestsToday }}</strong></li>
            <li><span>Failed Today</span><strong [class.text-danger]="cfg.status.failedToday > 0">{{ cfg.status.failedToday }}</strong></li>
          </ul>
          <p class="hint">Health reflects real calls in the last hour; with no recent calls it is "Not Monitored". Use Test Connection to check now.</p>
        </section>
      </div>
      @let img = cfg.image;
      <div class="admin-grid wide-left">
        <form class="card form-grid" (ngSubmit)="saveImage()">
          <h2>Image Generation</h2>
          <p class="hint">Creates images for posts. Each image becomes a new post version and is reviewed by a person before it can be scheduled.</p>
          <label>Provider *
            <select name="imgProvider" [(ngModel)]="imgProvider">
              <option value="OpenAI">OpenAI</option><option value="OpenAICompatible">OpenAI-compatible endpoint</option>
            </select>
          </label>
          @if (imgProvider === 'OpenAICompatible') {
            <label>Base URL * <input name="imgBaseUrl" type="url" [(ngModel)]="imgBaseUrl" placeholder="https://…/v1" /></label>
          }
          <div class="two-inputs">
            <label>Image Model * <input name="imgModel" [(ngModel)]="imgModel" placeholder="gpt-image-1" />
              <span class="hint">GPT image models return JPEG, which Instagram requires; DALL·E returns PNG (LinkedIn and Facebook only).</span>
            </label>
            <label>Image Size *
              <select name="imgSize" [(ngModel)]="imgSize">
                @for (s of img.sizes; track s) { <option [value]="s">{{ s }}{{ s === '1024x1024' ? ' (square)' : ' (landscape)' }}</option> }
              </select>
            </label>
          </div>

          <div class="secret-field">
            <span class="field-label">Image API Key <span class="hint">optional</span></span>
            @if (img.ownApiKey && !replacingImgKey()) {
              <div class="secret-set">
                <span class="badge delivery success">Configured ✓</span>
                <span class="hint">Last updated: {{ when(img.apiKeyUpdatedAtUtc) }}</span>
                <button type="button" (click)="replacingImgKey.set(true)">Replace API Key</button>
              </div>
            } @else {
              <input name="imgKey" type="password" autocomplete="new-password" [(ngModel)]="imgKey" spellcheck="false"
                [placeholder]="cfg.apiKeyConfigured ? 'Leave empty to use the AI / LLM API key' : 'Paste the API key'" />
              <span class="hint">Stored encrypted in the secret store. It is never shown again.</span>
              @if (img.ownApiKey) { <button type="button" class="link" (click)="imgKey = ''; replacingImgKey.set(false)">Keep the current key</button> }
            }
          </div>
          <label class="checkbox"><input name="imgEnabled" type="checkbox" [(ngModel)]="imgEnabled" /> Image Generation Enabled</label>

          @if (imgTest(); as t) { <p [class]="'banner ' + (t.succeeded && t.modelAvailable ? 'success' : 'error')">{{ t.message }}</p> }
          <div class="actions">
            <button type="button" (click)="testImage()" [disabled]="busy()">{{ busy() === 'img-test' ? 'Testing…' : 'Test Connection' }}</button>
            <button type="submit" class="primary" [disabled]="busy() || !imgModel.trim()">{{ busy() === 'img-save' ? 'Saving…' : 'Save Image Settings' }}</button>
          </div>
        </form>

        <section class="card">
          <h2>Image Provider Status</h2>
          <ul class="kv">
            <li><span>Configuration</span><span [class]="'badge delivery ' + tone(img.status.configured)">{{ labels[img.status.configured] }}</span></li>
            <li><span>Health</span><span [class]="'badge delivery ' + tone(img.status.health)">{{ labels[img.status.health] }}</span></li>
            <li><span>Model</span><span>{{ img.model || '—' }} · {{ img.size || '—' }}</span></li>
            <li><span>API Key</span><span>{{ img.ownApiKey ? 'Separate image key' : cfg.apiKeyConfigured ? 'Uses the AI / LLM key' : 'None' }}</span></li>
            <li><span>Last Successful Image</span><span>{{ when(img.status.lastSuccessAtUtc) }}</span></li>
            <li><span>Last Failure</span><span>{{ img.status.lastFailure ? img.status.lastFailure + ' · ' + when(img.status.lastFailureAtUtc) : '—' }}</span></li>
            <li><span>Images Today</span><strong>{{ img.status.requestsToday }}</strong></li>
            <li><span>Failed Today</span><strong [class.text-danger]="img.status.failedToday > 0">{{ img.status.failedToday }}</strong></li>
          </ul>
          <p class="hint">Test Connection only checks the key and model list; it does not generate (or bill) an image.</p>
        </section>
      </div>
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class AiConfigurationPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly c = signal<AiConfiguration | null>(null);
  protected readonly busy = signal<'save' | 'test' | 'img-save' | 'img-test' | null>(null);
  protected readonly imgTest = signal<{ succeeded: boolean; message: string; modelAvailable: boolean } | null>(null);
  protected readonly replacingImgKey = signal(false);
  protected imgProvider = 'OpenAI';
  protected imgBaseUrl = '';
  protected imgModel = 'gpt-image-1';
  protected imgSize = '1024x1024';
  protected imgKey = '';
  protected imgEnabled = false;
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
    this.imgProvider = cfg.image.provider ?? 'OpenAI';
    this.imgBaseUrl = cfg.image.baseUrl ?? '';
    this.imgModel = cfg.image.model ?? 'gpt-image-1';
    this.imgSize = cfg.image.size ?? '1024x1024';
    this.imgEnabled = cfg.image.model ? cfg.image.enabled : true;
  }
}
