import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { CampaignApiService } from './campaign-api.service';

describe('CampaignApiService', () => {
  let api: CampaignApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CampaignApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('scopes requests to the workspace and sends the row version as a quoted If-Match', async () => {
    const result = firstValueFrom(api.update('org-1', 'camp-1', { name: 'n', color: '#2563EB', description: null }, 'AAAAAAAAB9E='));

    const request = http.expectOne('/api/v1/organizations/org-1/campaigns/camp-1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"AAAAAAAAB9E="');
    request.flush({});
    await result;
  });

  it('archives through an explicit command endpoint, not a status patch', async () => {
    const result = firstValueFrom(api.archive('org-1', 'camp-1', 'rv'));

    const request = http.expectOne('/api/v1/organizations/org-1/campaigns/camp-1/archive');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"rv"');
    request.flush({});
    await result;
  });
});
