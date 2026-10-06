import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { Campaign, CampaignInput, CampaignSummary } from '../models/campaign.model';

/** Campaign folders. Every URL is scoped to the workspace; the API verifies membership and ownership. */
@Injectable({ providedIn: 'root' })
export class CampaignApiService {
  private readonly http = inject(HttpClient);

  list(orgId: string, includeArchived = false): Observable<CampaignSummary[]> {
    return this.http.get<CampaignSummary[]>(this.campaigns(orgId), { params: { includeArchived } });
  }

  get(orgId: string, campaignId: string): Observable<Campaign> {
    return this.http.get<Campaign>(`${this.campaigns(orgId)}/${campaignId}`);
  }

  create(orgId: string, input: CampaignInput): Observable<Campaign> {
    return this.http.post<Campaign>(this.campaigns(orgId), input);
  }

  update(orgId: string, campaignId: string, input: CampaignInput, rowVersion: string): Observable<Campaign> {
    return this.http.put<Campaign>(`${this.campaigns(orgId)}/${campaignId}`, input, { headers: ifMatch(rowVersion) });
  }

  archive(orgId: string, campaignId: string, rowVersion: string): Observable<Campaign> {
    return this.http.post<Campaign>(`${this.campaigns(orgId)}/${campaignId}/archive`, null, { headers: ifMatch(rowVersion) });
  }

  private campaigns(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/campaigns`;
  }
}
