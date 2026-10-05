import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { HEALTH_LABELS, InternalApiService, SocialProvider, tone, when } from '../internal-api.service';

/** One provider app. The client secret is write-only: once saved it shows "Configured ✓" and can only be replaced. */
@Component({
  selector: 'app-provider-card',
  imports: [FormsModule, ProblemBannerComponent],
  template: `
    @let p = provider();
    <form class="card form-grid" (ngSubmit)="save()">
      <div class="card-head">
        <h2>{{ meta() ? 'Meta' : 'LinkedIn' }}</h2>
        <span [class]="'badge delivery ' + tone(p.configurationStatus)">{{ labels[p.configurationStatus] }}</span>
      </div>
      @if (meta()) { <p class="hint">One Meta app serves Facebook Pages and Instagram business accounts.</p> }
      @if (p.source === 'ENVIRONMENT') { <p class="banner info">Using the deployment's environment configuration. Saving here takes precedence.</p> }
      <app-problem-banner [problem]="problem()" />
      @if (notice(); as n) { <p class="banner success" role="status">{{ n }}</p> }

      <label>{{ meta() ? 'Meta App ID' : 'Client ID' }} * <input name="clientId" [(ngModel)]="clientId" required autocomplete="off" /></label>
      <div class="secret-field">
        <span class="field-label">{{ meta() ? 'Meta App Secret' : 'Client Secret' }} *</span>
        @if (p.secretConfigured && p.source === 'PLATFORM' && !replacing()) {
          <div class="secret-set">
            <span class="badge delivery success">Configured ✓</span>
            <span class="hint">Last updated: {{ when(p.secretUpdatedAtUtc) }}</span>
            <button type="button" (click)="replacing.set(true)">Replace Secret</button>
          </div>
        } @else {
          <input name="secret" type="password" autocomplete="new-password" [(ngModel)]="secret" spellcheck="false" placeholder="Paste the secret" />
          <span class="hint">Stored encrypted in the secret store. It is never shown again.</span>
          @if (p.secretConfigured && p.source === 'PLATFORM') { <button type="button" class="link" (click)="replacing.set(false); secret = ''">Keep the current secret</button> }
        }
      </div>
      <label>Redirect URI <input name="redirectUri" type="url" [(ngModel)]="redirectUri" [placeholder]="'https://your-api/api/v1/oauth/' + (meta() ? 'facebook' : 'linkedin') + '/callback'" />
        @if (meta() && p.derivedRedirectUri) { <span class="hint">Instagram callback: {{ p.derivedRedirectUri }}</span> }
      </label>
      <label class="checkbox"><input name="enabled" type="checkbox" [(ngModel)]="enabled" /> Enabled</label>

      <ul class="kv">
        <li><span>Configuration Status</span><span>{{ labels[p.configurationStatus] }}</span></li>
        <li><span>Last Successful Request</span><span>{{ when(p.lastSuccessAtUtc) }}</span></li>
        <li><span>Failures Last 24h</span><strong [class.text-danger]="p.failuresLast24Hours > 0">{{ p.failuresLast24Hours }}</strong></li>
        @if (p.lastFailure) { <li><span>Last Failure</span><span class="hint">{{ p.lastFailure }}</span></li> }
      </ul>

      @if (validation(); as v) { <p [class]="'banner ' + (v.valid ? 'success' : 'error')">{{ v.message }}</p> }
      <div class="actions">
        <button type="button" (click)="validate()" [disabled]="busy()">{{ busy() === 'validate' ? 'Validating…' : 'Validate Configuration' }}</button>
        <button type="submit" class="primary" [disabled]="busy() || !clientId.trim()">{{ busy() === 'save' ? 'Saving…' : 'Save' }}</button>
      </div>
    </form>
  `,
})
export class ProviderCardComponent implements OnInit {
  private readonly api = inject(InternalApiService);
  readonly provider = input.required<SocialProvider>();
  readonly changed = output<SocialProvider>();

  protected readonly labels = HEALTH_LABELS;
  protected readonly tone = tone;
  protected readonly when = when;
  protected readonly busy = signal<'save' | 'validate' | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly validation = signal<{ valid: boolean; message: string } | null>(null);
  protected readonly replacing = signal(false);
  protected clientId = '';
  protected secret = '';
  protected redirectUri = '';
  protected enabled = true;

  protected meta(): boolean {
    return this.provider().provider === 'META';
  }

  ngOnInit(): void {
    const p = this.provider();
    this.clientId = p.clientId;
    this.redirectUri = p.redirectUri;
    this.enabled = p.source === 'NONE' ? true : p.enabled;
  }

  private get key(): 'linkedin' | 'meta' {
    return this.meta() ? 'meta' : 'linkedin';
  }

  /** A new secret is stored first (separate, audited action), then the settings that may require it. */
  protected async save(): Promise<void> {
    this.busy.set('save');
    this.problem.set(null);
    this.notice.set(null);
    try {
      let rowVersion = this.provider().rowVersion;
      if (this.secret.trim()) {
        rowVersion = (await firstValueFrom(this.api.replaceProviderSecret(this.key, this.secret.trim()))).rowVersion;
        this.secret = '';
      }
      const saved = await firstValueFrom(this.api.saveSocialProvider(this.key, {
        clientId: this.clientId.trim(), redirectUri: this.redirectUri.trim() || null, enabled: this.enabled, rowVersion,
      }));
      this.replacing.set(false);
      this.changed.emit(saved);
      this.notice.set('Saved.');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }

  protected async validate(): Promise<void> {
    this.busy.set('validate');
    this.validation.set(null);
    try {
      this.validation.set(await firstValueFrom(this.api.testSocialProvider(this.key)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(null);
    }
  }
}

/** Platform integrations (the operator's own provider apps), not customer social accounts. */
@Component({
  selector: 'app-internal-social-providers-page',
  imports: [ProviderCardComponent, ProblemBannerComponent],
  template: `
    <div class="page-header"><div><h1>Social Providers</h1><p class="muted">Platform app credentials that customers' LinkedIn, Facebook and Instagram connections use.</p></div></div>
    <app-problem-banner [problem]="problem()" />
    <div class="admin-grid">
      @for (p of providers(); track p.provider) {
        <app-provider-card [provider]="p" (changed)="replace($event)" />
      } @empty { <p class="muted">Loading…</p> }
    </div>
  `,
})
export class SocialProvidersPage implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly providers = signal<SocialProvider[]>([]);
  protected readonly problem = signal<ApiProblem | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.providers.set(await firstValueFrom(this.api.socialProviders()));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected replace(saved: SocialProvider): void {
    this.providers.update((all) => all.map((p) => (p.provider === saved.provider ? saved : p)));
  }
}
