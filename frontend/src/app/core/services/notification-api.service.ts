import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AppNotification {
  id: string;
  type: string;
  title: string;
  message: string;
  resourceType: 'ReviewRequest' | 'Post' | string;
  resourceId: string;
  createdAtUtc: string;
  readAtUtc: string | null;
}

/** The signed-in user's notifications in the current workspace (the API never returns anyone else's). */
@Injectable({ providedIn: 'root' })
export class NotificationApiService {
  private readonly http = inject(HttpClient);

  list(orgId: string): Observable<AppNotification[]> {
    return this.http.get<AppNotification[]>(this.url(orgId));
  }

  unreadCount(orgId: string): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.url(orgId)}/unread-count`);
  }

  markRead(orgId: string, id: string): Observable<void> {
    return this.http.post<void>(`${this.url(orgId)}/${id}/read`, null);
  }

  markAllRead(orgId: string): Observable<void> {
    return this.http.post<void>(`${this.url(orgId)}/read-all`, null);
  }

  private url(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/notifications`;
  }
}
