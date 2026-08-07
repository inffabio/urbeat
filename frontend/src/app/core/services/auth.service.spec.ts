import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, HttpBackend } from '@angular/common/http';
import { AuthService } from './auth.service';
import { ApiService } from './api.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();

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
    it('should call login API and save token on success', () => {
      const loginReq = { email: 'test@test.com', password: 'password123' };
      const mockResponse = { accessToken: 'new-token', refreshToken: 'refresh-token' };

      service.loginSeller(loginReq).subscribe(res => {
        expect(res.accessToken).toBe('new-token');
        expect(service.getToken()).toBe('new-token');
        expect(localStorage.getItem('urbeat_token')).toBe('new-token');
        expect(localStorage.getItem('urbeat_refresh')).toBe('refresh-token');
      });

      const req = httpMock.expectOne('/api/auth/login/seller');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(loginReq);
      req.flush(mockResponse);
    });
  });

  describe('loginAdmin', () => {
    it('should call admin login API and save token on success', () => {
      const loginReq = { email: 'admin@test.com', password: 'admin123' };
      const mockResponse = { accessToken: 'admin-token', refreshToken: 'admin-refresh' };

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
      localStorage.setItem('urbeat_token', 'old-token');
      localStorage.setItem('urbeat_refresh', 'old-refresh');
      
      service.getToken(); 

      service.logout();

      expect(service.getToken()).toBeNull();
      expect(localStorage.getItem('urbeat_token')).toBeNull();
      expect(localStorage.getItem('urbeat_refresh')).toBeNull();
    });
  });

  describe('refreshToken', () => {
    it('should call refresh API with credentials and save new tokens', () => {
      const mockResponse = { accessToken: 'refreshed-token', refreshToken: 'refreshed-refresh' };

      service.refreshToken().subscribe(res => {
        expect(res.accessToken).toBe('refreshed-token');
        expect(service.getToken()).toBe('refreshed-token');
        expect(localStorage.getItem('urbeat_token')).toBe('refreshed-token');
        expect(localStorage.getItem('urbeat_refresh')).toBe('refreshed-refresh');
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
      const mockToken = { accessToken: 'customer-token', refreshToken: 'rotated-refresh' };

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
