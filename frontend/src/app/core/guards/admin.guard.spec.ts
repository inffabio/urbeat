import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { adminGuard } from './admin.guard';
import { AuthService } from '../services/auth.service';
import { ToastController } from '@ionic/angular/standalone';

// Mock ToastController
const mockToast = {
  present: jest.fn(),
};

describe('adminGuard', () => {
  let authServiceMock: any;
  let routerMock: any;
  let toastControllerMock: any;

  beforeEach(() => {
    authServiceMock = {
      getToken: jest.fn(),
      logout: jest.fn(),
    };

    routerMock = {
      navigateByUrl: jest.fn(),
    };

    toastControllerMock = {
      create: jest.fn().mockResolvedValue(mockToast),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock },
        { provide: ToastController, useValue: toastControllerMock }
      ]
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  it('should allow activation if user has valid admin token', async () => {
    // Mock a valid admin JWT payload (base64 encoded: {"role": "Admin"})
    const adminToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiQWRtaW4ifQ.mocksignature';
    authServiceMock.getToken.mockReturnValue(adminToken);

    const result = await TestBed.runInInjectionContext(() => adminGuard());

    expect(result).toBe(true);
    expect(routerMock.navigateByUrl).not.toHaveBeenCalled();
    expect(authServiceMock.logout).not.toHaveBeenCalled();
  });

  it('should deny activation, show toast, and redirect to /painel/login if no token', async () => {
    authServiceMock.getToken.mockReturnValue(null);

    const result = await TestBed.runInInjectionContext(() => adminGuard());

    expect(result).toBe(false);
    expect(toastControllerMock.create).toHaveBeenCalledWith(
      expect.objectContaining({
        message: 'Acesso restrito. Faça login como administrador.',
        position: 'top',
      })
    );
    expect(mockToast.present).toHaveBeenCalled();
    expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/painel/login');
    expect(authServiceMock.logout).not.toHaveBeenCalled();
  });

  it('should deny activation, show toast, logout, and redirect if token exists but user is not admin', async () => {
    // Mock a valid customer JWT payload (base64 encoded: {"role": "Customer"})
    const customerToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiQ3VzdG9tZXIifQ.mocksignature';
    authServiceMock.getToken.mockReturnValue(customerToken);

    const result = await TestBed.runInInjectionContext(() => adminGuard());

    expect(result).toBe(false);
    expect(toastControllerMock.create).toHaveBeenCalled();
    expect(mockToast.present).toHaveBeenCalled();
    expect(authServiceMock.logout).toHaveBeenCalled();
    expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/painel/login');
  });
});
