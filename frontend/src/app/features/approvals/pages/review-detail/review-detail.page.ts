import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../../core/http/api-problem';
import { WorkspaceService } from '../../../../core/services/workspace.service';
import { PlatformIconComponent } from '../../../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../../../shared/components/problem-banner.component';
import { DEMO_MODE_NOTE } from '../../../../shared/models/states';
import { postText } from '../../../content/models/post.model';
import { MediaApiService } from '../../../content/services/media-api.service';
import { ReviewDetail, ReviewSummary } from '../../models/review.model';
import { ReviewApiService } from '../../services/review-api.service';
import { NoticeComponent } from '../../../../shared/components/notice.component';

/**
 * Review of one exact version. Shows the submitted content (never newer edits), account, media, notes, automated
 * findings and history. Buttons are hidden when the API would refuse, but the API decides.
 */
@Component({
  selector: 'app-review-detail-page',
  imports: [NoticeComponent, RouterLink, DatePipe, PlatformIconComponent, ProblemBannerComponent],
  templateUrl: './review-detail.page.html',
  styleUrl: './review-detail.page.css',
})
export class ReviewDetailPage {
  protected readonly reviewLabels: Record<string, string> = { PENDING: 'In Review', APPROVED: 'Approved', CHANGES_REQUESTED: 'Changes Requested' };
  protected readonly tones: Record<string, string> = { PENDING: 'waiting', APPROVED: 'success', CHANGES_REQUESTED: 'danger' };
  protected readonly demoNote = DEMO_MODE_NOTE;
  private readonly router = inject(Router);
  private readonly api = inject(ReviewApiService);
  private readonly media = inject(MediaApiService);
  protected readonly workspace = inject(WorkspaceService);

  readonly reviewId = input.required<string>();

  protected readonly detail = signal<ReviewDetail | null>(null);
  /** Pending reviews of the other platforms of the same post group; the decision here is applied to them too. */
  protected readonly siblings = signal<ReviewSummary[]>([]);
  protected readonly busy = signal(false);
  protected readonly loading = signal(true);
  protected readonly notice = signal<string | null>(null);
  protected readonly problem = signal<ApiProblem | null>(null);
  protected readonly showChangesForm = signal(false);
  protected readonly explanation = signal('');

  constructor() {
    effect(() => {
      if (this.workspace.orgId() && this.reviewId()) void this.load();
    });
  }

  protected text(d: ReviewDetail): string {
    return postText(d.version);
  }

  protected imageUrl(mediaAssetId: string): string {
    return this.media.previewUrl(this.workspace.orgId()!, mediaAssetId);
  }

  async load(): Promise<void> {
    const orgId = this.workspace.orgId();
    if (!orgId) return;
    this.loading.set(true);
    try {
      const detail = await firstValueFrom(this.api.get(orgId, this.reviewId()));
      this.detail.set(detail);
      this.siblings.set(detail.review.status !== 'PENDING' ? [] : (await firstValueFrom(this.api.list(orgId, { status: 'PENDING' })))
        .filter((r) => r.groupId === detail.review.groupId && r.id !== detail.review.id));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected approve(): Promise<void> {
    return this.decide((orgId, review) => firstValueFrom(this.api.approve(orgId, review)), 'Approved. It can now be scheduled or published.');
  }

  protected requestChanges(): Promise<void> {
    return this.decide((orgId, review) => firstValueFrom(this.api.requestChanges(orgId, review, this.explanation())), 'Changes requested. The creator has been notified.');
  }

  private async decide(action: (orgId: string, review: ReviewSummary) => Promise<unknown>, message: string): Promise<void> {
    const orgId = this.workspace.orgId();
    const detail = this.detail();
    if (!orgId || !detail) return;
    this.busy.set(true);
    this.problem.set(null);
    this.notice.set(null);
    try {
      // One decision for every platform of the post group; each platform's exact version is still approved (or sent
      // back) on its own, and the server checks each one.
      for (const review of [detail.review, ...this.siblings()]) {
        try {
          await action(orgId, review);
        } catch (error) {
          throw review === detail.review ? error : new Error(`${review.platform}: ${toApiProblem(error).detail}`);
        }
      }
      this.showChangesForm.set(false);
      // After approval, go to Approvals → Approved to schedule or publish. After requesting changes the reviewer stays
      // here: the creator makes the edits (they are notified and the notification opens the post in Composer).
      if (message.startsWith('Approved')) {
        await this.router.navigate(['/approvals'], { queryParams: { tab: 'approved' } });
        return;
      }
      this.notice.set(message);
      await this.load();
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.busy.set(false);
    }
  }
}
