import { Routes } from '@angular/router';
import { STRINGS } from './shared/strings.he';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: STRINGS.home.title,
    loadComponent: () => import('./pages/gallery/gallery').then((m) => m.GalleryPage),
  },
  {
    path: 'profile/:id',
    title: STRINGS.profile.title,
    loadComponent: () => import('./pages/profile/profile').then((m) => m.ProfilePage),
  },
  {
    path: 'design-system',
    title: STRINGS.designSystem.title,
    loadComponent: () => import('./design-system/design-system').then((m) => m.DesignSystem),
  },
  {
    path: 'about',
    title: STRINGS.about.title,
    loadComponent: () => import('./pages/about/about').then((m) => m.AboutPage),
  },
  {
    path: 'join',
    title: STRINGS.join.title,
    loadComponent: () => import('./pages/join/join').then((m) => m.JoinPage),
  },
  {
    path: 'edit',
    title: STRINGS.edit.title,
    loadComponent: () => import('./pages/edit/edit').then((m) => m.EditPage),
  },
  {
    path: '**',
    title: STRINGS.notFoundPage.title,
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFoundPage),
  },
];
