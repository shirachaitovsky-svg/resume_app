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
  { path: 'join', pathMatch: 'full', redirectTo: 'me' },
  {
    path: 'join/:token',
    title: STRINGS.joinForm.title,
    loadComponent: () => import('./pages/join/join-redirect').then((m) => m.JoinRedirectPage),
  },
  { path: 'edit', pathMatch: 'full', redirectTo: 'me' },
  {
    path: 'me',
    title: STRINGS.myProfile.title,
    loadComponent: () => import('./pages/me/me').then((m) => m.MePage),
  },
  {
    path: 'contact/verify',
    title: STRINGS.contactVerify.title,
    loadComponent: () => import('./pages/contact/verify/contact-verify').then((m) => m.ContactVerifyPage),
  },
  {
    path: 'contact/decide',
    title: STRINGS.contactDecide.title,
    loadComponent: () => import('./pages/contact/decide/contact-decide').then((m) => m.ContactDecidePage),
  },
  {
    path: 'admin',
    loadChildren: () => import('./pages/admin/admin.routes').then((m) => m.ADMIN_ROUTES),
  },
  {
    path: '**',
    title: STRINGS.notFoundPage.title,
    loadComponent: () => import('./pages/not-found/not-found').then((m) => m.NotFoundPage),
  },
];
