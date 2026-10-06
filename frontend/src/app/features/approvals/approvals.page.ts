import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { formatInZone } from '../publications/models/time-zones';
import { ReviewSummary } from './models/review.model';
import { ReviewApiService } from './services/review-api.service';

type Tab = 'pending' | 'mine' | 'changes' | 'completed';

const STATUS_TEXT: Record<string, { label: string; tone: string }> = {
  PENDING: { label: 'In Review', tone: 'waiting' },
  APPROVED: { label: 'Approved', tone: 'success' },
  CHANGES_REQUESTED: { label: 'Changes Requested', tone: 'danger' },
};

/** Human review. Each review covers one exact version; Approve / Request Changes happen on the review page. */
@Component({
  selector: 'app-approvals-page',
  imports: [RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Approvals</h1>
        <p class="muted">A person approves every post before it can be scheduled or published. Approval covers one exact version.</p>
      </div>
    </div>
    <nav class="tabs" aria-label="Approvals">
      @if (workspace.canReview()) {
        <button type="button" [class.active]="tab() === 'pending'" (click)="tab.set('pending')">Pending My Review @if (count('pending')) { <span class="count">{{ count('pending') }}</span> }</button>
      }
      <button type="button" [class.active]="tab() === 'mine'" (click)="tab.set('mine')">Submitted by Me</button>
      <button type="button" [class.active]="tab() === 'changes'" (click)="tab.set('changes')">Changes Requested @if (count('changes')) { <span class="count">{{ count('changes') }}</span> }</button>
      <button type="button" [class.active]="tab() === 'completed'" (click)="tab.set('completed')">Completed</button>
    </nav>
    <app-problem-banner [problem]="problem()" />

    @if (loading()) {
      <p class="muted">Loading…</p>
    } @else {
      <ul class="review-list">
        @for (r of shown(); track r.id) {
          <li>
            <app-platform-icon [platform]="r.platform" />
            <div class="grow">
              <a [routerLink]="['/approvals', r.id]" class="ellipsis-line"><strong>{{ r.excerpt }}</strong></a>
              <span class="hint">
                @if (r.campaignName) { <span class="color-dot sm" [style.background]="r.campaignColor"></span>{{ r.campaignName }} · }
                Version {{ r.versionNumber }} · by {{ r.submittedByName }} · {{ time(r.submittedAtUtc) }}
                @if (r.assignedReviewerName) { · Reviewer: {{ r.assignedReviewerName }} }
              </span>
              @if (r.decisionComment) { <span class="hint quote">“{{ r.decisionComment }}”</span> }
            </div>
            <span [class]="'badge delivery ' + statusText[r.status].tone">{{ statusText[r.status].label }}</span>
            @if (tab() === 'pending') {
              <a class="button primary small" [routerLink]="['/approvals', r.id]">Review</a>
            } @else if (r.status === 'CHANGES_REQUESTED' && workspace.canManageCampaigns()) {
              <a class="button small" [routerLink]="['/composer', r.postId]">Edit</a>
            } @else {
              <a class="button small" [routerLink]="['/approvals', r.id]">Open</a>
            }
          </li>
        } @empty {
          <li class="empty-row"><app-icon name="checkCircle" [size]="22" />{{ emptyText() }}</li>
        }
      </ul>
    }
  `,
})
export class ApprovalsPage {
  private readonly api = inject(ReviewApiService);
  private readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);

  protected readonly statusText = STATUS_TEXT;
  protected readonly tab = signal<Tab>(this.workspace.canReview() ? 'pending' : 'mine');
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
    completed: 'No approved posts yet.',
  })[this.tab()]);

  constructor() {
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
      // Assigned to me, or to nobody in particular (then not my own submissions). Oldest first.
      case 'pending':
        return this.reviews()
          .filter((r) => r.status === 'PENDING' && (r.assignedReviewerUserId === me || (!r.assignedReviewerUserId && r.submittedByUserId !== me)))
          .reverse();
      case 'mine':
        return this.reviews().filter((r) => r.submittedByUserId === me);
      case 'changes':
        return this.latest().filter((r) => r.status === 'CHANGES_REQUESTED');
      default:
        return this.reviews().filter((r) => r.status === 'APPROVED');
    }
  }

  private async load(orgId: string): Promise<void> {
    this.loading.set(true);
    this.problem.set(null);
    try {
      this.reviews.set(await firstValueFrom(this.api.list(orgId)));
    } catch (error) {
      this.problem.set(toApiProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}
