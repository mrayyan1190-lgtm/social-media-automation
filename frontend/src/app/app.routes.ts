import { Routes } from '@angular/router';
import { LoginPageComponent } from './core/auth/login-page.component';
import { RegisterPageComponent } from './core/auth/register-page.component';
import { authGuard, contentCreatorGuard, onboardingGuard, systemAdminGuard, workspaceAdminGuard } from './core/guards/auth.guard';
import { ShellComponent } from './core/layout/shell.component';

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

  // The customer application.
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      { path: 'home', loadComponent: () => import('./features/home/home.page').then((m) => m.HomePage) },
      { path: 'ai-studio', canActivate: [contentCreatorGuard], loadComponent: () => import('./features/ai-studio/ai-studio.page').then((m) => m.AiStudioPage) },
      { path: 'campaigns', loadChildren: () => import('./features/campaigns/campaigns.routes').then((m) => m.CAMPAIGN_ROUTES) },
      // The post composer (edit, rewrite, versions, review submission, scheduling) for one platform post.
      { path: 'posts', loadChildren: () => import('./features/content/content.routes').then((m) => m.CONTENT_ROUTES) },
      { path: 'publisher', loadChildren: () => import('./features/publisher/publisher.routes').then((m) => m.PUBLISHER_ROUTES) },
      { path: 'library', loadComponent: () => import('./features/library/library.page').then((m) => m.LibraryPage) },
      { path: 'brand', loadComponent: () => import('./features/settings/pages/brand-settings/brand-settings.page').then((m) => m.BrandSettingsPage) },
      // Workspace administration: workspace Admins only (the API enforces each permission too).
      { path: 'social-accounts', canActivate: [workspaceAdminGuard], loadComponent: () => import('./features/settings/pages/social-connections.page').then((m) => m.SocialConnectionsPage) },
      { path: 'team', canActivate: [workspaceAdminGuard], loadComponent: () => import('./features/settings/pages/team.page').then((m) => m.TeamPage) },
      { path: 'settings', canActivate: [workspaceAdminGuard], loadChildren: () => import('./features/settings/settings.routes').then((m) => m.SETTINGS_ROUTES) },
      { path: 'notifications', loadComponent: () => import('./features/account/notifications.page').then((m) => m.NotificationsPage) },
      { path: 'profile', loadComponent: () => import('./features/account/profile.page').then((m) => m.ProfilePage) },

      // Older links (bookmarks, notifications, emails) keep working.
      { path: 'dashboard', pathMatch: 'full', redirectTo: 'home' },
      { path: 'approvals', pathMatch: 'full', redirectTo: 'publisher/approvals' },
      { path: 'approvals/:reviewId', redirectTo: 'publisher/approvals/:reviewId' },
      { path: 'calendar', pathMatch: 'full', redirectTo: 'publisher/planner' },
      { path: 'publishing', pathMatch: 'full', redirectTo: 'publisher/planner' },
      { path: 'publishing/:publicationJobId', redirectTo: 'publisher/planner' },
    ],
  },
  { path: '**', redirectTo: '' },
];
