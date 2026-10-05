import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { PublicationJob } from '../models/publication.model';
import { PublicationApiService } from './publication-api.service';

describe('PublicationApiService', () => {
  let http: HttpTestingController;
  const when = { localDate: '2027-11-07', localTime: '01:30:00', timeZone: 'America/New_York', utcOffsetMinutes: -300 };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('schedules the exact version with local date, time and IANA zone under the post ETag', async () => {
    const result = firstValueFrom(TestBed.inject(PublicationApiService).schedule('org-1', 'post-1', 'v-2', when, 'rv=='));

    const request = http.expectOne('/api/v1/organizations/org-1/posts/post-1/schedule');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"rv=="');
    expect(request.request.body).toEqual({ postVersionId: 'v-2', ...when });
    request.flush({});
    await result;
  });

  it('publishes now by creating a job through the API, and cancels with the job ETag', async () => {
    const api = TestBed.inject(PublicationApiService);
    const now = firstValueFrom(api.publishNow('org-1', 'post-1', 'v-2', 'rv=='));
    const publish = http.expectOne('/api/v1/organizations/org-1/posts/post-1/publish-now');
    expect(publish.request.body).toEqual({ postVersionId: 'v-2' });
    publish.flush({});
    await now;

    const cancel = firstValueFrom(api.cancel('org-1', { id: 'job-1', rowVersion: 'jr==' } as PublicationJob));
    const request = http.expectOne('/api/v1/organizations/org-1/publication-jobs/job-1/cancel');
    expect(request.request.headers.get('If-Match')).toBe('"jr=="');
    request.flush({});
    await cancel;
  });

  it('sends calendar filters as query parameters, repeating state', async () => {
    const result = firstValueFrom(TestBed.inject(PublicationApiService).list('org-1', {
      from: '2027-01-01T00:00:00.000Z', to: '2027-01-08T00:00:00.000Z', platform: 'Instagram', states: ['FAILED', 'ACTION_REQUIRED'],
    }));
    const request = http.expectOne((r) => r.url === '/api/v1/organizations/org-1/publication-jobs');
    expect(request.request.params.get('from')).toBe('2027-01-01T00:00:00.000Z');
    expect(request.request.params.get('platform')).toBe('Instagram');
    expect(request.request.params.getAll('state')).toEqual(['FAILED', 'ACTION_REQUIRED']);
    request.flush([]);
    await result;
  });
});
