import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AiSettingsApiService } from '../../settings/services/ai-settings-api.service';
import { AiGenerationRequest } from '../models/ai.model';
import { AiApiService } from './ai-api.service';

const request = (status: AiGenerationRequest['status']): AiGenerationRequest => ({
  id: 'req-1', campaignId: 'c-1', kind: 'Generation', platforms: ['LinkedIn'], postId: null, sourceVersionId: null, rewriteAction: null,
  status, attemptCount: 1, maxAttempts: 3, failureCode: null, failureMessage: null, provider: 'OpenAI', model: 'm',
  createdAtUtc: '', completedAtUtc: null, outputs: [],
});

describe('AiApiService', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests a rewrite of the exact version the user sees (If-Match)', async () => {
    const result = firstValueFrom(TestBed.inject(AiApiService).requestRewrite('org-1', 'post-1', 'Custom', 'Mention the webinar', 'rv=='));

    const req = http.expectOne('/api/v1/organizations/org-1/posts/post-1/rewrites');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"rv=="');
    expect(req.request.body).toEqual({ action: 'Custom', customInstruction: 'Mention the webinar' });
    req.flush(request('PENDING'));
    await result;
  });

  it('polls a durable request until it reaches a final state', async () => {
    const seen: string[] = [];
    const done = TestBed.inject(AiApiService).waitForCompletion('org-1', request('PENDING'), (r) => seen.push(r.status), () => false, 1);

    for (const status of ['PROCESSING', 'COMPLETED'] as const) {
      await new Promise((resolve) => setTimeout(resolve, 5));
      http.expectOne('/api/v1/organizations/org-1/ai-generations/req-1').flush(request(status));
    }

    expect((await done).status).toBe('COMPLETED');
    expect(seen).toEqual(['PROCESSING', 'COMPLETED']);
  });

  it('sends a typed API key only in a POST body when listing models', async () => {
    const result = firstValueFrom(TestBed.inject(AiSettingsApiService).listModels('org-1', 'Groq', null, 'gsk_abc'));

    const req = http.expectOne('/api/v1/organizations/org-1/ai-settings/models');
    expect(req.request.method).toBe('POST');
    expect(req.request.urlWithParams).not.toContain('gsk_abc');
    expect(req.request.body).toEqual({ provider: 'Groq', baseUrl: null, apiKey: 'gsk_abc' });
    req.flush(['llama']);
    expect(await result).toEqual(['llama']);
  });
});
