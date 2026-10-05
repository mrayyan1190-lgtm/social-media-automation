import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { OnboardingStep } from '../auth/auth.models';
import { Me } from '../auth/auth.models';
import { AuthService, ONBOARDING_ROUTES } from '../auth/auth.service';
import { InternalAuthService } from '../auth/internal-auth.service';
import { WorkspaceService } from '../services/workspace.service';

/**
 * App shell: signed-in users who have finished onboarding. Unauthenticated users go to login; users with an
 * unfinished onboarding step go to that step (query parameters such as an OAuth result are kept).
 * UX only: the API enforces authentication and workspace access itself.
 */
export const authGuard: CanActivateFn = async (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  try {
    const me = auth.me() ?? (await auth.loadMe());
    if (me) {
      if (isOperatorOnly(me)) return router.createUrlTree(['/internal']);
      const step = me.onboarding.nextStep;
      return step === 'COMPLETE' ? true : router.createUrlTree([ONBOARDING_ROUTES[step]], { queryParams: route.queryParams });
    }
  } catch {
    // API unreachable: fall through to the login page, which reports the error on submit.
  }
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Onboarding pages: only the step the API says is next can be opened, so a refresh or a typed URL always lands on the
 * right step, and a finished user goes to the dashboard. The step is read fresh because it changes as steps complete.
 */
export function onboardingGuard(step: OnboardingStep): CanActivateFn {
  return async () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    try {
      const me = await auth.loadMe();
      if (me) {
        if (isOperatorOnly(me)) return router.createUrlTree(['/internal']);
        return me.onboarding.nextStep === step ? true : router.createUrlTree([ONBOARDING_ROUTES[me.onboarding.nextStep]]);
      }
    } catch {
      // API unreachable: fall through to the login page.
    }
    return router.createUrlTree(['/login']);
  };
}

/** Content creation pages (AI Studio): Admins and Creators. UX only; the API enforces every permission. */
export const contentCreatorGuard: CanActivateFn = () =>
  inject(WorkspaceService).canManageCampaigns() ? true : inject(Router).createUrlTree(['/access-denied']);

/** Customer settings: workspace Admins of the current workspace. UX only; the API enforces every permission. */
export const workspaceAdminGuard: CanActivateFn = () =>
  inject(WorkspaceService).isAdmin() ? true : inject(Router).createUrlTree(['/access-denied']);

/**
 * A system administrator with no workspace membership has no customer app: send them to the internal area instead of
 * workspace onboarding (customer workspace access is never granted implicitly).
 */
function isOperatorOnly(me: Me): boolean {
  return me.isSystemAdmin && me.memberships.length === 0;
}

/**
 * Internal platform area: requires the internal sign-in (GET /api/internal/me succeeds only for a system administrator).
 * Not signed in -> /internal/login; signed in as a customer (including a workspace Admin) -> /access-denied, and no
 * internal data is ever requested. UX only: every /api/internal endpoint enforces RequireSystemAdmin on the server.
 */
export const systemAdminGuard: CanActivateFn = async () => {
  const internalAuth = inject(InternalAuthService);
  const auth = inject(AuthService);
  const router = inject(Router);
  try {
    if (await internalAuth.loadMe()) return true; // always fresh: the session may have changed since
    // Not an internal session. A customer is denied; a system admin who used the customer sign-in must sign in here.
    const me = await auth.loadMe();
    return me && !me.isSystemAdmin ? router.createUrlTree(['/access-denied']) : router.createUrlTree(['/internal/login']);
  } catch {
    return router.createUrlTree(['/internal/login']);
  }
};
