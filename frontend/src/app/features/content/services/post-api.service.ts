import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { SocialPlatform } from '../../../shared/models/platform.model';
import { Post, PostContent, PostListItem, PostVersion } from '../models/post.model';

@Injectable({ providedIn: 'root' })
export class PostApiService {
  private readonly http = inject(HttpClient);

  /** Every non-archived post in the workspace (Planner, Home). */
  listAll(orgId: string): Observable<PostListItem[]> {
    return this.http.get<PostListItem[]>(`${this.org(orgId)}/posts`);
  }

  /** Creates the post with Version 1. */
  /** groupWithPostId: this post is a platform copy of that post, so the two are reviewed and scheduled together. */
  create(orgId: string, platform: SocialPlatform, content: PostContent, groupWithPostId: string | null = null): Observable<Post> {
    return this.http.post<Post>(`${this.org(orgId)}/posts`, { platform, ...content, groupWithPostId });
  }

  /** "Delete draft": archives a DRAFT / CHANGES_REQUESTED post; its history is kept. */
  archive(orgId: string, post: Post): Observable<void> {
    return this.http.post<void>(`${this.org(orgId)}/posts/${post.id}/archive`, {}, { headers: ifMatch(post.rowVersion) });
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
