import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OrganizationRole } from '../auth/auth.models';
import { ifMatch } from '../http/api-problem';

export interface Organization {
  id: string;
  name: string;
  industry: string;
  defaultLanguage: string;
  timeZoneId: string;
  requireDistinctReviewer: boolean;
  status: 'Active' | 'Suspended';
  myRole: OrganizationRole;
  rowVersion: string;
}

export interface OrganizationSettings {
  name: string;
  industry: string;
  defaultLanguage: string;
  timeZoneId: string;
  requireDistinctReviewer: boolean;
}

export interface Member {
  memberId: string;
  userId: string;
  email: string;
  displayName: string;
  role: OrganizationRole;
  status: 'Active' | 'Removed' | string;
  rowVersion: string;
}

/** Workspace settings and team. Changes need the last seen row version (If-Match); the API enforces Admin rights. */
@Injectable({ providedIn: 'root' })
export class OrganizationApiService {
  private readonly http = inject(HttpClient);

  /** Workspace setup: the caller becomes its Admin. Language defaults to "en" on the server. */
  create(input: { name: string; industry: string; timeZoneId: string }): Observable<Organization> {
    return this.http.post<Organization>(`${environment.apiBaseUrl}/organizations`, input);
  }

  get(orgId: string): Observable<Organization> {
    return this.http.get<Organization>(this.url(orgId));
  }

  updateSettings(orgId: string, settings: OrganizationSettings, rowVersion: string): Observable<Organization> {
    return this.http.put<Organization>(`${this.url(orgId)}/settings`, settings, { headers: ifMatch(rowVersion) });
  }

  members(orgId: string): Observable<Member[]> {
    return this.http.get<Member[]>(`${this.url(orgId)}/members`);
  }

  changeRole(orgId: string, member: Member, role: OrganizationRole): Observable<Member> {
    return this.http.put<Member>(`${this.url(orgId)}/members/${member.memberId}/role`, { role }, { headers: ifMatch(member.rowVersion) });
  }

  /** Adds an existing account by email (no invitation email in the MVP). */
  addMember(orgId: string, email: string, role: OrganizationRole): Observable<Member> {
    return this.http.post<Member>(`${this.url(orgId)}/members`, { email, role });
  }

  /** Admin. Creates a new login with the Creator role; the Admin hands the credentials over (no invitation email). */
  createCreator(orgId: string, input: { fullName: string; email: string; password: string }): Observable<Member> {
    return this.http.post<Member>(`${this.url(orgId)}/members/creators`, input);
  }

  deactivate(orgId: string, member: Member): Observable<Member> {
    return this.http.post<Member>(`${this.url(orgId)}/members/${member.memberId}/deactivate`, {}, { headers: ifMatch(member.rowVersion) });
  }

  private url(orgId: string): string {
    return `${environment.apiBaseUrl}/organizations/${orgId}`;
  }
}
