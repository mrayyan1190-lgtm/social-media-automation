import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ReviewSummary } from '../models/review.model';
import { ReviewApiService } from './review-api.service';

describe('ReviewApiService', () => {
  let http: HttpTestingController;
  const review = { id: 'r-1', rowVersion: 'rv==' } as ReviewSummary;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('approves through the explicit command with the review row version', async () => {
    const result = firstValueFrom(TestBed.inject(ReviewApiService).approve('org-1', review));

    const req = http.expectOne('/api/v1/organizations/org-1/review-requests/r-1/approve');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"rv=="');
    req.flush({});
    await result;
  });

  it('sends the mandatory explanation when requesting changes', async () => {
    const result = firstValueFrom(TestBed.inject(ReviewApiService).requestChanges('org-1', review, 'Remove the 50% claim'));

    const req = http.expectOne('/api/v1/organizations/org-1/review-requests/r-1/request-changes');
    expect(req.request.body).toEqual({ explanation: 'Remove the 50% claim' });
    expect(req.request.headers.get('If-Match')).toBe('"rv=="');
    req.flush({});
    await result;
  });

  it('submits the post version the user is looking at', async () => {
    const result = firstValueFrom(TestBed.inject(ReviewApiService).submit('org-1', 'p-1', 'Ready', 'post-rv'));

    const req = http.expectOne('/api/v1/organizations/org-1/posts/p-1/submit-review');
    expect(req.request.headers.get('If-Match')).toBe('"post-rv"');
    req.flush({});
    await result;
  });
});
