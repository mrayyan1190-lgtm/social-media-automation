import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { MediaAsset, MediaLibraryItem } from '../models/post.model';

@Injectable({ providedIn: 'root' })
export class MediaApiService {
  private readonly http = inject(HttpClient);

  /** The server decides the real type from the bytes; the browser's file type is only a hint. */
  upload(orgId: string, file: File): Observable<MediaAsset> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<MediaAsset>(`${this.media(orgId)}`, form);
  }

  list(orgId: string): Observable<MediaLibraryItem[]> {
    return this.http.get<MediaLibraryItem[]>(this.media(orgId));
  }

  /** Same-origin preview URL served behind the session cookie. Not a media identity and never stored. */
  previewUrl(orgId: string, mediaAssetId: string): string {
    return `${this.media(orgId)}/${mediaAssetId}/content`;
  }

  private media(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/media`;
  }
}
