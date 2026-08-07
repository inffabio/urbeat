import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

describe('authGuard', () => {
  let authServiceMock: any;
  let routerMock: any;

  beforeEach(() => {
    authServiceMock = {
      isLoggedIn: jest.fn(),
      getToken: jest.fn(),
      logout: jest.fn(),
    };

    routerMock = {
      navigateByUrl: jest.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock }
      ]
    });
  });

  it('should allow activation if user is logged in', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.getToken.mockReturnValue('eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiU2VsbGVyIn0.signature');

    const result = TestBed.runInInjectionContext(() => authGuard());

    expect(result).toBe(true);
    expect(routerMock.navigateByUrl).not.toHaveBeenCalled();
  });

  it('should redirect to "/login-vendedor" and deny activation if user is not logged in', () => {
    authServiceMock.isLoggedIn.mockReturnValue(false);
    authServiceMock.getToken.mockReturnValue(null);

    const result = TestBed.runInInjectionContext(() => authGuard());

    expect(result).toBe(false);
    expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/login-vendedor');
  });

  it('should deny activation and logout if token is not a seller token', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.getToken.mockReturnValue('eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiQ3VzdG9tZXIifQ.signature');

    const result = TestBed.runInInjectionContext(() => authGuard({} as any, {} as any));

    expect(result).toBe(false);
    expect(authServiceMock.logout).toHaveBeenCalled();
    expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/login-vendedor');
  });
});
