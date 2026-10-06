import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { SocialPlatform } from '../../campaigns/models/campaign.model';
import { Post, PostContent, PostListItem, PostVersion } from '../models/post.model';

@Injectable({ providedIn: 'root' })
export class PostApiService {
  private readonly http = inject(HttpClient);

  /** Every non-archived post in the workspace (Planner, Home), or only those of one campaign. */
  listAll(orgId: string, campaignId?: string): Observable<PostListItem[]> {
    return this.http.get<PostListItem[]>(`${this.org(orgId)}/posts`, campaignId ? { params: { campaignId } } : {});
  }

  /** Creates the post with Version 1, optionally filed in a campaign. */
  create(orgId: string, platform: SocialPlatform, campaignId: string | null, content: PostContent): Observable<Post> {
    return this.http.post<Post>(`${this.org(orgId)}/posts`, { platform, campaignId, ...content });
  }

  get(orgId: string, postId: string): Observable<Post> {
    return this.http.get<Post>(`${this.org(orgId)}/posts/${postId}`);
  }

  versions(orgId: string, postId: string): Observable<PostVersion[]> {
    return this.http.get<PostVersion[]>(`${this.org(orgId)}/posts/${postId}/versions`);
  }

  /** Saves an edit as a new immutable version; the post's rowVersion guards against overwriting someone else's edit. */
  saveNewVersion(orgId: string, postId: string, content: PostContent, rowVersion: string): Observable<Post> {
    return this.http.post<Post>(`${this.org(orgId)}/posts/${postId}/versions`, content, { headers: ifMatch(rowVersion) });
  }

  /** Files the post into a campaign (or none). Organizational only: no new version, approval unchanged. */
  moveToCampaign(orgId: string, postId: string, campaignId: string | null, rowVersion: string): Observable<Post> {
    return this.http.put<Post>(`${this.org(orgId)}/posts/${postId}/campaign`, { campaignId }, { headers: ifMatch(rowVersion) });
  }

  private org(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}`;
  }
}
