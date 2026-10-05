import { Component, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { MediaLibraryItem } from '../content/models/post.model';
import { MediaApiService } from '../content/services/media-api.service';
import { formatInZone } from '../publications/models/time-zones';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

/**
 * Media Library: the workspace's uploaded images with where they are used. Assets are immutable; attaching one to a post
 * (from the post page) creates a new version of that post.
 */
@Component({
  selector: 'app-library-page',
  imports: [IconComponent, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Media Library</h1>
        <p class="muted">Images for your posts. JPEG or PNG, up to 8 MB. Attach an image from a post's page.</p>
      </div>
      @if (workspace.canManageCampaigns()) {
        <label class="button primary">
          <app-icon name="upload" [size]="16" />{{ uploading() ? 'Uploading…' : 'Upload Image' }}
          <input type="file" accept="image/jpeg,image/png" hidden (change)="upload($event)" [disabled]="uploading()" />
        </label>
      }
    </div>
    @if (uploadError(); as e) { <p class="banner error" role="alert">{{ e }}</p> }
    <app-problem-banner [problem]="problem()" />

    @if (loading() && !items().length) {
      <p class="muted">Loading…</p>
    } @else if (!items().length) {
      <section class="empty-hero compact">
        <app-icon name="image" [size]="26" />
        <h2>Upload your first media asset</h2>
        <p class="muted">Images you upload here can be attached to any post. Instagram posts need one image.</p>
      </section>
    } @else {
      <div class="media-grid">
        @for (item of items(); track item.asset.id) {
          <figure class="media-card">
            <img [src]="previewUrl(item.asset.id)" [alt]="item.asset.originalFileName" loading="lazy" />
            <figcaption>
              <strong [title]="item.asset.originalFileName">{{ item.asset.originalFileName }}</strong>
              @if (item.asset.sourceType === 'AiGenerated') { <span class="badge">AI generated</span> }
              <span class="hint">{{ type(item.asset.contentType) }} · {{ item.asset.width }}×{{ item.asset.height }} · {{ time(item.asset.createdAtUtc) }}</span>
              <span class="hint">{{ item.usedByPostCount ? 'Used by ' + item.usedByPostCount + ' post' + (item.usedByPostCount > 1 ? 's' : '') : 'Not used yet' }}</span>
            </figcaption>
          </figure>
        }
      </div>
    }
  `,
})
export class LibraryPage {
  private readonly api = inject(MediaApiService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly items = signal<MediaLibraryItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
  }

  protected previewUrl(id: string): string {
    return this.api.previewUrl(this.workspace.orgId()!, id);
  }

  protected type(contentType: string): string {
    return contentType.replace('image/', '').toUpperCase();
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone(), { dateStyle: 'medium' });
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const orgId = this.workspace.orgId();
    if (!file || !orgId) return;
    this.uploadError.set(null);
    // Early hints only; the server inspects the bytes and applies the authoritative rules.
    if (!['image/jpeg', 'image/png'].includes(file.type)) {
      this.uploadError.set('Choose a JPEG or PNG image. Video is not supported yet.');
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      this.uploadError.set('Images must be at most 8 MB.');
      return;
    }
    this.uploading.set(true);
    try {
      await firstValueFrom(this.api.upload(orgId, file));
      await this.load(orgId);
    } catch (error) {
      this.uploadError.set(toApiProblem(error).detail);
    } finally {
      this.uploading.set(false);
    }
  }

  private async load(orgId: string): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set(await firstValueFrom(this.api.list(orgId)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
