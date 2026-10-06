import { Routes } from '@angular/router';

export const CAMPAIGN_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./pages/campaign-list/campaign-list.page').then((m) => m.CampaignListPage) },
  { path: 'new', redirectTo: '' },
  { path: ':campaignId', loadComponent: () => import('./pages/campaign-detail/campaign-detail.page').then((m) => m.CampaignDetailPage) },
  // Older tab links.
  { path: ':campaignId/:tab', redirectTo: ':campaignId' },
];
