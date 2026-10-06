import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { SocialPlatform } from '../../campaigns/models/campaign.model';
import { AiGenerationRequest, ContentEvaluation, GenerationInput, RewriteAction, isActive } from '../models/ai.model';

/** Durable AI requests: the API only enqueues; the AI worker does the work and the UI polls for the outcome. */
@Injectable({ providedIn: 'root' })
export class AiApiService {
  private readonly http = inject(HttpClient);

  /** AI Studio: queues generation; the worker writes one new draft post per platform (listed in `outputs` when done). */
  requestGeneration(orgId: string, input: GenerationInput): Observable<AiGenerationRequest> {
    return this.http.post<AiGenerationRequest>(`${this.org(orgId)}/ai-generations`, input);
  }

  get(orgId: string, requestId: string): Observable<AiGenerationRequest> {
    return this.http.get<AiGenerationRequest>(`${this.org(orgId)}/ai-generations/${requestId}`);
  }

  requestRewrite(orgId: string, postId: string, action: RewriteAction, customInstruction: string | null, postRowVersion: string): Observable<AiGenerationRequest> {
    return this.http.post<AiGenerationRequest>(`${this.org(orgId)}/posts/${postId}/rewrites`, { action, customInstruction }, { headers: ifMatch(postRowVersion) });
  }

  /** Queues an AI image for the post's current version; the image arrives as a new version that still needs review. */
  requestImage(orgId: string, postId: string, instruction: string | null, postRowVersion: string): Observable<AiGenerationRequest> {
    return this.http.post<AiGenerationRequest>(`${this.org(orgId)}/posts/${postId}/image-generations`, { instruction }, { headers: ifMatch(postRowVersion) });
  }

  /** Which AI actions the platform currently offers (so only working buttons are shown). */
  capabilities(orgId: string): Observable<{ textGeneration: boolean; imageGeneration: boolean }> {
    return this.http.get<{ textGeneration: boolean; imageGeneration: boolean }>(`${this.org(orgId)}/ai/capabilities`);
  }

  evaluations(orgId: string, postId: string): Observable<ContentEvaluation[]> {
    return this.http.get<ContentEvaluation[]>(`${this.org(orgId)}/posts/${postId}/evaluations`);
  }

  /** Polls until the request completes or fails, reporting each intermediate state. Stops when `cancelled()` turns true. */
  async waitForCompletion(
    orgId: string, request: AiGenerationRequest, onUpdate: (r: AiGenerationRequest) => void, cancelled: () => boolean, intervalMs = 2000,
  ): Promise<AiGenerationRequest> {
    let current = request;
    while (isActive(current) && !cancelled()) {
      await new Promise((resolve) => setTimeout(resolve, intervalMs));
      if (cancelled()) break;
      current = await firstValueFrom(this.get(orgId, current.id));
      onUpdate(current);
    }
    return current;
  }

  private org(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}`;
  }
}
