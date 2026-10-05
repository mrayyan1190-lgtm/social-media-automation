import { Routes } from '@angular/router';

const editor = () => import('./pages/post-editor/post-editor.page').then((m) => m.PostEditorPage);

export const CONTENT_ROUTES: Routes = [
  { path: 'new', loadComponent: editor },
  { path: ':postId', loadComponent: editor },
  // The editor page includes the full version history.
  { path: ':postId/versions', loadComponent: editor },
];
