import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { Organization, OrganizationApiService } from './organization-api.service';

const STORAGE_KEY = 'sma.workspaceId';

/**
 * The workspace the user is currently working in. Only the selected workspace id is remembered locally
 * (a UX convenience, not a credential). The role-based flags below only hide or disable controls:
 * the API re-checks membership and permission on every request.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  private readonly auth = inject(AuthService);
  private readonly selectedId = signal<string | null>(readStoredId());

  readonly memberships = computed(() => this.auth.me()?.memberships ?? []);
  readonly current = computed(() => {
    const memberships = this.memberships();
    return memberships.find((m) => m.organizationId === this.selectedId()) ?? memberships.at(0) ?? null;
  });
  readonly orgId = computed(() => this.current()?.organizationId ?? null);
  readonly canManageCampaigns = computed(() => this.current()?.role === 'Admin' || this.current()?.role === 'Creator');
  readonly canManageBrand = computed(() => this.current()?.role === 'Admin');
  readonly isAdmin = computed(() => this.current()?.role === 'Admin');
  readonly canReview = computed(() => this.current()?.role === 'Admin' || this.current()?.role === 'Reviewer');

  /** Settings of the current workspace (language, time zone, reviewer policy); loaded when the workspace changes. */
  readonly organization = signal<Organization | null>(null);
  readonly timeZone = computed(() => this.organization()?.timeZoneId ?? 'UTC');

  private readonly organizations = inject(OrganizationApiService);

  constructor() {
    effect(() => {
      const orgId = this.orgId();
      this.organization.set(null);
      if (orgId) void this.refreshOrganization(orgId);
    });
  }

  async refreshOrganization(orgId = this.orgId()): Promise<void> {
    if (!orgId) return;
    try {
      const organization = await firstValueFrom(this.organizations.get(orgId));
      if (orgId === this.orgId()) this.organization.set(organization);
    } catch {
      // Pages that need the settings show their own errors; the shell keeps working with UTC.
    }
  }

  select(organizationId: string): void {
    this.selectedId.set(organizationId);
    try {
      localStorage.setItem(STORAGE_KEY, organizationId);
    } catch {
      // Storage may be unavailable (private mode); the selection still works for this session.
    }
  }
}

function readStoredId(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
