import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { PostContent } from '../models/post.model';
import { MediaApiService } from './media-api.service';
import { PostApiService } from './post-api.service';

describe('PostApiService / MediaApiService', () => {
  let http: HttpTestingController;
  const content: PostContent = {
    hook: null, body: 'b', callToAction: null, destinationUrl: null, hashtags: [], images: [{ mediaAssetId: 'm-1', altText: 'alt' }], socialAccountId: 'acct-1',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('saves an edit by POSTing a new version with If-Match, never by updating a version', async () => {
    const result = firstValueFrom(TestBed.inject(PostApiService).saveNewVersion('org-1', 'post-1', content, 'rv=='));

    const request = http.expectOne('/api/v1/organizations/org-1/posts/post-1/versions');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"rv=="');
    expect(request.request.body).toEqual(content);
    request.flush({});
    await result;
  });

  it('uploads media as multipart form data to the workspace', async () => {
    const file = new File([new Uint8Array([0xff, 0xd8, 0xff])], 'photo.jpg', { type: 'image/jpeg' });
    const result = firstValueFrom(TestBed.inject(MediaApiService).upload('org-1', file));

    const request = http.expectOne('/api/v1/organizations/org-1/media');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBe(true);
    expect((request.request.body as FormData).get('file')).toBeTruthy();
    request.flush({});
    await result;
  });
});
