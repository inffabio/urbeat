import { HttpInterceptorFn, HttpErrorResponse, HttpEvent, HttpRequest, HttpHandlerFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError, Observable } from 'rxjs';
import { ToastService } from '../services/toast.service';
import { AuthService } from '../services/auth.service';
import { CheckoutService } from '../services/checkout.service';
import { CustomerOrderTrackingService } from '../services/customer-order-tracking.service';
import { isCustomer } from '../utils/jwt.helper';

interface RefreshSubscriber {
  next: (token: string) => void;
  error: (err: unknown) => void;
}

let isRefreshing = false;
let refreshSubscribers: RefreshSubscriber[] = [];
const retriedRequests = new WeakSet<HttpRequest<unknown>>();
const LOCAL_AGENT_ORIGIN = 'http://127.0.0.1:43111/';

function isDeliveryAreaError(req: HttpRequest<unknown>, error: HttpErrorResponse): boolean {
  if (!req.url.includes('/checkout/preview') && !req.url.includes('/checkout/confirm')) {
    return false;
  }

  const message = typeof error.error === 'string' ? error.error : error.error?.error;
  return typeof message === 'string' && /entregamos.*bairro/i.test(message);
}

function isCheckoutRequest(req: HttpRequest<unknown>): boolean {
  return req.url.includes('/checkout/preview') || req.url.includes('/checkout/confirm');
}

function isImageUploadRequest(url: string): boolean {
  return url.includes('/upload-image');
}

function isLegacyProductImageUpload(url: string): boolean {
  return /\/products\/[^/?#]+\/images(?:[?#]|$)/.test(url);
}

// The API enforces its documented per-type limits (2 MB logo, 5 MB banner,
// 6 MB product) with HTTP 400, so an HTTP 413 can only be produced by the proxy
// or host in front of it. A 413 cannot be attributed to a specific file limit
// and must not claim the file exceeds 2 MB when NGINX rejected the request.
const UPLOAD_TOO_LARGE_MESSAGE =
  'Não foi possível enviar o arquivo: ele excede o limite de upload do servidor. Reduza o tamanho da imagem e tente novamente.';

function imageUploadTooLargeMessage(url: string): string | null {
  return isImageUploadRequest(url) ? UPLOAD_TOO_LARGE_MESSAGE : null;
}

function onRefreshed(token: string): void {
  const subscribers = refreshSubscribers;
  refreshSubscribers = [];
  for (const subscriber of subscribers) {
    subscriber.next(token);
  }
}

function onRefreshFailed(err: unknown): void {
  const subscribers = refreshSubscribers;
  refreshSubscribers = [];
  for (const subscriber of subscribers) {
    subscriber.error(err);
  }
}

function retryWithToken(
  req: HttpRequest<unknown>,
  token: string,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> {
  const retryReq = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
  retriedRequests.add(retryReq);
  return next(retryReq);
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toastService = inject(ToastService);
  const authService = inject(AuthService);
  const router = inject(Router);
  const checkoutService = inject(CheckoutService);
  const trackingService = inject(CustomerOrderTrackingService);

  const handleSessionExpired = (error: unknown): Observable<never> => {
    const token = authService.getToken();
    const customerSession = !!token && isCustomer(token);

    authService.logout();
    toastService.showError('Sua sessao expirou. Por favor, faca login novamente.');

    if (customerSession) {
      checkoutService.resetCheckout();
      trackingService.reset();
      const storePath = router.url.match(/^\/([^/]+)\//)?.[1];
      router.navigate(storePath ? ['/', storePath] : ['/']);
    } else {
      router.navigate(['/login-vendedor']);
    }

    return throwError(() => error);
  };

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (req.url.startsWith(LOCAL_AGENT_ORIGIN)) {
        return throwError(() => err);
      }

      if (err.status === 401) {
        if (req.url.includes('/auth/login') || req.url.includes('/auth/refresh')) {
          return throwError(() => err);
        }

        if (retriedRequests.has(req)) {
          return handleSessionExpired(err);
        }

        if (!isRefreshing) {
          isRefreshing = true;

          return authService.refreshToken().pipe(
            switchMap((res) => {
              isRefreshing = false;
              onRefreshed(res.accessToken);
              return retryWithToken(req, res.accessToken, next);
            }),
            catchError((refreshErr) => {
              isRefreshing = false;
              onRefreshFailed(refreshErr);
              return handleSessionExpired(refreshErr);
            })
          );
        }

        return new Observable<HttpEvent<unknown>>((subscriber) => {
          refreshSubscribers.push({
            next: (token) => {
              retryWithToken(req, token, next).subscribe(subscriber);
            },
            error: (refreshErr) => subscriber.error(refreshErr),
          });
        });
      }

      if (isCheckoutRequest(req) || isDeliveryAreaError(req, err)) {
        return throwError(() => err);
      }

      if (err.status === 413) {
        const uploadMessage = imageUploadTooLargeMessage(req.url);
        if (uploadMessage) {
          console.error('[HTTP error]', req.url, err);
          toastService.showError(uploadMessage);
          return throwError(() => err);
        }

        // The legacy product image endpoint owns its own size message, so the
        // interceptor must not also surface a generic toast for it.
        if (isLegacyProductImageUpload(req.url)) {
          return throwError(() => err);
        }
      }

      console.error('[HTTP error]', req.url, err);

      let errorMessage = 'Ocorreu um erro ao processar sua requisição.';

      if (err.status === 400 && err.error) {
        if (req.url.includes('/checkout/preview') && err.error.summary) {
          return throwError(() => err);
        }

        // Let product/category/upload endpoint errors be handled by their components
        if (req.url.includes('/products') || req.url.includes('/categories') || isImageUploadRequest(req.url)) {
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
