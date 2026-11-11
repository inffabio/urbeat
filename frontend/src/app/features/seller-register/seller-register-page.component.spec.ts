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

  function submit(response: typeof registerResponse & {
    rolePromotionPending?: boolean;
    roleAddedToExistingAccount?: boolean;
  }): void {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(of(response));
    component.submit();
  }

  it('keeps the signed-in customer session when adding a seller access', () => {
    authServiceMock.isAuthenticated.mockReturnValue(true);

    submit(registerResponse);

    expect(authServiceMock.logout).not.toHaveBeenCalled();
  });

  it('never sends an existing account password', () => {
    submit(registerResponse);

    const request = authServiceMock.registerSeller.mock.calls[0][0];
    expect(request).not.toHaveProperty('currentPassword');
  });

  it('does not render the current-password field nor keep its state', () => {
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('#currentPassword')).toBeNull();
    expect(html.textContent).not.toContain('Já tem conta urbeat?');
    expect((component as unknown as Record<string, unknown>)['currentPassword']).toBeUndefined();
    expect((component as unknown as Record<string, unknown>)['showCurrentPassword']).toBeUndefined();
  });

  it('sends the document without relying on untyped casts', () => {
    fillValidForm();
    component.document.set('123.456.789-09');
    authServiceMock.registerSeller.mockReturnValue(of(registerResponse));

    component.submit();

    expect(authServiceMock.registerSeller).toHaveBeenCalledWith(
      expect.objectContaining({ document: '12345678909' }),
    );
  });

  it('explains that the customer password was preserved after promoting an existing account', () => {
    submit({ ...registerResponse, roleAddedToExistingAccount: true, emailConfirmationPending: false });

    expect(toastMock.showSuccess).toHaveBeenCalledWith(
      'Acesso lojista criado! Sua senha de cliente continua a mesma.',
    );
  });

  it('uses a generic conflict message that never asks for an existing password', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(throwError(() => ({ status: 409 })));

    component.submit();

    expect(toastMock.showError).toHaveBeenCalledWith(
      'Não foi possível adicionar o acesso lojista com este e-mail.',
    );
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('shows the pending promotion message and keeps the session when e-mail ownership must be confirmed', () => {
    authServiceMock.isAuthenticated.mockReturnValue(true);
    submit({ ...registerResponse, rolePromotionPending: true, emailConfirmationPending: false });

    expect(toastMock.showSuccess).toHaveBeenCalledWith(
      'Se este e-mail já pertence a uma conta urbeat, enviamos um link para confirmar o novo acesso de lojista. Sua senha atual não foi alterada.',
    );
    expect(authServiceMock.logout).not.toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com', promotionPending: true, role: 'Seller' } },
    );
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

  it('allows registering a seller whose contractor name matches an existing seller', () => {
    fillValidForm();
    component.fullName.set('Contratante Teste');
    authServiceMock.registerSeller.mockReturnValue(of(registerResponse));

    component.submit();

    expect(authServiceMock.registerSeller).toHaveBeenCalledWith(
      expect.objectContaining({ fullName: 'Contratante Teste' }),
    );
    expect(component.errors()['fullName']).toBeUndefined();
    expect(toastMock.showError).not.toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
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
