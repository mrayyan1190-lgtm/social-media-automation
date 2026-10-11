import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/http/api-problem';
import { ProblemBannerComponent } from '../../../shared/components/problem-banner.component';
import { GoogleSignIn, HEALTH_LABELS, InternalApiService, SocialProvider, WorkspaceListItem, tone, when } from '../internal-api.service';
import { NoticeComponent } from '../../../shared/components/notice.component';

/** One provider app. The client secret is write-only: once saved it shows "Configured ✓" and can only be replaced. */
@Component({
  selector: 'app-provider-card',
  imports: [NoticeComponent, FormsModule, ProblemBannerComponent],
  template: `
    @let p = provider();
    <form class="card form-grid" (ngSubmit)="save()">
      <div class="card-head">
        <h2><span class="head-ic brand" [class.fb]="meta()">{{ meta() ? 'f' : 'in' }}</span>{{ meta() ? 'Meta' : 'LinkedIn' }}</h2>
        <span [class]="'badge delivery ' + tone(p.configurationStatus)">{{ labels[p.configurationStatus] }}</span>
      </div>
      @if (p.source === 'ENVIRONMENT') { <p class="banner info">Using the deployment's environment configuration. Saving here takes precedence.</p> }
      <app-problem-banner [problem]="problem()" />
      <app-notice [message]="notice()" />

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
          @if (p.secretConfigured && p.source === 'PLATFORM') { <button type="button" class="link" (click)="replacing.set(false); secret = ''">Keep the current secret</button> }
        }
      </div>
      <label>Redirect URI <input name="redirectUri" type="url" [(ngModel)]="redirectUri" [placeholder]="'https://your-api/api/v1/oauth/' + (meta() ? 'facebook' : 'linkedin') + '/callback'" /></label>
      @if (meta()) {
        <label>Meta Configuration ID <input name="configurationId" [(ngModel)]="configurationId" autocomplete="off" placeholder="Optional" /></label>
      }
      <label class="checkbox"><input name="enabled" type="checkbox" [(ngModel)]="enabled" /> Enabled</label>

      <ul class="kv">
        <li><span>Last Successful Request</span><span>{{ when(p.lastSuccessAtUtc) }}</span></li>
        <li><span>Failures Last 24h</span><strong [class.text-danger]="p.failuresLast24Hours > 0">{{ p.failuresLast24Hours }}</strong></li>
        @if (p.lastFailure) { <li><span>Last Failure</span><span class="hint">{{ p.lastFailure }}</span></li> }
      </ul>

      @if (validation(); as v) { <app-notice [message]="v.message" [tone]="v.valid ? 'success' : 'error'" /> }
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
  protected configurationId = '';
  protected enabled = true;

  protected meta(): boolean {
    return this.provider().provider === 'META';
  }

  ngOnInit(): void {
    const p = this.provider();
    this.clientId = p.clientId;
    this.redirectUri = p.redirectUri;
    this.configurationId = p.configurationId ?? '';
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
        clientId: this.clientId.trim(), redirectUri: this.redirectUri.trim() || null, configurationId: this.configurationId.trim() || null, enabled: this.enabled, rowVersion,
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

/** "Continue with Google" on the login page. Stored encrypted in the database; takes effect on the next sign-in. */
@Component({
  selector: 'app-google-sign-in-card',
  imports: [NoticeComponent, FormsModule, ProblemBannerComponent],
  template: `
    <form class="card form-grid" (ngSubmit)="save()">
      <div class="card-head">
        <h2><span class="head-ic purple"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2 4 6v6c0 5 3.4 8.5 8 10 4.6-1.5 8-5 8-10V6l-8-4Z"/><path d="m8.5 12 2.5 2.5L16 9"/></svg></span>Google sign-in</h2>
        <span [class]="'badge delivery ' + (state()?.source === 'NONE' ? 'neutral' : 'success')">{{ state()?.source === 'NONE' ? 'Not configured' : 'Configured' }}</span>
      </div>
      @if (state()?.source === 'ENVIRONMENT') { <p class="banner info">Using the deployment's environment configuration. Saving here takes precedence.</p> }
      <app-problem-banner [problem]="problem()" />
      <app-notice [message]="notice()" />
      <label>Client ID * <input name="clientId" [(ngModel)]="clientId" required autocomplete="off" /></label>
      <label>Client Secret {{ state()?.source === 'PLATFORM' ? '' : '*' }}
        <input name="secret" type="password" autocomplete="new-password" [(ngModel)]="secret" spellcheck="false"
          [placeholder]="state()?.source === 'PLATFORM' ? 'Configured ✓ (leave blank to keep)' : 'Paste the secret'" />
      </label>
      <label>Redirect URI <input [value]="callback" readonly /></label>
      <div class="actions">
        <button type="submit" class="primary" [disabled]="busy() || !clientId.trim()">{{ busy() ? 'Saving…' : 'Save' }}</button>
      </div>
    </form>
  `,
})
export class GoogleSignInCardComponent implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly state = signal<GoogleSignIn | null>(null);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly callback = `${location.origin}/api/v1/auth/google/callback`;
  protected clientId = '';
  protected secret = '';

  async ngOnInit(): Promise<void> {
    try {
      const s = await firstValueFrom(this.api.googleSignIn());
      this.state.set(s);
      this.clientId = s.clientId;
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    this.notice.set(null);
    try {
      this.state.set(await firstValueFrom(this.api.saveGoogleSignIn({ clientId: this.clientId.trim(), clientSecret: this.secret.trim() || null })));
      this.secret = '';
      this.notice.set('Saved. The next Google sign-in uses it.');
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Connects a Facebook Page to a workspace from a pasted Page access token (approved exception to OAuth-only connections).
 * The server checks the token with Meta and stores it encrypted; it is never shown again.
 */
@Component({
  selector: 'app-page-token-card',
  imports: [NoticeComponent, FormsModule, ProblemBannerComponent],
  template: `
    <form class="card form-grid" (ngSubmit)="connect()">
      <div class="card-head"><h2><span class="head-ic peach"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg></span>Facebook Page token</h2></div>
      <app-problem-banner [problem]="problem()" />
      <app-notice [message]="notice()" />
      <label>Workspace *
        <select name="org" [(ngModel)]="organizationId" required>
          <option value="" disabled>Choose a workspace</option>
          <option value="ALL">All active workspaces ({{ active().length }})</option>
          @for (w of workspaces(); track w.id) { <option [value]="w.id">{{ w.name }}{{ w.primaryAdminEmail ? " — " + w.primaryAdminEmail : "" }}</option> }
        </select>
      </label>
      <label>Page access token *
        <input name="token" type="password" autocomplete="new-password" spellcheck="false" [(ngModel)]="token" placeholder="EAA…" />
      </label>
      @if (organizationId === 'ALL') { <p class="hint">Also applies to workspaces created later.</p> }
      <div class="actions">
        <button type="submit" class="primary" [disabled]="busy() || !organizationId || !token.trim()">{{ busy() ? 'Connecting…' : 'Connect Page' }}</button>
      </div>
    </form>
  `,
})
export class PageTokenCardComponent implements OnInit {
  private readonly api = inject(InternalApiService);
  protected readonly workspaces = signal<WorkspaceListItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected organizationId = '';
  protected token = '';
  protected readonly active = () => this.workspaces().filter((w) => w.status === 'Active');

  async ngOnInit(): Promise<void> {
    try {
      this.workspaces.set(await firstValueFrom(this.api.workspaces({})));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    }
  }

  protected async connect(): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    this.notice.set(null);
    // "All" connects each active workspace in turn (each gets its own destination); one failure does not stop the rest.
    const targets = this.organizationId === 'ALL' ? this.active() : this.workspaces().filter((w) => w.id === this.organizationId);
    const failed: string[] = [];
    let last: { pageName: string; expiresAtUtc: string | null; capabilities: string[] } | null = null;
    try {
      for (const w of targets) {
        try {
          last = await firstValueFrom(this.api.connectFacebookPageToken(w.id, this.token.trim(), this.organizationId === 'ALL'));
        } catch (error) {
          if (targets.length === 1) throw error;
          failed.push(`${w.name}: ${toApiProblem(error).detail}`);
        }
      }
      if (last) {
        this.token = '';
        const expiry = last.expiresAtUtc ? ` Token expires ${when(last.expiresAtUtc)}.` : '';
        this.notice.set(`Connected "${last.pageName}" as a LIVE Facebook destination in ${targets.length - failed.length} workspace(s)`
          + (this.organizationId === 'ALL' ? ' and in every new workspace.' : '.') + expiry
          + (last.capabilities.includes('publish_text') ? '' : ' The token lacks pages_manage_posts, so it cannot publish.'));
      }
      if (failed.length) this.problem.set({ status: 0, code: 'PARTIAL_FAILURE', detail: `Not connected — ${failed.join(' · ')}` });
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Platform integrations (the operator's own provider apps), not customer social accounts. */
@Component({
  selector: 'app-internal-social-providers-page',
  imports: [ProviderCardComponent, GoogleSignInCardComponent, PageTokenCardComponent, ProblemBannerComponent],
  template: `
    <div class="page-header"><h1>Social Providers</h1></div>
    <app-problem-banner [problem]="problem()" />
    <div class="soc-grid">
      @for (p of providers(); track p.provider) {
        <app-provider-card [provider]="p" (changed)="replace($event)" />
      } @empty { <p class="muted">Loading…</p> }
      <app-page-token-card />
      <app-google-sign-in-card />
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
