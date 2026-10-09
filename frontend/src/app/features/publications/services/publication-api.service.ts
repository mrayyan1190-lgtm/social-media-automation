import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { LocalSchedule, PublicationAttempt, PublicationJob } from '../models/publication.model';

/** Schedule and Publish Now only create a durable job on the server; the publishing worker does the rest. */
@Injectable({ providedIn: 'root' })
export class PublicationApiService {
  private readonly http = inject(HttpClient);

  schedule(orgId: string, postId: string, postVersionId: string, when: LocalSchedule, postRowVersion: string): Observable<PublicationJob> {
    return this.http.post<PublicationJob>(`${this.org(orgId)}/posts/${postId}/schedule`, { postVersionId, ...when }, { headers: ifMatch(postRowVersion) });
  }

  publishNow(orgId: string, postId: string, postVersionId: string, postRowVersion: string): Observable<PublicationJob> {
    return this.http.post<PublicationJob>(`${this.org(orgId)}/posts/${postId}/publish-now`, { postVersionId }, { headers: ifMatch(postRowVersion) });
  }

  /** Calendar filters: from/to are UTC instants on the publish time; states may repeat. */
  list(
    orgId: string,
    options: { postId?: string; openOnly?: boolean; from?: string; to?: string; platform?: string; states?: string[] } = {},
  ): Observable<PublicationJob[]> {
    const params: Record<string, string | string[]> = {};
    if (options.postId) params['postId'] = options.postId;
    if (options.openOnly) params['openOnly'] = 'true';
    if (options.from) params['from'] = options.from;
    if (options.to) params['to'] = options.to;
    if (options.platform) params['platform'] = options.platform;
    if (options.states?.length) params['state'] = options.states;
    return this.http.get<PublicationJob[]>(`${this.org(orgId)}/publication-jobs`, { params });
  }

  get(orgId: string, jobId: string): Observable<PublicationJob> {
    return this.http.get<PublicationJob>(`${this.org(orgId)}/publication-jobs/${jobId}`);
  }

  attempts(orgId: string, jobId: string): Observable<PublicationAttempt[]> {
    return this.http.get<PublicationAttempt[]>(`${this.org(orgId)}/publication-jobs/${jobId}/attempts`);
  }

  cancel(orgId: string, job: PublicationJob): Observable<PublicationJob> {
    return this.http.post<PublicationJob>(`${this.org(orgId)}/publication-jobs/${job.id}/cancel`, {}, { headers: ifMatch(job.rowVersion) });
  }

  reschedule(orgId: string, job: PublicationJob, when: LocalSchedule): Observable<PublicationJob> {
    return this.http.post<PublicationJob>(`${this.org(orgId)}/publication-jobs/${job.id}/reschedule`, when, { headers: ifMatch(job.rowVersion) });
  }

  private org(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}`;
  }
}
