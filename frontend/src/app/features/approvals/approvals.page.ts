import { Component, ViewEncapsulation, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { Post } from '../content/models/post.model';
import { PostApiService } from '../content/services/post-api.service';
import { PublicationPanelComponent } from '../publications/components/publication-panel.component';
import { formatInZone } from '../publications/models/time-zones';
import { ReviewSummary } from './models/review.model';
import { ReviewApiService } from './services/review-api.service';

type Tab = 'pending' | 'mine' | 'changes' | 'approved';

const STATUS_TEXT: Record<string, { label: string; tone: string }> = {
  PENDING: { label: 'In Review', tone: 'ap-review' },
  APPROVED: { label: 'Approved', tone: 'ap-approved' },
  CHANGES_REQUESTED: { label: 'Changes Requested', tone: 'ap-attn' },
};

/**
 * Human review. Each review covers one exact version; Approve / Request Changes happen on the review page. Approved
 * posts are scheduled or published from the Approved tab (the Composer only edits and submits).
 */
@Component({
  selector: 'app-approvals-page',
  imports: [RouterLink, PlatformIconComponent, ProblemBannerComponent, PublicationPanelComponent],
  templateUrl: './approvals.page.html',
  styleUrl: './approvals.page.css',
  // The design styles also cover the publication panel inside each Approved card (all classes are ap-prefixed).
  encapsulation: ViewEncapsulation.None,
})
export class ApprovalsPage {
  private readonly api = inject(ReviewApiService);
  private readonly postsApi = inject(PostApiService);
  private readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly statusText = STATUS_TEXT;
  /** ?tab=approved opens the Approved tab (used right after approving). */
  readonly tabQuery = input<string>(undefined, { alias: 'tab' });
  protected readonly tab = signal<Tab>(this.workspace.canReview() ? 'pending' : 'mine');
  /** Approved reviews whose version is still the post's current, approved version, with that post. */
  protected readonly ready = signal<{ review: ReviewSummary; post: Post }[]>([]);
  protected readonly reviews = signal<ReviewSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly problem = signal<ApiProblem | null>(null);

  private readonly me = computed(() => this.auth.me()?.userId);
  /** The newest review of each post: older change requests are already answered by a newer version. */
  private readonly latest = computed(() => {
    const seen = new Set<string>();
    return this.reviews().filter((r) => {
      if (seen.has(r.postId)) return false;
      seen.add(r.postId);
      return true;
    });
  });
  protected readonly shown = computed(() => this.select(this.tab()));
  protected readonly emptyText = computed(() => ({
    pending: 'Nothing is waiting for your review.',
    mine: 'You have not submitted anything for review yet.',
    changes: 'No open change requests.',
    approved: 'No approved posts yet.',
  })[this.tab()]);

  constructor() {
    effect(() => {
      if (this.tabQuery() === 'approved') this.tab.set('approved');
    });
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
  }

  protected count(tab: Tab): number {
    return this.select(tab).length;
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone());
  }

  private select(tab: Tab): ReviewSummary[] {
    const me = this.me();
    switch (tab) {
      // Single-login MVP: every pending review, including my own submissions. Oldest first.
      case 'pending':
        return this.reviews()
          .filter((r) => r.status === 'PENDING')
          .reverse();
      case 'mine':
        return this.reviews().filter((r) => r.submittedByUserId === me);
      case 'changes':
        return this.latest().filter((r) => r.status === 'CHANGES_REQUESTED');
      default:
        return this.reviews().filter((r) => r.status === 'APPROVED');
    }
  }

  /** Loads the posts behind the newest approved reviews; edited-after-approval posts drop out (they need review again). */
  private async loadReady(orgId: string): Promise<void> {
    const approved = this.latest().filter((r) => r.status === 'APPROVED').slice(0, 25);
    const posts = await Promise.all(approved.map((r) => firstValueFrom(this.postsApi.get(orgId, r.postId)).catch(() => null)));
    this.ready.set(approved
      .map((review, i) => ({ review, post: posts[i] }))
      .filter((x): x is { review: ReviewSummary; post: Post } =>
        !!x.post && x.post.editorialState === 'APPROVED' && x.post.currentVersionId === x.review.postVersionId));
  }

  private async load(orgId: string): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.reviews.set(await firstValueFrom(this.api.list(orgId)));
      await this.loadReady(orgId);
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
