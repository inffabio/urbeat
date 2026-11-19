import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AdminAccountService } from '../../core/services/admin-account.service';
import { AdminInvitePageComponent } from './admin-invite-page.component';

describe('AdminInvitePageComponent', () => {
  let fixture: ComponentFixture<AdminInvitePageComponent>;
  let service: {
    validateInvitationToken: jest.Mock;
    acceptInvitation: jest.Mock;
  };
  let location: { replaceState: jest.Mock };

  function create(pathWithHash: string): void {
    window.history.replaceState(null, '', pathWithHash);
    fixture = TestBed.createComponent(AdminInvitePageComponent);
    fixture.detectChanges();
  }

  beforeEach(() => {
    service = {
      validateInvitationToken: jest.fn(() => of({ valid: true, emailMasked: 'a***@exemplo.com' })),
      acceptInvitation: jest.fn(() =>
        of({
          succeeded: true,
          purpose: 'AccountInvitation',
          message: 'Conta ativada. Entre com sua nova senha.',
        }),
      ),
    };
    location = { replaceState: jest.fn() };

    TestBed.configureTestingModule({
      imports: [AdminInvitePageComponent],
      providers: [
        provideRouter([]),
        { provide: AdminAccountService, useValue: service },
        { provide: Location, useValue: location },
      ],
    });
  });

  it('reads the token from the fragment and strips it before validating', () => {
    create('/convite-admin#token=opaque-token');

    expect(location.replaceState).toHaveBeenCalledWith('/convite-admin', '');
    expect(service.validateInvitationToken).toHaveBeenCalledWith('opaque-token');
    expect(fixture.componentInstance.state()).toBe('form');
    expect(fixture.nativeElement.textContent).toContain('a***@exemplo.com');
  });

  it('shows the invalid state when the fragment has no token', () => {
    create('/convite-admin');

    expect(location.replaceState).not.toHaveBeenCalled();
    expect(fixture.componentInstance.state()).toBe('invalid');
    expect(fixture.nativeElement.textContent).toContain('Link inválido ou expirado');
  });

  it('shows the invalid state when the server rejects the token', () => {
    service.validateInvitationToken.mockReturnValue(of({ valid: false }));
    create('/convite-admin#token=expirado');

    expect(fixture.componentInstance.state()).toBe('invalid');
    expect(fixture.nativeElement.textContent).toContain('Peça um novo convite');
    expect(fixture.nativeElement.querySelector('a[href="/painel/login"]')).not.toBeNull();
  });

  it('activates the account when the visible form is submitted', () => {
    create('/convite-admin#token=opaque-token');

    const newPassword = fixture.nativeElement.querySelector('#new-password') as HTMLInputElement;
    newPassword.value = 'Senha1234';
    newPassword.dispatchEvent(new Event('input'));
    const confirmPassword = fixture.nativeElement.querySelector(
      '#confirm-password',
    ) as HTMLInputElement;
    confirmPassword.value = 'Senha1234';
    confirmPassword.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form.password-form') as HTMLFormElement;
    const submit = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submit);
    fixture.detectChanges();

    expect(submit.defaultPrevented).toBe(true);
    expect(service.acceptInvitation).toHaveBeenCalledWith({
      token: 'opaque-token',
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
    expect(fixture.componentInstance.state()).toBe('success');
  });

  it('activates the account and offers the admin login', () => {
    create('/convite-admin#token=opaque-token');

    fixture.componentInstance.onSubmitted({
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
    fixture.detectChanges();

    expect(service.acceptInvitation).toHaveBeenCalledWith({
      token: 'opaque-token',
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
    expect(fixture.componentInstance.state()).toBe('success');
    expect(fixture.nativeElement.textContent).toContain('Acesso ativado');
    expect(fixture.nativeElement.querySelector('a[href="/painel/login"]')).not.toBeNull();
  });

  it('explains an already used link', () => {
    service.acceptInvitation.mockReturnValue(
      throwError(() => ({ error: { code: 'TOKEN_ALREADY_USED' } })),
    );
    create('/convite-admin#token=opaque-token');

    fixture.componentInstance.onSubmitted({
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
    fixture.detectChanges();

    expect(fixture.componentInstance.state()).toBe('form');
    expect(fixture.nativeElement.textContent).toContain('Este link já foi usado');
  });
});
