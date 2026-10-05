import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ReviewSummary } from '../approvals/models/review.model';
import { PublicationJob } from '../publications/models/publication.model';
import { SocialAccount } from '../social-accounts/models/social-account.model';

/** Workspace product overview. Editorial (post) and delivery (job) counts are separate; demo deliveries are never counted as published. */
export interface Dashboard {
  editorial: Partial<Record<string, number>>;
  delivery: Partial<Record<string, number>>;
  pendingReviewCount: number;
  pendingReviews: ReviewSummary[];
  upcoming: PublicationJob[];
  needsAttention: PublicationJob[];
  destinations: SocialAccount[];
}

export interface AuditEntry {
  id: string;
  actorUserId: string | null;
  actorName: string | null;
  action: string;
  resourceType: string;
  resourceId: string;
  metadata: Record<string, string | null>;
  createdAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class DashboardApiService {
  private readonly http = inject(HttpClient);

  get(orgId: string): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${environment.apiBaseUrl}/organizations/${orgId}/dashboard`);
  }

  /** Admin only (the API enforces it). */
  audit(orgId: string): Observable<AuditEntry[]> {
    return this.http.get<AuditEntry[]>(`${environment.apiBaseUrl}/organizations/${orgId}/audit`);
  }
}
