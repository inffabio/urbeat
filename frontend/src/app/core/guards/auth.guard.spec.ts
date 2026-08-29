import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authGuard, customerGuard } from './auth.guard';
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

describe('customerGuard', () => {
  let authServiceMock: any;
  let routerMock: any;

  beforeEach(() => {
    authServiceMock = {
      isLoggedIn: jest.fn(),
      getToken: jest.fn(),
    };

    routerMock = {
      navigate: jest.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock },
      ],
    });
  });

  it('should allow activation when the customer is authenticated', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.getToken.mockReturnValue('eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiQ3VzdG9tZXIifQ.signature');

    const result = TestBed.runInInjectionContext(() =>
      customerGuard({} as any, { url: '/loja/conta/cadastro' } as any),
    );

    expect(result).toBe(true);
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('should deny activation and redirect when the token is a seller token', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.getToken.mockReturnValue('eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiU2VsbGVyIn0.signature');

    const result = TestBed.runInInjectionContext(() =>
      customerGuard({} as any, { url: '/loja/conta/cadastro' } as any),
    );

    expect(result).toBe(false);
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('should deny activation and redirect when there is no token', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.getToken.mockReturnValue(null);

    const result = TestBed.runInInjectionContext(() =>
      customerGuard({} as any, { url: '/loja/conta/cadastro' } as any),
    );

    expect(result).toBe(false);
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('should redirect back to the storefront and deny activation when unauthenticated', () => {
    authServiceMock.isLoggedIn.mockReturnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      customerGuard({} as any, { url: '/loja/conta/cadastro' } as any),
    );

    expect(result).toBe(false);
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('should redirect to the root when no store path is present', () => {
    authServiceMock.isLoggedIn.mockReturnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      customerGuard({} as any, { url: '/' } as any),
    );

    expect(result).toBe(false);
    expect(routerMock.navigate).toHaveBeenCalledWith(['/']);
  });
});
