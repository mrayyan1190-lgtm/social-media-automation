import { Routes } from '@angular/router';

/** Workspace settings and social accounts for Admins. Brand and team have their own top-level pages. No infrastructure here. */
export const SETTINGS_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'workspace' },
  { path: 'workspace', loadComponent: () => import('./pages/workspace-settings.page').then((m) => m.WorkspaceSettingsPage) },
  // Older links (including the OAuth return of earlier builds).
  { path: 'brand', pathMatch: 'full', redirectTo: '/brand' },
  { path: 'team', pathMatch: 'full', redirectTo: '/members' },
  { path: 'social-connections', pathMatch: 'full', redirectTo: 'workspace' },
  { path: 'connections', pathMatch: 'full', redirectTo: 'workspace' },
  { path: 'destinations', pathMatch: 'full', redirectTo: 'workspace' },
];
