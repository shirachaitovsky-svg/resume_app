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
];
