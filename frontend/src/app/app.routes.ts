import { Routes } from '@angular/router';
import { LoginPageComponent } from './core/auth/login-page.component';
import { RegisterPageComponent } from './core/auth/register-page.component';
import { authGuard, contentCreatorGuard, onboardingGuard, systemAdminGuard, workspaceAdminGuard, workspaceMatchGuard, workspaceRedirectGuard } from './core/guards/auth.guard';
import { ShellComponent } from './core/layout/shell.component';

const composer = () => import('./features/composer/composer.page').then((m) => m.ComposerPage);

export const routes: Routes = [
  // Public
  { path: 'login', component: LoginPageComponent },
  { path: 'signup', component: RegisterPageComponent },
  { path: 'register', pathMatch: 'full', redirectTo: 'signup' },
  { path: 'access-denied', loadComponent: () => import('./features/account/access-denied.page').then((m) => m.AccessDeniedPage) },

  // New-user onboarding: dedicated pages outside the app shell; the guard only opens the step the API says is next.
  {
    path: 'onboarding',
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'profile' },
      { path: 'workspace', canActivate: [onboardingGuard('WORKSPACE')], loadComponent: () => import('./features/onboarding/workspace-step.page').then((m) => m.WorkspaceStepPage) },
      { path: 'profile', canActivate: [onboardingGuard('PROFILE')], loadComponent: () => import('./features/onboarding/profile-step.page').then((m) => m.ProfileStepPage) },
      { path: 'brand', canActivate: [onboardingGuard('BRAND_PROFILE')], loadComponent: () => import('./features/onboarding/brand-step.page').then((m) => m.BrandStepPage) },
      { path: 'social-connections', canActivate: [onboardingGuard('SOCIAL_CONNECTIONS')], loadComponent: () => import('./features/onboarding/social-step.page').then((m) => m.SocialStepPage) },
    ],
  },

  // Internal platform administration: own sign-in and layout, system administrators only (never a workspace role).
  { path: 'internal/login', loadComponent: () => import('./features/internal/internal-login.page').then((m) => m.InternalLoginPage) },
  { path: 'internal', canActivate: [systemAdminGuard], loadChildren: () => import('./features/internal/internal.routes').then((m) => m.INTERNAL_ROUTES) },

  // The customer application, scoped to a workspace in the URL: /{workspace-slug}/home.
  {
    path: ':workspace',
    canMatch: [workspaceMatchGuard],
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      { path: 'home', loadComponent: () => import('./features/home/home.page').then((m) => m.HomePage) },
      { path: 'ai-studio', canActivate: [contentCreatorGuard], loadComponent: () => import('./features/ai-studio/ai-studio.page').then((m) => m.AiStudioPage) },
      // Composer: a new post (/composer?campaignId=…) or an existing one (/composer/{postId}). Read-only for reviewers.
      { path: 'composer', loadComponent: composer },
      { path: 'composer/:postId', loadComponent: composer },
      // Creator (MVP): Home, AI Studio, Composer, Planner, Notifications and Profile only. Every other page is Admin-only
      // here as well as hidden in the sidebar; the API enforces the same permissions.
      { path: 'campaigns', canActivate: [workspaceAdminGuard], loadChildren: () => import('./features/campaigns/campaigns.routes').then((m) => m.CAMPAIGN_ROUTES) },
      { path: 'approvals', canActivate: [workspaceAdminGuard], loadComponent: () => import('./features/approvals/approvals.page').then((m) => m.ApprovalsPage) },
      { path: 'approvals/:reviewId', canActivate: [workspaceAdminGuard], loadComponent: () => import('./features/approvals/pages/review-detail/review-detail.page').then((m) => m.ReviewDetailPage) },
      { path: 'planner', loadComponent: () => import('./features/planner/planner.page').then((m) => m.PlannerPage) },
      { path: 'library', canActivate: [workspaceAdminGuard], loadComponent: () => import('./features/library/library.page').then((m) => m.LibraryPage) },
      { path: 'brand', canActivate: [workspaceAdminGuard], loadComponent: () => import('./features/settings/pages/brand-settings/brand-settings.page').then((m) => m.BrandSettingsPage) },
      // Social accounts live in Settings; the provider sign-in still returns to /social-accounts?status=… (query kept).
      { path: 'social-accounts', pathMatch: 'full', redirectTo: 'settings/workspace' },
      // Members: the Admin creates Creator logins (no invitation email in the MVP).
      { path: 'members', canActivate: [workspaceAdminGuard], loadComponent: () => import('./features/settings/pages/team.page').then((m) => m.TeamPage) },
      { path: 'team', redirectTo: 'members' },
      { path: 'settings', canActivate: [workspaceAdminGuard], loadChildren: () => import('./features/settings/settings.routes').then((m) => m.SETTINGS_ROUTES) },
      { path: 'notifications', loadComponent: () => import('./features/account/notifications.page').then((m) => m.NotificationsPage) },
      { path: 'profile', loadComponent: () => import('./features/account/profile.page').then((m) => m.ProfilePage) },

      // Older links (bookmarks, notifications, emails) keep working.
      { path: 'dashboard', pathMatch: 'full', redirectTo: 'home' },
      { path: 'posts/new', redirectTo: 'composer' },
      { path: 'posts/:postId', redirectTo: 'composer/:postId' },
      { path: 'posts/:postId/versions', redirectTo: 'composer/:postId' },
      { path: 'publisher/approvals', pathMatch: 'full', redirectTo: 'approvals' },
      { path: 'publisher/approvals/:reviewId', redirectTo: 'approvals/:reviewId' },
      { path: 'publisher', redirectTo: 'planner' },
      { path: 'publisher/:view', redirectTo: 'planner' },
      { path: 'calendar', pathMatch: 'full', redirectTo: 'planner' },
      { path: 'publishing', pathMatch: 'full', redirectTo: 'planner' },
      { path: 'publishing/:publicationJobId', redirectTo: 'planner' },
      { path: '**', redirectTo: 'home' },
    ],
  },
  // Any other URL (/, /home, /campaigns/123, links without a workspace) opens inside the current workspace.
  { path: '**', component: ShellComponent, canActivate: [authGuard, workspaceRedirectGuard], children: [] },
];
