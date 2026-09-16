import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerRegisterPageComponent } from './seller-register-page.component';

describe('SellerRegisterPageComponent', () => {
  let fixture: ComponentFixture<SellerRegisterPageComponent>;
  let component: SellerRegisterPageComponent;
  let authServiceMock: {
    registerSeller: jest.Mock;
    logout: jest.Mock;
    isAuthenticated: jest.Mock;
  };
  let routerMock: { navigate: jest.Mock };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock; showWarning: jest.Mock };

  const registerResponse = {
    succeeded: true,
    userId: 'user-1',
    emailConfirmationPending: false,
  };

  beforeEach(async () => {
    authServiceMock = {
      registerSeller: jest.fn(),
      logout: jest.fn(),
      isAuthenticated: jest.fn().mockReturnValue(false),
    };
    routerMock = { navigate: jest.fn() };
    toastMock = {
      showSuccess: jest.fn(),
      showError: jest.fn(),
      showWarning: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerRegisterPageComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: {} },
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SellerRegisterPageComponent);
    component = fixture.componentInstance;
  });

  function fillValidForm(): void {
    component.fullName.set('Contratante Teste');
    component.document.set('');
    component.whatsapp.set('(21) 99999-9999');
    component.email.set('novo@vendedor.com');
    component.password.set('Senha@123');
    component.confirmPassword.set('Senha@123');
  }

  function submit(response: typeof registerResponse): void {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(of(response));
    component.submit();
  }

  it('clears the existing session before registering a new seller account', () => {
    authServiceMock.isAuthenticated.mockReturnValue(true);

    submit(registerResponse);

    expect(authServiceMock.logout).toHaveBeenCalledTimes(1);
    expect(authServiceMock.logout.mock.invocationCallOrder[0]).toBeLessThan(
      authServiceMock.registerSeller.mock.invocationCallOrder[0],
    );
  });

  it('does not log out after the registration request resolves', () => {
    authServiceMock.isAuthenticated.mockReturnValue(true);
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(of(registerResponse));

    component.submit();

    const registerOrder = authServiceMock.registerSeller.mock.invocationCallOrder[0];
    expect(authServiceMock.registerSeller).toHaveBeenCalledTimes(1);
    expect(authServiceMock.logout).toHaveBeenCalledTimes(1);
    expect(
      authServiceMock.logout.mock.invocationCallOrder.every((order) => order < registerOrder),
    ).toBe(true);
  });

  it('does not clear a session when there is no existing session', () => {
    authServiceMock.isAuthenticated.mockReturnValue(false);

    submit(registerResponse);

    expect(authServiceMock.logout).not.toHaveBeenCalled();
  });

  it('navigates to /login-vendedor when the account is immediately activated', () => {
    submit({ ...registerResponse, emailConfirmationPending: false });

    expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
    expect(routerMock.navigate).not.toHaveBeenCalledWith(['/login']);
  });

  it('navigates to the email confirmation page when confirmation is pending', () => {
    submit({ ...registerResponse, emailConfirmationPending: true });

    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com', userId: 'user-1' } },
    );
  });

  it('preserves registration error handling for an email conflict', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(throwError(() => ({ status: 409 })));

    component.submit();

    expect(toastMock.showError).toHaveBeenCalledWith('Este e-mail já está em uso');
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('preserves document already registered error handling', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(
      throwError(() => ({
        status: 400,
        error: { documentAlreadyRegistered: true, emailConfirmationPending: false },
      })),
    );

    component.submit();

    expect(toastMock.showError).toHaveBeenCalledWith('CPF já cadastrado.');
  });

  it('navigates to email confirmation when the document is registered and confirmation is pending', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(
      throwError(() => ({
        status: 409,
        error: {
          documentAlreadyRegistered: true,
          emailConfirmationPending: true,
          userId: 'existing-user-9',
        },
      })),
    );

    component.submit();

    expect(toastMock.showSuccess).toHaveBeenCalledWith(
      'CPF já cadastrado. Um novo link de confirmação foi enviado para o seu e-mail.',
    );
    expect(toastMock.showError).not.toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com', userId: 'existing-user-9' } },
    );
  });

  it('navigates to email confirmation without a userId when the document response omits it', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(
      throwError(() => ({
        status: 409,
        error: {
          documentAlreadyRegistered: true,
          emailConfirmationPending: true,
        },
      })),
    );

    component.submit();

    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com' } },
    );
  });
});
