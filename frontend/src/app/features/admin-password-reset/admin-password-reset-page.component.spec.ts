import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AdminAccountService } from '../../core/services/admin-account.service';
import { AdminPasswordResetPageComponent } from './admin-password-reset-page.component';

describe('AdminPasswordResetPageComponent', () => {
  let fixture: ComponentFixture<AdminPasswordResetPageComponent>;
  let service: {
    validatePasswordResetToken: jest.Mock;
    confirmPasswordReset: jest.Mock;
  };
  let location: { replaceState: jest.Mock };

  function create(pathWithHash: string): void {
    window.history.replaceState(null, '', pathWithHash);
    fixture = TestBed.createComponent(AdminPasswordResetPageComponent);
    fixture.detectChanges();
  }

  beforeEach(() => {
    service = {
      validatePasswordResetToken: jest.fn(() =>
        of({ valid: true, purpose: 'PasswordReset', emailMasked: 'a***@exemplo.com' }),
      ),
      confirmPasswordReset: jest.fn(() =>
        of({
          succeeded: true,
          purpose: 'PasswordReset',
          message: 'Senha atualizada. Entre com a nova senha.',
        }),
      ),
    };
    location = { replaceState: jest.fn() };

    TestBed.configureTestingModule({
      imports: [AdminPasswordResetPageComponent],
      providers: [
        provideRouter([]),
        { provide: AdminAccountService, useValue: service },
        { provide: Location, useValue: location },
      ],
    });
  });

  it('reads and removes the fragment before validating the reset token', () => {
    create('/redefinir-senha-admin#token=reset-token');

    expect(location.replaceState).toHaveBeenCalledWith('/redefinir-senha-admin', '');
    expect(service.validatePasswordResetToken).toHaveBeenCalledWith('reset-token');
    expect(fixture.componentInstance.state()).toBe('form');
  });

  it('rejects a missing token and a server-rejected token', () => {
    create('/redefinir-senha-admin');
    expect(fixture.componentInstance.state()).toBe('invalid');

    service.validatePasswordResetToken.mockReturnValue(of({ valid: false }));
    create('/redefinir-senha-admin#token=expirado');
    expect(fixture.componentInstance.state()).toBe('invalid');
    expect(fixture.nativeElement.textContent).toContain('Link inválido ou expirado');
  });

  it('confirms the new password and redirects the user to the admin login', () => {
    create('/redefinir-senha-admin#token=reset-token');

    fixture.componentInstance.onSubmitted({
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
    fixture.detectChanges();

    expect(service.confirmPasswordReset).toHaveBeenCalledWith({
      token: 'reset-token',
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
    expect(fixture.componentInstance.state()).toBe('success');
    expect(fixture.nativeElement.textContent).toContain('Senha atualizada');
    expect(fixture.nativeElement.querySelector('a[href="/painel/login"]')).not.toBeNull();
  });

  it('shows the server message for an expired link', () => {
    service.confirmPasswordReset.mockReturnValue(
      throwError(() => ({ error: { code: 'INVALID_OR_EXPIRED_TOKEN' } })),
    );
    create('/redefinir-senha-admin#token=reset-token');

    fixture.componentInstance.onSubmitted({
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Este link é inválido ou expirou');
  });
});
