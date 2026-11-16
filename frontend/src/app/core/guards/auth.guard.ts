import { ActivatedRouteSnapshot, CanActivateChildFn, CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { AdminStoreSessionService } from '../services/admin-store-session.service';
import { isAdmin, isCustomer, isSeller } from '../utils/jwt.helper';

/**
 * `/app` is the seller dashboard. A seller keeps the historical synchronous check; an
 * administrator only enters with a live, server-validated store context and is otherwise sent back
 * to the administrative store list — never logged out and never sent to the seller login.
 */
const requireStoreSession = (): boolean | Promise<boolean> => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.getToken();
  if (auth.isLoggedIn() && token && isSeller(token)) return true;

  if (token && isAdmin(token)) {
    return inject(AdminStoreSessionService)
      .ensureRestored()
      .then((active) => {
        if (active) return true;

        void router.navigateByUrl('/admin/lojas');
        return false;
      });
  }

  if (token) auth.logout();
  router.navigateByUrl('/login-vendedor');
  return false;
};

export const authGuard: CanActivateFn = (
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
) => requireStoreSession();

export const authChildGuard: CanActivateChildFn = (
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
) => requireStoreSession();

const requireAuthenticatedCustomer = (state: RouterStateSnapshot): boolean => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.getToken();
  if (auth.isLoggedIn() && token && isCustomer(token)) return true;

  const storePath = state.url.match(/^\/([^/]+)\//)?.[1];
  router.navigate(storePath ? ['/', storePath] : ['/']);
  return false;
};

export const customerGuard: CanActivateFn = (
  _route: ActivatedRouteSnapshot,
  state: RouterStateSnapshot,
) => requireAuthenticatedCustomer(state);
