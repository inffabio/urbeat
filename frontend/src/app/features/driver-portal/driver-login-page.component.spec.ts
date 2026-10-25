import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { DriverLoginPageComponent } from './driver-login-page.component';

describe('DriverLoginPageComponent', () => {
  let authMock: { getToken: jest.Mock; isLoggedIn: jest.Mock; loginDriver: jest.Mock };
  let routerMock: { navigateByUrl: jest.Mock };

  beforeEach(async () => {
    authMock = {
      getToken: jest.fn().mockReturnValue(null),
      isLoggedIn: jest.fn().mockReturnValue(false),
      loginDriver: jest.fn().mockReturnValue(of({ accessToken: 'token', expiresAtUtc: '' })),
    };
    routerMock = { navigateByUrl: jest.fn().mockResolvedValue(true) };

    await TestBed.configureTestingModule({
      imports: [DriverLoginPageComponent],
      providers: [
        { provide: AuthService, useValue: authMock },
        { provide: Router, useValue: routerMock },
      ],
    }).compileComponents();
  });

  it('validates the form before logging in', () => {
    const fixture = TestBed.createComponent(DriverLoginPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.submit();

    expect(authMock.loginDriver).not.toHaveBeenCalled();
    expect(fixture.componentInstance.loginForm.controls.email.touched).toBe(true);
  });

  it('logs in and navigates to the driver orders page', () => {
    const fixture = TestBed.createComponent(DriverLoginPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.loginForm.setValue({ email: 'driver@test.com', password: 'SenhaForte123' });
    component.submit();

    expect(authMock.loginDriver).toHaveBeenCalledWith({
      email: 'driver@test.com',
      password: 'SenhaForte123',
    });
    expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/entregador/pedidos');
  });

  it('explains missing delivery driver access on 403', () => {
    authMock.loginDriver.mockReturnValue(throwError(() => ({ status: 403 })));
    const fixture = TestBed.createComponent(DriverLoginPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.loginForm.setValue({ email: 'driver@test.com', password: 'SenhaForte123' });
    component.submit();

    expect(component.error()).toContain('Aceite o convite');
  });
});
