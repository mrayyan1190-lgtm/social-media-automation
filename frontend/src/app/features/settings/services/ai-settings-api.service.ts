import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { AiProvider, AiSettings, SaveAiSettings } from '../models/ai-settings.model';

@Injectable({ providedIn: 'root' })
export class AiSettingsApiService {
  private readonly http = inject(HttpClient);

  get(orgId: string): Observable<AiSettings> {
    return this.http.get<AiSettings>(this.url(orgId));
  }

  save(orgId: string, input: SaveAiSettings, rowVersion: string | null): Observable<AiSettings> {
    return this.http.put<AiSettings>(this.url(orgId), input, rowVersion ? { headers: ifMatch(rowVersion) } : {});
  }

  /** POST so a freshly typed key travels in the body, never in a URL. Omit the key to use the stored one. */
  listModels(orgId: string, provider: AiProvider, baseUrl: string | null, apiKey: string | null): Observable<string[]> {
    return this.http.post<string[]>(`${this.url(orgId)}/models`, { provider, baseUrl, apiKey });
  }

  private url(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/ai-settings`;
  }
}
