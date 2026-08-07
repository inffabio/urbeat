import { ActivatedRouteSnapshot, CanActivateChildFn, CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { isSeller } from '../utils/jwt.helper';

const requireAuthenticatedSeller = (): boolean => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.getToken();
  if (auth.isLoggedIn() && token && isSeller(token)) return true;

  if (token) auth.logout();
  router.navigateByUrl('/login-vendedor');
  return false;
};

export const authGuard: CanActivateFn = (
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
) => requireAuthenticatedSeller();

export const authChildGuard: CanActivateChildFn = (
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
) => requireAuthenticatedSeller();
