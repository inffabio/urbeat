import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { errorInterceptor } from './error.interceptor';
import { authInterceptor } from './auth.interceptor';
import { ToastService } from '../services/toast.service';
import { AuthService } from '../services/auth.service';
import { CheckoutService } from '../services/checkout.service';
import { CustomerOrderTrackingService } from '../services/customer-order-tracking.service';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

function encodeTokenPayload(payload: unknown): string {
  return `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify(payload))}.signature`;
}

describe('errorInterceptor', () => {
  let httpMock: HttpTestingController;
  let httpClient: HttpClient;
  let toastServiceMock: jest.Mocked<ToastService>;
  let authServiceMock: jest.Mocked<Partial<AuthService>>;
  let routerMock: jest.Mocked<Partial<Router>> & { url: string };
  let checkoutServiceMock: { resetCheckout: jest.Mock };
  let trackingServiceMock: { reset: jest.Mock };
  let refreshTokenSpy: jest.Mock;
  let consoleErrorSpy: jest.SpyInstance;

  beforeEach(() => {
    refreshTokenSpy = jest.fn();
    consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    toastServiceMock = {
      showError: jest.fn(),
      showSuccess: jest.fn(),
      showWarning: jest.fn(),
      showInfo: jest.fn(),
    } as any;

    authServiceMock = {
      getToken: jest.fn().mockReturnValue('expired-token'),
      refreshToken: refreshTokenSpy,
      logout: jest.fn(),
      saveToken: jest.fn(),
    };

    routerMock = {
      url: '/app/dashboard',
      navigate: jest.fn().mockResolvedValue(true),
    };

    checkoutServiceMock = { resetCheckout: jest.fn() };
    trackingServiceMock = { reset: jest.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastServiceMock },
        { provide: AuthService, useValue: authServiceMock },
        { provide: CheckoutService, useValue: checkoutServiceMock },
        { provide: CustomerOrderTrackingService, useValue: trackingServiceMock },
        { provide: Router, useValue: routerMock },
      ]
    });

    httpMock = TestBed.inject(HttpTestingController);
    httpClient = TestBed.inject(HttpClient);
  });

  afterEach(() => {
    httpMock.verify();
    consoleErrorSpy.mockRestore();
    jest.clearAllMocks();
  });

  const triggerError = (url: string, errorBody: any, status: number) => {
    httpClient.get(url).subscribe({
      error: () => {}
    });
    const req = httpMock.expectOne(url);
    req.flush(errorBody, { status, statusText: 'Error' });
  };

  describe('400 Bad Request', () => {
    it('should show toast with string error message', () => {
      triggerError('/api/test', 'Invalid data', 400);
      expect(toastServiceMock.showError).toHaveBeenCalledWith('Invalid data');
    });

    it('should show toast with errors array (FluentValidation style)', () => {
      triggerError('/api/test', { errors: { email: ['Email is invalid'], password: ['Password is too short'] } }, 400);
      expect(toastServiceMock.showError).toHaveBeenCalledWith('Email is invalid\nPassword is too short');
    });

    it('should show toast with detail property (Problem Details)', () => {
      triggerError('/api/test', { detail: 'The store name is required.' }, 400);
      expect(toastServiceMock.showError).toHaveBeenCalledWith('The store name is required.');
    });

    it('should not show toast for checkout preview below-minimum responses with summary', () => {
      triggerError('/api/checkout/preview', {
        error: 'Order is below minimum value.',
        summary: { subtotal: 10, minimumOrderValue: 20 }
      }, 400);

      expect(toastServiceMock.showError).not.toHaveBeenCalled();
    });

    it('should let checkout delivery-area errors be handled by the checkout modal', () => {
      triggerError('/api/checkout/confirm', {
        error: 'Ainda nao entregamos no seu bairro.'
      }, 400);

      expect(toastServiceMock.showError).not.toHaveBeenCalled();
    });

    it('should leave all checkout errors for the checkout screen', () => {
      triggerError('/api/checkout/confirm', {
        error: 'Não foi possível validar o pedido.'
      }, 400);

      expect(toastServiceMock.showError).not.toHaveBeenCalled();
    });
  });

  describe('401 Unauthorized', () => {
    it('should pass through 401 for login endpoint without refresh', () => {
      triggerError('/api/auth/login/seller', 'Unauthorized', 401);
      expect(toastServiceMock.showError).not.toHaveBeenCalled();
      expect(refreshTokenSpy).not.toHaveBeenCalled();
    });

    it('should pass through 401 for refresh endpoint without retrying', () => {
      triggerError('/api/auth/refresh', 'Unauthorized', 401);
      expect(toastServiceMock.showError).not.toHaveBeenCalled();
      expect(refreshTokenSpy).not.toHaveBeenCalled();
    });

    it('treats a seller 401 as an expired session without refreshing the shared cookie', () => {
      authServiceMock.getToken.mockReturnValue(encodeTokenPayload({ role: 'Seller' }));

      let caughtError: any;
      httpClient.get('/api/stores/my-store').subscribe({
        error: (err) => { caughtError = err; }
      });

      const req = httpMock.expectOne('/api/stores/my-store');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      expect(refreshTokenSpy).not.toHaveBeenCalled();
      expect(authServiceMock.logout).toHaveBeenCalled();
      expect(toastServiceMock.showError).toHaveBeenCalledWith('Sua sessao expirou. Por favor, faca login novamente.');
      expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
      expect(caughtError).toBeTruthy();
    });

    it('should attempt refresh and retry on 401 for API endpoints', () => {
      refreshTokenSpy.mockReturnValue(of({ accessToken: 'new-token', refreshToken: 'new-refresh' }));

      let success = false;
      httpClient.get('/api/stores/my-store').subscribe({
        next: () => { success = true; }
      });

      const req = httpMock.expectOne('/api/stores/my-store');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      httpMock.expectOne('/api/stores/my-store');
    });

    it('should logout and navigate on refresh failure', () => {
      refreshTokenSpy.mockReturnValue(throwError(() => new Error('Refresh failed')));

      let caughtError: any;
      httpClient.get('/api/stores/my-store').subscribe({
        error: (err) => { caughtError = err; }
      });

      const req = httpMock.expectOne('/api/stores/my-store');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      expect(authServiceMock.logout).toHaveBeenCalled();
      expect(toastServiceMock.showError).toHaveBeenCalledWith('Sua sessao expirou. Por favor, faca login novamente.');
      expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
    });

    it('redirects customer sessions to the public store menu and clears customer state on refresh failure', () => {
      authServiceMock.getToken.mockReturnValue(encodeTokenPayload({ role: 'Customer' }));
      refreshTokenSpy.mockReturnValue(throwError(() => new Error('Refresh failed')));
      routerMock.url = '/loja/checkout/pagamento';

      httpClient.get('/api/customer/me').subscribe({ error: () => {} });

      const req = httpMock.expectOne('/api/customer/me');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      expect(authServiceMock.logout).toHaveBeenCalled();
      expect(checkoutServiceMock.resetCheckout).toHaveBeenCalled();
      expect(trackingServiceMock.reset).toHaveBeenCalled();
      expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
      expect(routerMock.navigate).not.toHaveBeenCalledWith(['/login-vendedor']);
    });

    it('resets the seller shell on seller session expiry', () => {
      authServiceMock.getToken.mockReturnValue(encodeTokenPayload({ role: 'Seller' }));
      refreshTokenSpy.mockReturnValue(throwError(() => new Error('Refresh failed')));

      httpClient.get('/api/stores/my-store').subscribe({ error: () => {} });

      const req = httpMock.expectOne('/api/stores/my-store');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      expect(authServiceMock.logout).toHaveBeenCalled();
      expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
      expect(checkoutServiceMock.resetCheckout).not.toHaveBeenCalled();
      expect(trackingServiceMock.reset).not.toHaveBeenCalled();
    });

    it('sends a seller on the wizard to login on session expiry so the next login routes by publication state', () => {
      authServiceMock.getToken.mockReturnValue(encodeTokenPayload({ role: 'Seller' }));
      refreshTokenSpy.mockReturnValue(throwError(() => new Error('Refresh failed')));
      routerMock.url = '/configurar-loja/produtos';

      httpClient.get('/api/stores/my-store').subscribe({ error: () => {} });

      const req = httpMock.expectOne('/api/stores/my-store');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
    });

    it('resolves pending requests with an error when the refresh fails', () => {
      let failRefresh: (err: unknown) => void = () => {};
      refreshTokenSpy.mockReturnValue(new Observable((subscriber) => {
        failRefresh = (err) => subscriber.error(err);
      }));

      let firstErrored = false;
      let secondErrored = false;

      httpClient.get('/api/first').subscribe({ error: () => { firstErrored = true; } });
      httpMock.expectOne('/api/first').flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      httpClient.get('/api/second').subscribe({ error: () => { secondErrored = true; } });
      httpMock.expectOne('/api/second').flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      failRefresh(new Error('Refresh failed'));

      expect(firstErrored).toBe(true);
      expect(secondErrored).toBe(true);
      expect(authServiceMock.logout).toHaveBeenCalled();
    });

    it('retries only once and stops the loop when the refreshed token is rejected again', () => {
      refreshTokenSpy.mockReturnValue(of({ accessToken: 'fresh-token', refreshToken: 'fresh-refresh' }));

      httpClient.get('/api/loop').subscribe({ error: () => {} });

      const req = httpMock.expectOne('/api/loop');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      const retryReq = httpMock.expectOne('/api/loop');
      retryReq.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

      expect(refreshTokenSpy).toHaveBeenCalledTimes(1);
      expect(authServiceMock.logout).toHaveBeenCalled();
    });
  });

  describe('403 Forbidden', () => {
    it('should show access denied message', () => {
      triggerError('/api/admin/data', 'Forbidden', 403);
      expect(toastServiceMock.showError).toHaveBeenCalledWith('Acesso não autorizado.');
    });
  });

  describe('404 Not Found', () => {
    it('should show "Recurso não encontrado" for general 404 errors', () => {
      triggerError('/api/stores/invalid-id', 'Not Found', 404);
      expect(toastServiceMock.showError).toHaveBeenCalledWith('Recurso não encontrado.');
    });

    it('should NOT show toast for 404 on /stores/my-store (expected for new users)', () => {
      triggerError('/api/stores/my-store', 'Not Found', 404);
      expect(toastServiceMock.showError).not.toHaveBeenCalled();
    });
  });

  describe('500+ Server Errors', () => {
    it('should show generic server error message', () => {
      triggerError('/api/crash', 'Internal Server Error', 500);
      expect(toastServiceMock.showError).toHaveBeenCalledWith('Erro interno no servidor. Tente novamente mais tarde.');
    });
  });

  describe('413 Payload Too Large', () => {
    const uploadInfrastructureMessage =
      'Não foi possível enviar o arquivo: ele excede o limite de upload do servidor. Reduza o tamanho da imagem e tente novamente.';

    it('shows a clear upload infrastructure/size message for 413 on a logo upload', () => {
      const formData = new FormData();
      httpClient.post('/api/stores/upload-image?type=logo', formData).subscribe({ error: () => {} });
      const req = httpMock.expectOne('/api/stores/upload-image?type=logo');
      req.flush('Payload Too Large', { status: 413, statusText: 'Payload Too Large' });

      expect(toastServiceMock.showError).toHaveBeenCalledWith(uploadInfrastructureMessage);
    });

    it('does not falsely claim a 2 MB logo limit when the proxy rejects a logo upload', () => {
      const formData = new FormData();
      httpClient.post('/api/stores/upload-image?type=logo', formData).subscribe({ error: () => {} });
      const req = httpMock.expectOne('/api/stores/upload-image?type=logo');
      req.flush('Payload Too Large', { status: 413, statusText: 'Payload Too Large' });

      expect(toastServiceMock.showError).not.toHaveBeenCalledWith('A logo deve ter no máximo 2 MB.');
      expect(toastServiceMock.showError).not.toHaveBeenCalledWith(expect.stringContaining('2 MB'));
    });

    it('does not claim a specific banner size limit for a 413 from the proxy', () => {
      const formData = new FormData();
      httpClient.post('/api/stores/upload-image?type=banner', formData).subscribe({ error: () => {} });
      const req = httpMock.expectOne('/api/stores/upload-image?type=banner');
      req.flush('Payload Too Large', { status: 413, statusText: 'Payload Too Large' });

      expect(toastServiceMock.showError).toHaveBeenCalledWith(uploadInfrastructureMessage);
      expect(toastServiceMock.showError).not.toHaveBeenCalledWith(expect.stringContaining('5 MB'));
    });

    it('keeps generic handling for 413 responses that are not image uploads', () => {
      triggerError('/api/orders/bulk', 'Payload Too Large', 413);

      expect(toastServiceMock.showError).not.toHaveBeenCalledWith(expect.stringContaining('2 MB'));
      expect(toastServiceMock.showError).not.toHaveBeenCalledWith(expect.stringContaining('5 MB'));
      expect(toastServiceMock.showError).not.toHaveBeenCalledWith(uploadInfrastructureMessage);
    });

    it('leaves legacy product image 413 responses to the component size message', () => {
      const url = '/api/stores/store-1/products/product-1/images';
      httpClient.post(url, new FormData()).subscribe({ error: () => {} });
      const req = httpMock.expectOne(url);
      req.flush('Payload Too Large', { status: 413, statusText: 'Payload Too Large' });

      expect(toastServiceMock.showError).not.toHaveBeenCalled();
    });
  });

  it('does not show a global error toast when the optional local printer agent is offline', () => {
    httpClient.get('http://127.0.0.1:43111/printers').subscribe({ error: () => {} });
    const req = httpMock.expectOne('http://127.0.0.1:43111/printers');
    req.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(toastServiceMock.showError).not.toHaveBeenCalled();
    expect(consoleErrorSpy).not.toHaveBeenCalled();
  });
});
