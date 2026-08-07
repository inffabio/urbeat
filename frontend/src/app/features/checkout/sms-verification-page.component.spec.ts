import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { SmsVerificationPageComponent } from './sms-verification-page.component';
import { AuthService } from '../../core/services/auth.service';
import { CheckoutService } from '../../core/services/checkout.service';

describe('SmsVerificationPageComponent', () => {
  let checkoutServiceMock: {
    verificationId: ReturnType<typeof signal<string | null>>;
    verificationExpiresAtUtc: ReturnType<typeof signal<string | null>>;
    verificationResendAvailableAtUtc: ReturnType<typeof signal<string | null>>;
    verificationMaskedPhone: ReturnType<typeof signal<string | null>>;
    customerAddressId: ReturnType<typeof signal<string | null>>;
    confirmCustomerVerification: jest.Mock;
    resendCustomerVerification: jest.Mock;
  };
  let authServiceMock: { saveToken: jest.Mock };
  let routerMock: { navigate: jest.Mock; url: string };

  beforeEach(async () => {
    jest.useFakeTimers();
    const now = new Date('2026-07-28T22:30:00.000Z');
    jest.setSystemTime(now);

    checkoutServiceMock = {
      verificationId: signal('verification-1'),
      verificationExpiresAtUtc: signal('2026-07-28T22:31:00.000Z'),
      verificationResendAvailableAtUtc: signal('2026-07-28T22:31:00.000Z'),
      verificationMaskedPhone: signal('*******9999'),
      customerAddressId: signal(null),
      confirmCustomerVerification: jest.fn().mockReturnValue(of({
        succeeded: true,
        accessToken: 'access-token',
        refreshToken: 'refresh-token',
        expiresAtUtc: '2026-07-28T22:45:00.000Z',
        refreshTokenExpiresAtUtc: '2026-08-04T22:30:00.000Z',
        customerAddressId: 'addr1',
      })),
      resendCustomerVerification: jest.fn().mockReturnValue(of({
        succeeded: true,
        expiresAtUtc: '2026-07-28T22:32:00.000Z',
        resendAvailableAtUtc: '2026-07-28T22:32:00.000Z',
      })),
    };
    authServiceMock = { saveToken: jest.fn() };
    routerMock = { navigate: jest.fn(), url: '/loja/checkout/confirmar-sms' };

    await TestBed.configureTestingModule({
      imports: [SmsVerificationPageComponent],
      providers: [
        { provide: CheckoutService, useValue: checkoutServiceMock },
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock },
        { provide: Location, useValue: { back: jest.fn() } },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('should render four digit inputs and the countdown', () => {
    const fixture = TestBed.createComponent(SmsVerificationPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.queryAll(By.css('.otp-input'))).toHaveLength(4);
    expect(fixture.debugElement.query(By.css('.timer')).nativeElement.textContent).toContain('01:00');
  });

  it('should paste four digits across inputs and confirm automatically', () => {
    const fixture = TestBed.createComponent(SmsVerificationPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.onPaste({ preventDefault: jest.fn(), clipboardData: { getData: () => '1234' } } as any);

    expect(checkoutServiceMock.confirmCustomerVerification).toHaveBeenCalledWith({ verificationId: 'verification-1', code: '1234' });
    expect(authServiceMock.saveToken).toHaveBeenCalledWith(expect.objectContaining({ accessToken: 'access-token' }));
    expect(checkoutServiceMock.customerAddressId()).toBe('addr1');
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'checkout', 'pagamento']);
  });

  it('should enable resend after countdown expires', () => {
    const fixture = TestBed.createComponent(SmsVerificationPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.canResend()).toBe(false);

    jest.advanceTimersByTime(60_000);
    fixture.detectChanges();

    expect(fixture.componentInstance.canResend()).toBe(true);
  });

  it('should show inline error when confirmation fails', () => {
    checkoutServiceMock.confirmCustomerVerification.mockReturnValue(throwError(() => ({ error: { error: 'Código inválido.' } })));
    const fixture = TestBed.createComponent(SmsVerificationPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.setDigit(0, '1');
    fixture.componentInstance.setDigit(1, '2');
    fixture.componentInstance.setDigit(2, '3');
    fixture.componentInstance.setDigit(3, '4');
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.verification-error')).nativeElement.textContent).toContain('Código inválido');
  });
});
