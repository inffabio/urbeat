import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { adminStoreContextInterceptor } from './admin-store-context.interceptor';
import { AdminStoreSessionService } from '../services/admin-store-session.service';
import { ToastService } from '../services/toast.service';

describe('adminStoreContextInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let session: {
    contextToken: jest.Mock<string | null, []>;
    markInvalid: jest.Mock;
    shouldNotify: jest.Mock;
  };
  let toast: { showError: jest.Mock; showWarning: jest.Mock };

  beforeEach(() => {
    session = {
      contextToken: jest.fn(() => 'opaque-token'),
      markInvalid: jest.fn(),
      shouldNotify: jest.fn(() => true),
    };
    toast = { showError: jest.fn(() => Promise.resolve()), showWarning: jest.fn(() => Promise.resolve()) };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([adminStoreContextInterceptor])),
        provideHttpClientTesting(),
        { provide: AdminStoreSessionService, useValue: session },
        { provide: ToastService, useValue: toast },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it.each([
    '/api/stores/my-store',
    '/api/stores/store-1/products',
    '/api/orders/store',
    '/api/neighborhoods/cities',
    '/api/subscriptions/my',
  ])('adds the header on the delegable path %s', (url) => {
    http.get(url).subscribe();
    const request = httpMock.expectOne(url);

    expect(request.request.headers.get('X-Admin-Store-Context')).toBe('opaque-token');
    request.flush({});
  });

  it.each([
    '/api/auth/logout',
    '/api/admin/stores',
    '/api/admin/stores/store-1/access-context',
    '/api/public/stores/by-path/pizza-hunter',
    '/api/customer/me',
  ])('never adds the header on %s', (url) => {
    http.get(url).subscribe();
    const request = httpMock.expectOne(url);

    expect(request.request.headers.has('X-Admin-Store-Context')).toBe(false);
    request.flush({});
  });

  it('does not add the header when no context is active', () => {
    session.contextToken.mockReturnValue(null);

    http.get('/api/stores/my-store').subscribe();
    const request = httpMock.expectOne('/api/stores/my-store');

    expect(request.request.headers.has('X-Admin-Store-Context')).toBe(false);
    request.flush({});
  });

  it('invalidates the session and shows one toast for context-specific 403 codes', () => {
    const errors: unknown[] = [];

    http.get('/api/stores/my-store').subscribe({ error: (error) => errors.push(error) });
    const request = httpMock.expectOne('/api/stores/my-store');
    request.flush(
      { code: 'ADMIN_STORE_CONTEXT_EXPIRED', message: 'Sessão administrativa expirada.' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(session.markInvalid).toHaveBeenCalledWith('ADMIN_STORE_CONTEXT_EXPIRED');
    expect(toast.showError).toHaveBeenCalledTimes(1);
    expect(errors).toHaveLength(1);
  });

  it('does not treat ADMIN_STORE_ACTION_NOT_ALLOWED as a session failure', () => {
    http.get('/api/stores/store-1/categories').subscribe({ error: () => undefined });
    const request = httpMock.expectOne('/api/stores/store-1/categories');
    request.flush(
      { code: 'ADMIN_STORE_ACTION_NOT_ALLOWED', message: 'Ação não permitida.' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(session.markInvalid).not.toHaveBeenCalled();
    expect(toast.showError).not.toHaveBeenCalled();
  });
});
