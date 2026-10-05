import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { SocialPlatform } from '../../campaigns/models/campaign.model';
import { Post, PostContent, PostListItem, PostSummary, PostVersion } from '../models/post.model';

@Injectable({ providedIn: 'root' })
export class PostApiService {
  private readonly http = inject(HttpClient);

  /** Every non-archived post in the workspace (Publisher, Home). */
  listAll(orgId: string): Observable<PostListItem[]> {
    return this.http.get<PostListItem[]>(`${this.org(orgId)}/posts`);
  }

  listForCampaign(orgId: string, campaignId: string): Observable<PostSummary[]> {
    return this.http.get<PostSummary[]>(`${this.org(orgId)}/campaigns/${campaignId}/posts`);
  }

  create(orgId: string, campaignId: string, platform: SocialPlatform, content: PostContent): Observable<Post> {
    return this.http.post<Post>(`${this.org(orgId)}/campaigns/${campaignId}/posts`, { platform, ...content });
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

  private org(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}`;
  }
}
