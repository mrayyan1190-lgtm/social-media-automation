import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiProblem, toApiProblem } from '../../core/http/api-problem';
import { WorkspaceService } from '../../core/services/workspace.service';
import { IconComponent } from '../../shared/components/icon.component';
import { PlatformIconComponent } from '../../shared/components/platform-icon.component';
import { ProblemBannerComponent } from '../../shared/components/problem-banner.component';
import { ReviewSummary } from '../approvals/models/review.model';
import { ReviewApiService } from '../approvals/services/review-api.service';
import { formatInZone } from '../publications/models/time-zones';

type Tab = 'pending' | 'mine' | 'completed' | 'changes';

const STATUS_TEXT: Record<string, { label: string; tone: string }> = {
  PENDING: { label: 'In Review', tone: 'waiting' },
  APPROVED: { label: 'Approved', tone: 'success' },
  CHANGES_REQUESTED: { label: 'Changes Requested', tone: 'danger' },
};

/**
 * Approvals: role-aware. Reviewers start on what waits for them; Creators on what they submitted. Each review covers
 * one exact version; the decision page is where Approve / Request Changes happen.
 */
@Component({
  selector: 'app-approvals-page',
  imports: [RouterLink, IconComponent, PlatformIconComponent, ProblemBannerComponent],
  template: `
    <div class="page-header">
      <div>
        <h1>Approvals</h1>
        <p class="muted">Every review covers one exact version. Approving it approves exactly that content and destination.</p>
      </div>
    </div>
    <nav class="tabs" aria-label="Approvals">
      @if (workspace.canReview()) {
        <button type="button" [class.active]="tab() === 'pending'" (click)="tab.set('pending')">Pending My Review <span class="count">{{ count('pending') }}</span></button>
      }
      <button type="button" [class.active]="tab() === 'mine'" (click)="tab.set('mine')">Submitted by Me <span class="count">{{ count('mine') }}</span></button>
      <button type="button" [class.active]="tab() === 'completed'" (click)="tab.set('completed')">Completed</button>
      <button type="button" [class.active]="tab() === 'changes'" (click)="tab.set('changes')">Changes Requested</button>
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
              <a [routerLink]="['/publisher/approvals', r.id]"><strong>{{ r.campaignName }}</strong> · {{ r.platform }} · Version {{ r.versionNumber }}</a>
              <span class="hint">
                Submitted by {{ r.submittedByName }} · {{ time(r.submittedAtUtc) }}
                @if (r.decidedByName) { · {{ statusText[r.status].label }} by {{ r.decidedByName }} }
              </span>
              @if (r.decisionComment) { <span class="hint quote">“{{ r.decisionComment }}”</span> }
            </div>
            <span [class]="'badge delivery ' + statusText[r.status].tone">{{ statusText[r.status].label }}</span>
            @if (tab() === 'pending') {
              <a class="button primary" [routerLink]="['/publisher/approvals', r.id]">Review</a>
            } @else if (r.status === 'CHANGES_REQUESTED' && workspace.canManageCampaigns()) {
              <a class="button" [routerLink]="['/posts', r.postId]">Edit</a>
            }
          </li>
        } @empty {
          <li class="empty-row">
            <app-icon name="checkCircle" [size]="22" />
            {{ emptyText() }}
          </li>
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
  protected readonly shown = computed(() => this.select(this.tab()));
  protected readonly emptyText = computed(() => ({
    pending: 'Nothing is waiting for review.',
    mine: 'You have not submitted anything for review yet.',
    completed: 'No approved content yet.',
    changes: 'No change requests.',
  })[this.tab()]);

  constructor() {
    effect(() => {
      const orgId = this.workspace.orgId();
      if (orgId) void this.load(orgId);
    });
  }

  protected count(tab: Tab): number {
    return this.select(tab).filter((r) => r.status === 'PENDING').length;
  }

  protected time(utc: string): string {
    return formatInZone(utc, this.workspace.timeZone());
  }

  private select(tab: Tab): ReviewSummary[] {
    const all = this.reviews();
    switch (tab) {
      // Oldest first, so the longest-waiting review is on top. Own submissions are never "mine to review".
      case 'pending':
        return all.filter((r) => r.status === 'PENDING' && r.submittedByUserId !== this.me()).reverse();
      case 'mine':
        return all.filter((r) => r.submittedByUserId === this.me());
      case 'completed':
        return all.filter((r) => r.status === 'APPROVED');
      default:
        return all.filter((r) => r.status === 'CHANGES_REQUESTED');
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
