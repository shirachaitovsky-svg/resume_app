import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { ADMIN_HOME, ADMIN_LOGIN, ADMIN_SETUP, AdminSession } from './admin-session';

export type AdminArea = 'login' | 'setup' | 'home';

const AREA_PATH: Readonly<Record<AdminArea, string>> = {
  login: ADMIN_LOGIN,
  setup: ADMIN_SETUP,
  home: ADMIN_HOME,
};

/** Lets a route through only when the session state belongs there; otherwise redirects to where it does. */
export function adminGuard(area: AdminArea): CanActivateFn {
  return async (): Promise<boolean | UrlTree> => {
    const session = inject(AdminSession);
    const router = inject(Router);
    await session.ensureLoaded();
    const destination = session.destination();
    return destination === AREA_PATH[area] ? true : router.parseUrl(destination);
  };
}
