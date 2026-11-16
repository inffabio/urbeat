import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthService } from '../services/auth.service';
import { SKIP_AUTH_HEADER } from './auth.context';
import { authInterceptor } from './auth.interceptor';

function encodeToken(payload: unknown): string {
  return `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify(payload))}.signature`;
}

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let token: string | null;

  const adminToken = encodeToken({ role: 'Admin' });
  const sellerToken = encodeToken({ role: 'Seller' });
  const customerToken = encodeToken({ role: 'Customer' });

  beforeEach(() => {
    token = null;

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { getToken: () => token } },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function authorizationOf(url: string, context?: HttpContext): string | null {
    http.get(url, { context }).subscribe();
    const request = httpMock.expectOne(url);
    const header = request.request.headers.get('Authorization');
    request.flush({});
    return header;
  }

  describe('public storefront endpoints', () => {
    const publicUrls = [
      '/api/public/stores/by-path/minha-loja',
      '/api/public/stores/store-1',
      '/api/public/stores/store-1/catalog/categories',
      '/api/public/stores/store-1/catalog/products',
      '/api/public/stores/store-1/reviews',
    ];

    it('never forwards an administrative bearer token to public storefront calls', () => {
      token = adminToken;

      for (const url of publicUrls) {
        expect(authorizationOf(url)).toBeNull();
      }
    });

    it('never forwards a seller bearer token to public storefront calls', () => {
      token = sellerToken;

      for (const url of publicUrls) {
        expect(authorizationOf(url)).toBeNull();
      }
    });

    it('keeps forwarding a customer bearer token to public storefront calls', () => {
      token = customerToken;

      expect(authorizationOf('/api/public/stores/by-path/minha-loja'))
        .toBe(`Bearer ${customerToken}`);
    });

    it('does not forward an undecodable token to public storefront calls', () => {
      token = 'opaque-token';

      expect(authorizationOf('/api/public/stores/by-path/minha-loja')).toBeNull();
    });
  });

  describe('protected surfaces', () => {
    it('keeps the administrative bearer token on administrative endpoints', () => {
      token = adminToken;

      expect(authorizationOf('/api/admin/stores')).toBe(`Bearer ${adminToken}`);
      expect(authorizationOf('/api/stores/store-1/products')).toBe(`Bearer ${adminToken}`);
    });

    it('keeps the seller bearer token on seller endpoints', () => {
      token = sellerToken;

      expect(authorizationOf('/api/stores/my-store')).toBe(`Bearer ${sellerToken}`);
      expect(authorizationOf('/api/orders/store')).toBe(`Bearer ${sellerToken}`);
    });

    it('still honors the explicit SKIP_AUTH_HEADER opt-out', () => {
      token = customerToken;
      const context = new HttpContext().set(SKIP_AUTH_HEADER, true);

      expect(authorizationOf('/api/delivery-routes/store', context)).toBeNull();
    });

    it('never forwards the bearer token to the refresh endpoint', () => {
      token = customerToken;

      expect(authorizationOf('/api/auth/refresh')).toBeNull();
    });
  });
});
