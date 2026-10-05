import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ifMatch } from '../../../core/http/api-problem';
import { BrandProfile, BrandProfileInput } from '../models/brand-profile.model';

@Injectable({ providedIn: 'root' })
export class BrandProfileApiService {
  private readonly http = inject(HttpClient);

  get(orgId: string): Observable<BrandProfile> {
    return this.http.get<BrandProfile>(this.url(orgId));
  }

  /** Pass the current rowVersion when updating; omit it only when creating the first profile. */
  save(orgId: string, input: BrandProfileInput, rowVersion: string | null): Observable<BrandProfile> {
    return this.http.put<BrandProfile>(this.url(orgId), input, rowVersion ? { headers: ifMatch(rowVersion) } : {});
  }

  private url(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}/brand-profile`;
  }
}
