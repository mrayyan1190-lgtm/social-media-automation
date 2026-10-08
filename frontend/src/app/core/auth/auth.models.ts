export type OrganizationRole = 'Admin' | 'Creator' | 'Reviewer';

export interface Membership {
  organizationId: string;
  organizationName: string;
  role: OrganizationRole;
}

export type OnboardingStep = 'WORKSPACE' | 'PROFILE' | 'BRAND_PROFILE' | 'SOCIAL_CONNECTIONS' | 'COMPLETE';

/** Decided by the API so the UI never guesses where the user should go next. */
export interface OnboardingStatus {
  organizationId: string | null;
  accountCreated: boolean;
  workspaceCreated: boolean;
  profileCompleted: boolean;
  brandProfileCompleted: boolean;
  socialAccountsConnected: boolean;
  canCreateContent: boolean;
  nextStep: OnboardingStep;
}

export interface Me {
  userId: string;
  email: string;
  displayName: string;
  memberships: Membership[];
  onboarding: OnboardingStatus;
  /** Internal platform administrator (not a workspace role). UX only: internal endpoints re-check on the server. */
  isSystemAdmin: boolean;
}

/** Sign-up creates the account and its workspace; name and time zone come on the profile step. */
export interface RegisterRequest {
  email: string;
  password: string;
  workspaceName: string;
}

export interface CompleteProfileRequest {
  organizationId: string | null;
  fullName: string;
  timeZoneId: string;
}

/** GET /api/internal/me: the signed-in internal system administrator (platform level, never a workspace role). */
export interface SystemAdminMe {
  id: string;
  fullName: string;
  email: string;
  systemRole: 'SystemAdmin';
}
