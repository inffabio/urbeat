import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { driverGuard } from './driver.guard';

function tokenWithRole(role: string): string {
  return `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify({ role }))}.signature`;
}

describe('driverGuard', () => {
  let authMock: { isLoggedIn: jest.Mock; getToken: jest.Mock; logout: jest.Mock };
  let routerMock: { navigateByUrl: jest.Mock };

  beforeEach(() => {
    authMock = {
      isLoggedIn: jest.fn(),
      getToken: jest.fn(),
      logout: jest.fn(),
    };
    routerMock = { navigateByUrl: jest.fn() };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authMock },
        { provide: Router, useValue: routerMock },
      ],
    });
  });

  it('allows a delivery driver', () => {
    authMock.isLoggedIn.mockReturnValue(true);
    authMock.getToken.mockReturnValue(tokenWithRole('DeliveryDriver'));

    expect(TestBed.runInInjectionContext(() => driverGuard({} as never, {} as never))).toBe(true);
    expect(routerMock.navigateByUrl).not.toHaveBeenCalled();
  });

  it('redirects a non-driver identity to the driver login', () => {
    authMock.isLoggedIn.mockReturnValue(true);
    authMock.getToken.mockReturnValue(tokenWithRole('Seller'));

    expect(TestBed.runInInjectionContext(() => driverGuard({} as never, {} as never))).toBe(false);
    expect(authMock.logout).not.toHaveBeenCalled();
    expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/entregador/login');
  });
});
