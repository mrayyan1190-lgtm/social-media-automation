import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Membership } from '../auth/auth.models';
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
  /** URL segment of the current workspace: /{slug}/home. */
  readonly slug = computed(() => {
    const current = this.current();
    return current ? workspaceSlug(current, this.memberships()) : null;
  });
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

/**
 * URL-friendly workspace name ("Centaiva Inc" -> "centaiva-inc"). Only resolved among the user's own memberships, so it
 * needs no global uniqueness; two memberships with the same name get an id suffix. Not an authorization input: the API
 * still checks membership of the organization id on every request.
 */
export function workspaceSlug(membership: Membership, memberships: readonly Membership[]): string {
  const base = toSlug(membership.organizationName) || membership.organizationId.slice(0, 8);
  const clash = memberships.some((m) => m.organizationId !== membership.organizationId && toSlug(m.organizationName) === base);
  return clash ? `${base}-${membership.organizationId.slice(0, 8)}` : base;
}

function toSlug(name: string): string {
  return name.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

function readStoredId(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
