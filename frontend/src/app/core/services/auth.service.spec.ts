import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, HttpBackend } from '@angular/common/http';
import { AuthService } from './auth.service';
import { ApiService } from './api.service';

function encodeTokenPayload(payload: unknown): string {
  return `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify(payload))}.signature`;
}

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        ApiService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('Initial State', () => {
    it('should initialize with null token if localStorage is empty', () => {
      expect(service.getToken()).toBeNull();
      expect(service.isAuthenticated()).toBe(false);
    });
  });

  describe('loginSeller', () => {
    it('should call login API and save the seller token in the tab session', () => {
      const loginReq = { email: 'test@test.com', password: 'password123' };
      const accessToken = encodeTokenPayload({ role: 'Seller' });
      const mockResponse = { accessToken, expiresAtUtc: '2026-08-04T22:30:00.000Z' };

      service.loginSeller(loginReq).subscribe(res => {
        expect(res.accessToken).toBe(accessToken);
        expect(service.getToken()).toBe(accessToken);
        expect(sessionStorage.getItem('urbeat_seller_token')).toBe(accessToken);
        expect(localStorage.getItem('urbeat_token')).toBeNull();
        expect(localStorage.getItem('urbeat_refresh')).toBeNull();
      });

      const req = httpMock.expectOne('/api/auth/login/seller');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(loginReq);
      req.flush(mockResponse);
    });
  });

  describe('seller session isolation', () => {
    const sellerToken = encodeTokenPayload({ role: 'Seller' });

    const configureFreshService = (): { freshService: AuthService } => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          AuthService,
          ApiService,
          provideHttpClient(),
          provideHttpClientTesting()
        ]
      });
      const freshService = TestBed.inject(AuthService);
      httpMock = TestBed.inject(HttpTestingController);
      return { freshService };
    };

    it('clears a conflicting generic/local token when a seller logs in', () => {
      localStorage.setItem('urbeat_token', 'legacy-customer-token');

      service.loginSeller({ email: 'a@a.com', password: 'x' }).subscribe();

      const req = httpMock.expectOne('/api/auth/login/seller');
      req.flush({ accessToken: sellerToken, expiresAtUtc: '' });

      expect(sessionStorage.getItem('urbeat_seller_token')).toBe(sellerToken);
      expect(localStorage.getItem('urbeat_token')).toBeNull();
      expect(service.getToken()).toBe(sellerToken);
    });

    it('restores the seller token from the tab session on reload', () => {
      sessionStorage.setItem('urbeat_seller_token', sellerToken);

      const { freshService } = configureFreshService();

      expect(freshService.getToken()).toBe(sellerToken);
      expect(freshService.isLoggedIn()).toBe(true);
    });

    it('prefers the tab seller session over a legacy customer token in localStorage', () => {
      sessionStorage.setItem('urbeat_seller_token', sellerToken);
      localStorage.setItem('urbeat_token', encodeTokenPayload({ role: 'Customer' }));

      const { freshService } = configureFreshService();

      expect(freshService.getToken()).toBe(sellerToken);
    });

    it('clears only the tab seller session on logout and skips the shared-cookie endpoint', () => {
      sessionStorage.setItem('urbeat_seller_token', sellerToken);
      localStorage.setItem('urbeat_token', 'other-tab-customer-token');

      const { freshService } = configureFreshService();

      freshService.logout();

      expect(freshService.getToken()).toBeNull();
      expect(sessionStorage.getItem('urbeat_seller_token')).toBeNull();
      expect(localStorage.getItem('urbeat_token')).toBe('other-tab-customer-token');
      httpMock.expectNone('/api/auth/logout');
    });

    it('keeps customer sessions in localStorage and clears the tab seller token', () => {
      sessionStorage.setItem('urbeat_seller_token', sellerToken);
      const customerToken = encodeTokenPayload({ role: 'Customer' });

      service.login({ email: 'c@c.com', password: 'x' }).subscribe();

      const req = httpMock.expectOne('/api/auth/login/customer');
      req.flush({ accessToken: customerToken, expiresAtUtc: '' });

      expect(localStorage.getItem('urbeat_token')).toBe(customerToken);
      expect(sessionStorage.getItem('urbeat_seller_token')).toBeNull();
      expect(service.getToken()).toBe(customerToken);
    });
  });

  describe('loginAdmin', () => {
    it('should call admin login API and save token on success', () => {
      const loginReq = { email: 'admin@test.com', password: 'admin123' };
      const mockResponse = { accessToken: 'admin-token', expiresAtUtc: '2026-08-04T22:30:00.000Z' };

      service.loginAdmin(loginReq).subscribe(res => {
        expect(res.accessToken).toBe('admin-token');
        expect(service.getToken()).toBe('admin-token');
      });

      const req = httpMock.expectOne('/api/auth/login/admin');
      expect(req.request.method).toBe('POST');
      req.flush(mockResponse);
    });
  });

  describe('logout', () => {
    it('should clear token from localStorage and state', () => {
      service.saveToken({ accessToken: 'old-token', expiresAtUtc: '' });

      service.logout();

      expect(service.getToken()).toBeNull();
      expect(localStorage.getItem('urbeat_token')).toBeNull();
      expect(localStorage.getItem('urbeat_refresh')).toBeNull();

      const req = httpMock.expectOne('/api/auth/logout');
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBe(true);
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('clears local state even when the logout endpoint fails', () => {
      service.saveToken({ accessToken: 'old-token', expiresAtUtc: '' });

      service.logout();

      expect(service.getToken()).toBeNull();
      expect(localStorage.getItem('urbeat_token')).toBeNull();

      const req = httpMock.expectOne('/api/auth/logout');
      req.flush('Server error', { status: 500, statusText: 'Internal Server Error' });
    });

    it('does not send the access token as part of the logout request body', () => {
      service.saveToken({ accessToken: 'secret-token', expiresAtUtc: '' });

      service.logout();

      const req = httpMock.expectOne('/api/auth/logout');
      expect(req.request.body).toEqual({});
      expect(JSON.stringify(req.request.body)).not.toContain('secret-token');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });
  });

  describe('refreshToken', () => {
    it('should call refresh API with credentials and save new tokens', () => {
      const mockResponse = { accessToken: 'refreshed-token', expiresAtUtc: '2026-08-04T22:30:00.000Z' };

      service.refreshToken().subscribe(res => {
        expect(res.accessToken).toBe('refreshed-token');
        expect(service.getToken()).toBe('refreshed-token');
        expect(localStorage.getItem('urbeat_token')).toBe('refreshed-token');
        expect(localStorage.getItem('urbeat_refresh')).toBeNull();
      });

      const req = httpMock.expectOne('/api/auth/refresh');
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBe(true);
      req.flush(mockResponse);
    });

    it('should propagate error when refresh fails', () => {
      service.refreshToken().subscribe({
        error: (err) => {
          expect(err.status).toBe(401);
          expect(service.getToken()).toBeNull();
        }
      });

      const req = httpMock.expectOne('/api/auth/refresh');
      req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    });
  });

  describe('restoreCustomerSession', () => {
    it('should refresh from the secure cookie and load the current customer profile', () => {
      const mockToken = { accessToken: 'customer-token', expiresAtUtc: '2026-08-04T22:30:00.000Z' };

      (service as any).restoreCustomerSession().subscribe((profile: any) => {
        expect(profile.fullName).toBe('Maria Oliveira');
        expect(profile.primaryAddressId).toBe('addr1');
        expect(service.getToken()).toBe('customer-token');
        expect((service as any).customerProfile()).toEqual({
          fullName: 'Maria Oliveira',
          email: 'maria@email.com',
          phoneNumber: '22999999999',
          primaryAddressId: 'addr1',
        });
      });

      const refreshReq = httpMock.expectOne('/api/auth/refresh');
      expect(refreshReq.request.method).toBe('POST');
      expect(refreshReq.request.withCredentials).toBe(true);
      refreshReq.flush(mockToken);

      const profileReq = httpMock.expectOne('/api/customer/me');
      expect(profileReq.request.method).toBe('GET');
      profileReq.flush({
        fullName: 'Maria Oliveira',
        email: 'maria@email.com',
        phoneNumber: '22999999999',
        primaryAddressId: 'addr1',
      });
    });
  });

  it('updates the customer profile and refreshes the profile signal', () => {
    const request = {
      fullName: 'Maria Atualizada',
      email: 'maria.nova@email.com',
      phoneNumber: '22988887777',
    };

    service.updateCustomerProfile(request).subscribe((profile) => {
      expect(profile.fullName).toBe('Maria Atualizada');
      expect(service.customerProfile()).toEqual(profile);
    });

    const req = httpMock.expectOne('/api/customer/me');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush({ ...request, primaryAddressId: 'address-1' });
  });

  describe('updateSellerProfile', () => {
    it('should PUT the contractor name to the seller profile endpoint', () => {
      const request = { fullName: 'Contratante Atualizado' };

      service.updateSellerProfile(request).subscribe((profile) => {
        expect(profile.fullName).toBe('Contratante Atualizado');
      });

      const req = httpMock.expectOne('/api/seller/profile');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({
        fullName: 'Contratante Atualizado',
        document: null,
        phoneNumber: '11999999999',
        email: 'vendedor@email.com',
      });
    });
  });

  describe('registerSeller', () => {
    it('should call seller registration API', () => {
      const registerReq = { 
        name: 'Test Store', 
        email: 'store@test.com', 
        password: 'password123',
        phoneNumber: '11999999999'
      };

      service.registerSeller(registerReq).subscribe(res => {
        expect(res).toBeTruthy();
      });

      const req = httpMock.expectOne('/api/auth/register/seller');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(registerReq);
      req.flush({ message: 'Registered successfully' });
    });
  });
});
