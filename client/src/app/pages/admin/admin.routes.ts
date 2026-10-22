import { Routes } from '@angular/router';
import { STRINGS } from '../../shared/strings.he';
import { adminGuard } from './admin.guard';

export const ADMIN_ROUTES: Routes = [
  {
    path: 'login',
    title: STRINGS.admin.login.title,
    canActivate: [adminGuard('login')],
    loadComponent: () => import('./login/admin-login').then((m) => m.AdminLoginPage),
  },
  {
    path: 'setup',
    title: STRINGS.admin.setup.title,
    canActivate: [adminGuard('setup')],
    loadComponent: () => import('./setup/admin-setup').then((m) => m.AdminSetupPage),
  },
  {
    path: '',
    pathMatch: 'full',
    title: STRINGS.admin.title,
    canActivate: [adminGuard('home')],
    loadComponent: () => import('./home/admin-home').then((m) => m.AdminHomePage),
  },
  {
    path: 'cohorts',
    title: STRINGS.admin.cohorts.title,
    canActivate: [adminGuard('home')],
    loadComponent: () => import('./cohorts/admin-cohorts').then((m) => m.AdminCohortsPage),
  },
  {
    path: 'review',
    title: STRINGS.admin.review.title,
    canActivate: [adminGuard('home')],
    loadComponent: () => import('./review/admin-review').then((m) => m.AdminReviewPage),
  },
  {
    path: 'review/:id',
    title: STRINGS.admin.review.detail.title,
    canActivate: [adminGuard('home')],
    loadComponent: () => import('./review/submission-detail').then((m) => m.AdminSubmissionDetailPage),
  },
  {
    path: 'contact-requests',
    title: STRINGS.admin.contactRequests.title,
    canActivate: [adminGuard('home')],
    loadComponent: () => import('./contact-requests/admin-contact-requests').then((m) => m.AdminContactRequestsPage),
  },
  {
    path: 'profiles',
    title: STRINGS.admin.profiles.title,
    canActivate: [adminGuard('home')],
    loadComponent: () => import('./profiles/admin-profiles').then((m) => m.AdminProfilesPage),
  },
];
