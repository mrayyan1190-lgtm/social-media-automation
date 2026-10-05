import { Routes } from '@angular/router';
import { contentCreatorGuard } from '../../core/guards/auth.guard';

const detail = () => import('./pages/campaign-detail/campaign-detail.page').then((m) => m.CampaignDetailPage);

/** Campaign detail tabs (Overview, Content, Activity) share one page; the tab comes from the route data. */
export const CAMPAIGN_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./pages/campaign-list/campaign-list.page').then((m) => m.CampaignListPage) },
  { path: 'new', canActivate: [contentCreatorGuard], loadComponent: () => import('./pages/campaign-create/campaign-create.page').then((m) => m.CampaignCreatePage) },
  { path: ':campaignId', loadComponent: detail, data: { tab: 'overview' } },
  { path: ':campaignId/content', loadComponent: detail, data: { tab: 'content' } },
  { path: ':campaignId/activity', loadComponent: detail, data: { tab: 'activity' } },
  // Older links.
  { path: ':campaignId/edit', redirectTo: ':campaignId' },
  { path: ':campaignId/details', redirectTo: ':campaignId' },
  { path: ':campaignId/platforms', redirectTo: ':campaignId' },
  { path: ':campaignId/review', redirectTo: ':campaignId' },
  { path: ':campaignId/generate', redirectTo: ':campaignId/content' },
];
