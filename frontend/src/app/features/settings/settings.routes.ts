import { Routes } from '@angular/router';

/** Workspace settings for Admins. Brand, team and social accounts have their own top-level pages. No infrastructure here. */
export const SETTINGS_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'workspace' },
  { path: 'workspace', loadComponent: () => import('./pages/workspace-settings.page').then((m) => m.WorkspaceSettingsPage) },
  // Older links (including the OAuth return of earlier builds).
  { path: 'brand', pathMatch: 'full', redirectTo: '/brand' },
  { path: 'team', pathMatch: 'full', redirectTo: '/team' },
  { path: 'social-connections', pathMatch: 'full', redirectTo: '/social-accounts' },
  { path: 'connections', pathMatch: 'full', redirectTo: '/social-accounts' },
  { path: 'destinations', pathMatch: 'full', redirectTo: '/social-accounts' },
];
