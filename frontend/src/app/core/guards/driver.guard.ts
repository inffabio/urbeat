import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { isDeliveryDriver } from '../utils/jwt.helper';

const requireAuthenticatedDriver = (): boolean => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = auth.getToken();
  if (auth.isLoggedIn() && token && isDeliveryDriver(token)) return true;

  // Never destroy another identity's session here: a seller/customer who opens the
  // driver URL is simply redirected. Logging in as a driver replaces the tab token.
  router.navigateByUrl('/entregador/login');
  return false;
};

export const driverGuard: CanActivateFn = () => requireAuthenticatedDriver();
export const driverChildGuard: CanActivateFn = () => requireAuthenticatedDriver();
