import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { resolveTokenRole } from '../utils/jwt.helper';
import { SKIP_AUTH_HEADER } from './auth.context';

/**
 * Public storefront surface (store resolution, catalog and reviews). It is anonymous by contract,
 * so an administrator/seller credential must never be forwarded here: those tokens belong to the
 * dashboard surfaces and would only leak another session's identity into the public page.
 */
const PUBLIC_API_PATH = /^\/api\/public(?:\/|$)/;

function pathOf(url: string): string {
  try {
    return new URL(url, 'http://urbeat.local').pathname;
  } catch {
    return url.split(/[?#]/, 1)[0] ?? url;
  }
}

function isPublicApiRequest(url: string): boolean {
  return PUBLIC_API_PATH.test(pathOf(url));
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.getToken();
  if (
    token
    && req.url.includes('/api/')
    && !req.url.includes('/auth/refresh')
    && !req.context.get(SKIP_AUTH_HEADER)
    // Public calls keep the customer identity (already anonymous-safe) but never a privileged one.
    && (!isPublicApiRequest(req.url) || resolveTokenRole(token) === 'Customer')
  ) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    });
  }
  return next(req);
};
