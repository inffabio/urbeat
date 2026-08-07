import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { errorInterceptor } from './error.interceptor';
import { authInterceptor } from './auth.interceptor';
import { ToastService } from '../services/toast.service';
import { AuthService } from '../services/auth.service';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

describe('errorInterceptor', () => {
  let httpMock: HttpTestingController;
  let httpClient: HttpClient;
  let toastServiceMock: jest.Mocked<ToastService>;
  let authServiceMock: jest.Mocked<Partial<AuthService>>;
  let routerMock: jest.Mocked<Partial<Router>>;
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
      navigate: jest.fn().mockResolvedValue(true),
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastServiceMock },
        { provide: AuthService, useValue: authServiceMock },
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

  it('does not show a global error toast when the optional local printer agent is offline', () => {
    httpClient.get('http://127.0.0.1:43111/printers').subscribe({ error: () => {} });
    const req = httpMock.expectOne('http://127.0.0.1:43111/printers');
    req.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(toastServiceMock.showError).not.toHaveBeenCalled();
    expect(consoleErrorSpy).not.toHaveBeenCalled();
  });
});
