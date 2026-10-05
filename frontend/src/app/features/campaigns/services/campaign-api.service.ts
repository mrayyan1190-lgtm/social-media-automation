import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { Campaign, CampaignBrief, CampaignFact, CampaignFactInput, CampaignInput, CampaignSummary, SocialPlatform } from '../models/campaign.model';

/** Campaigns and their facts. Every URL is scoped to the workspace; the API verifies membership and ownership. */
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

  setPlatforms(orgId: string, campaignId: string, platforms: SocialPlatform[], rowVersion: string): Observable<Campaign> {
    return this.http.put<Campaign>(`${this.campaigns(orgId)}/${campaignId}/platforms`, { platforms }, { headers: ifMatch(rowVersion) });
  }

  /** The resolved brief and generation readiness. */
  review(orgId: string, campaignId: string): Observable<CampaignBrief> {
    return this.http.get<CampaignBrief>(`${this.campaigns(orgId)}/${campaignId}/review`);
  }

  archive(orgId: string, campaignId: string, rowVersion: string): Observable<Campaign> {
    return this.http.post<Campaign>(`${this.campaigns(orgId)}/${campaignId}/archive`, null, { headers: ifMatch(rowVersion) });
  }

  addFact(orgId: string, campaignId: string, input: CampaignFactInput): Observable<CampaignFact> {
    return this.http.post<CampaignFact>(this.facts(orgId, campaignId), input);
  }

  updateFact(orgId: string, campaignId: string, factId: string, input: CampaignFactInput, rowVersion: string): Observable<CampaignFact> {
    return this.http.put<CampaignFact>(`${this.facts(orgId, campaignId)}/${factId}`, input, { headers: ifMatch(rowVersion) });
  }

  removeFact(orgId: string, campaignId: string, factId: string, rowVersion: string): Observable<void> {
    return this.http.delete<void>(`${this.facts(orgId, campaignId)}/${factId}`, { headers: ifMatch(rowVersion) });
  }

  private campaigns(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/campaigns`;
  }

  private facts(orgId: string, campaignId: string): string {
    return `${this.campaigns(orgId)}/${campaignId}/facts`;
  }
}
