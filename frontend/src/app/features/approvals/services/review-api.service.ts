import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { ReviewComment, ReviewDetail, ReviewStatus, ReviewSummary } from '../models/review.model';

/** Explicit review commands; every decision carries the review's row version so a stale screen cannot decide. */
@Injectable({ providedIn: 'root' })
export class ReviewApiService {
  private readonly http = inject(HttpClient);

  /** Submits the post's current version; `reviewerUserId` routes it to one team reviewer (any reviewer when null). */
  submit(orgId: string, postId: string, note: string | null, reviewerUserId: string | null, postRowVersion: string): Observable<ReviewSummary> {
    return this.http.post<ReviewSummary>(`${this.org(orgId)}/posts/${postId}/submit-review`, { note, reviewerUserId }, { headers: ifMatch(postRowVersion) });
  }

  list(orgId: string, filter: { status?: ReviewStatus; postId?: string } = {}): Observable<ReviewSummary[]> {
    const params: Record<string, string> = {};
    if (filter.status) params['status'] = filter.status;
    if (filter.postId) params['postId'] = filter.postId;
    return this.http.get<ReviewSummary[]>(`${this.org(orgId)}/review-requests`, { params });
  }

  get(orgId: string, reviewId: string): Observable<ReviewDetail> {
    return this.http.get<ReviewDetail>(`${this.org(orgId)}/review-requests/${reviewId}`);
  }

  approve(orgId: string, review: ReviewSummary): Observable<ReviewSummary> {
    return this.http.post<ReviewSummary>(`${this.org(orgId)}/review-requests/${review.id}/approve`, {}, { headers: ifMatch(review.rowVersion) });
  }

  requestChanges(orgId: string, review: ReviewSummary, explanation: string): Observable<ReviewSummary> {
    return this.http.post<ReviewSummary>(`${this.org(orgId)}/review-requests/${review.id}/request-changes`, { explanation }, { headers: ifMatch(review.rowVersion) });
  }

  comment(orgId: string, reviewId: string, body: string): Observable<ReviewComment> {
    return this.http.post<ReviewComment>(`${this.org(orgId)}/review-requests/${reviewId}/comments`, { body });
  }

  private org(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}`;
  }
}
