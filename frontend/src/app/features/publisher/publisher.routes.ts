import { Routes } from '@angular/router';

const posts = () => import('./posts.page').then((m) => m.PublisherPostsPage);
const jobs = () => import('./publications.page').then((m) => m.PublicationsPage);

/**
 * Publisher: the one place to manage content after creation. Planner, All Posts and Drafts browse posts; Scheduled,
 * Published and Failed browse publishing outcomes; Approvals is the review workflow.
 */
export const PUBLISHER_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'planner' },
  { path: 'planner', loadComponent: posts, data: { view: 'planner' } },
  { path: 'posts', loadComponent: posts, data: { view: 'all' } },
  { path: 'drafts', loadComponent: posts, data: { view: 'drafts' } },
  { path: 'approvals', loadComponent: () => import('./approvals.page').then((m) => m.ApprovalsPage) },
  { path: 'approvals/:reviewId', loadComponent: () => import('../approvals/pages/review-detail/review-detail.page').then((m) => m.ReviewDetailPage) },
  { path: 'scheduled', loadComponent: jobs, data: { kind: 'scheduled' } },
  { path: 'published', loadComponent: jobs, data: { kind: 'published' } },
  { path: 'failed', loadComponent: jobs, data: { kind: 'failed' } },
];
