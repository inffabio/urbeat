import { HttpInterceptorFn, HttpErrorResponse, HttpEvent } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError, Observable } from 'rxjs';
import { ToastService } from '../services/toast.service';
import { AuthService } from '../services/auth.service';

let isRefreshing = false;
let refreshSubscribers: Array<(token: string) => void> = [];
const LOCAL_AGENT_ORIGIN = 'http://127.0.0.1:43111/';

function onRefreshed(token: string): void {
  for (const cb of refreshSubscribers) {
    cb(token);
  }
  refreshSubscribers = [];
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toastService = inject(ToastService);
  const authService = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (req.url.startsWith(LOCAL_AGENT_ORIGIN)) {
        return throwError(() => err);
      }

      if (err.status === 401) {
        if (req.url.includes('/auth/login') || req.url.includes('/auth/refresh')) {
          return throwError(() => err);
        }

        if (!isRefreshing) {
          isRefreshing = true;

          return authService.refreshToken().pipe(
            switchMap((res) => {
              isRefreshing = false;
              onRefreshed(res.accessToken);

              const retryReq = req.clone({
                setHeaders: { Authorization: `Bearer ${res.accessToken}` }
              });
              return next(retryReq);
            }),
            catchError((refreshErr) => {
              isRefreshing = false;
              refreshSubscribers = [];

              authService.logout();
              toastService.showError('Sua sessao expirou. Por favor, faca login novamente.');
              router.navigate(['/login-vendedor']);
              return throwError(() => refreshErr);
            })
          );
        }

        return new Observable<HttpEvent<unknown>>((subscriber) => {
          refreshSubscribers.push((token: string) => {
            const retryReq = req.clone({
              setHeaders: { Authorization: `Bearer ${token}` }
            });
            next(retryReq).subscribe(subscriber);
          });
        });
      }

      console.error('[HTTP error]', req.url, err);

      let errorMessage = 'Ocorreu um erro ao processar sua requisição.';

      if (err.status === 400 && err.error) {
        if (req.url.includes('/checkout/preview') && err.error.summary) {
          return throwError(() => err);
        }

        // Let product/category endpoint errors be handled by their components
        if (req.url.includes('/products') || req.url.includes('/categories')) {
          return throwError(() => err);
        }

        if (typeof err.error === 'string') {
          errorMessage = err.error;
        } else if (err.error.message) {
          errorMessage = err.error.message;
        } else if (err.error.error) {
          errorMessage = err.error.error;
        } else if (err.error.errors && typeof err.error.errors === 'object') {
          const errors = Object.values(err.error.errors).flat();
          errorMessage = errors.join('\n');
        } else if (err.error.detail) {
          errorMessage = err.error.detail;
        } else {
          errorMessage = JSON.stringify(err.error);
        }
      } else if (err.status === 403) {
        errorMessage = 'Acesso não autorizado.';
      } else if (err.status === 409) {
        if (err.error?.detail) {
          errorMessage = err.error.detail;
        } else {
          errorMessage = 'Este recurso já existe.';
        }
      } else if (err.status === 404) {
        if (req.url.includes('/address-lookup/cep/')) {
          return throwError(() => err);
        }
        if (req.url.includes('/stores/my-store')) {
          return throwError(() => err);
        }
        errorMessage = 'Recurso não encontrado.';
      } else if (err.status >= 500) {
        errorMessage = 'Erro interno no servidor. Tente novamente mais tarde.';
      } else if (err.message) {
        errorMessage = err.message;
      }

      toastService.showError(errorMessage);
      return throwError(() => err);
    })
  );
};
