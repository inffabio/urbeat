import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import {
  ADMIN_STORE_CONTEXT_ERROR_CODES,
  AdminStoreSessionService,
} from '../services/admin-store-session.service';
import { ToastService } from '../services/toast.service';

/**
 * Only the delegated store surface receives the opaque context header. `/api/auth/` and
 * `/api/admin/` are intentionally excluded: the context endpoints set the header explicitly in
 * `AdminStoreSessionService`, and a stale context must never block authentication or the
 * administrative list.
 */
const DELEGABLE_PREFIXES = [
  '/api/stores/',
  '/api/orders/',
  '/api/neighborhoods/',
  '/api/subscriptions/',
];

const CONTEXT_HEADER = 'X-Admin-Store-Context';

function pathOf(url: string): string {
  try {
    return new URL(url, 'http://urbeat.local').pathname;
  } catch {
    return url.split(/[?#]/, 1)[0] ?? url;
  }
}

function messageFor(code: string): string {
  switch (code) {
    case 'ADMIN_STORE_CONTEXT_EXPIRED':
      return 'Sessão administrativa expirada. Volte ao painel administrativo.';
    case 'ADMIN_STORE_CONTEXT_STORE_MISMATCH':
      return 'Esta sessão administrativa pertence a outra loja.';
    case 'ADMIN_STORE_CONTEXT_STORE_MISSING':
      return 'A loja desta sessão administrativa não existe mais.';
    case 'ADMIN_STORE_CONTEXT_ADMIN_NOT_ALLOWED':
      return 'Sua conta perdeu o acesso administrativo.';
    default:
      return 'Sessão administrativa inválida. Abra a loja novamente pelo painel administrativo.';
  }
}

export const adminStoreContextInterceptor: HttpInterceptorFn = (req, next) => {
  const sessionService = inject(AdminStoreSessionService);
  const toast = inject(ToastService);

  const token = sessionService.contextToken();
  const path = pathOf(req.url);
  const isDelegable = DELEGABLE_PREFIXES.some((prefix) => path.startsWith(prefix));

  const outgoing =
    token && isDelegable
      ? req.clone({ setHeaders: { [CONTEXT_HEADER]: token } })
      : req;

  return next(outgoing).pipe(
    catchError((error: HttpErrorResponse) => {
      const code =
        error.status === 403 && typeof error.error?.code === 'string' ? error.error.code : null;

      if (code && ADMIN_STORE_CONTEXT_ERROR_CODES.has(code)) {
        sessionService.markInvalid(code);
        if (sessionService.shouldNotify(code)) {
          void toast.showError(messageFor(code));
        }
      }

      // No logout and no navigation here: the administrator keeps a valid JWT and the banner
      // offers the explicit return to /admin/lojas.
      return throwError(() => error);
    }),
  );
};
