import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { of } from 'rxjs';

import { CustomerPageComponent } from './customer-page.component';
import { AddressService } from '../../core/services/address.service';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { AuthService } from '../../core/services/auth.service';

describe('CustomerPageComponent', () => {
  let cart: CartService;
  let checkoutServiceMock: {
    fulfillmentType: ReturnType<typeof signal<FulfillmentType>>;
    customerInfo: ReturnType<typeof signal<any>>;
    customerAddress: ReturnType<typeof signal<any>>;
    customerAddressId: ReturnType<typeof signal<string | null>>;
    verificationId: ReturnType<typeof signal<string | null>>;
    verificationExpiresAtUtc: ReturnType<typeof signal<string | null>>;
    verificationResendAvailableAtUtc: ReturnType<typeof signal<string | null>>;
    verificationMaskedPhone: ReturnType<typeof signal<string | null>>;
    startCustomerVerification: jest.Mock;
    createCustomerSession: jest.Mock;
  };
  let authServiceMock: { saveToken: jest.Mock };
  let routerMock: { navigate: jest.Mock; url: string };

  beforeEach(async () => {
    localStorage.clear();

    checkoutServiceMock = {
      fulfillmentType: signal(FulfillmentType.Delivery),
      customerInfo: signal(null),
      customerAddress: signal(null),
      customerAddressId: signal(null),
      verificationId: signal(null),
      verificationExpiresAtUtc: signal(null),
      verificationResendAvailableAtUtc: signal(null),
      verificationMaskedPhone: signal(null),
      startCustomerVerification: jest.fn().mockReturnValue(of({
        verificationId: 'verification-1',
        expiresAtUtc: '2026-07-28T22:31:00.000Z',
        resendAvailableAtUtc: '2026-07-28T22:31:00.000Z',
        maskedPhone: '*******9999',
      })),
      createCustomerSession: jest.fn().mockReturnValue(of({
        succeeded: true,
        accessToken: 'access-token',
        refreshToken: 'refresh-token',
        expiresAtUtc: '2026-07-28T22:45:00.000Z',
        refreshTokenExpiresAtUtc: '2026-08-04T22:30:00.000Z',
        customerAddressId: 'addr1',
      })),
    };
    authServiceMock = { saveToken: jest.fn() };
    routerMock = { navigate: jest.fn(), url: '/loja/checkout/cadastro' };

    await TestBed.configureTestingModule({
      imports: [CustomerPageComponent],
      providers: [
        CartService,
        { provide: CheckoutService, useValue: checkoutServiceMock },
        { provide: AddressService, useValue: { lookupCep: jest.fn(), create: jest.fn().mockReturnValue(of({ id: 'addr1' })) } },
        { provide: AuthService, useValue: authServiceMock },
        { provide: ApiService, useValue: { get: jest.fn().mockReturnValue(of({ covered: true, deliveryFee: 0 })) } },
        { provide: SignalRService, useValue: { startCustomerHub: jest.fn().mockResolvedValue(undefined), invokeCustomerMethod: jest.fn(), onCustomerEvent: jest.fn() } },
        { provide: ToastService, useValue: { showWarning: jest.fn(), showGrouped: jest.fn(), showError: jest.fn() } },
        { provide: Router, useValue: routerMock },
        { provide: Location, useValue: { back: jest.fn() } },
      ],
    }).compileComponents();

    cart = TestBed.inject(CartService);
  });

  it('should render visible labels for customer and address fields', () => {
    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();

    const labels = fixture.debugElement
      .queryAll(By.css('.field-label'))
      .map(label => (label.nativeElement as HTMLElement).textContent?.trim());

    expect(labels).toEqual(expect.arrayContaining([
      'Nome completo',
      'Celular',
      'E-mail',
      'CEP',
      'Cidade',
      'UF',
      'Bairro',
      'Rua',
      'Número',
      'Complemento',
    ]));
  });

  it('should connect the CEP input to the visible CEP error message', () => {
    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.componentInstance.cepError.set(true);
    fixture.detectChanges();

    const cepInput = fixture.debugElement.query(By.css('input[name="cep"]')).nativeElement as HTMLInputElement;
    const error = fixture.debugElement.query(By.css('#cep-error')).nativeElement as HTMLElement;

    expect(cepInput.getAttribute('aria-describedby')).toBe('cep-error');
    expect(error.textContent).toContain('CEP não encontrado');
  });

  it('should render checkout when persisted cart items do not have ids', () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    cart.items.set([
      { productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 } as any,
      { productId: 'p2', productName: 'Batata', quantity: 1, unitPrice: 10 } as any,
    ]);

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();

    const title = fixture.debugElement.query(By.css('.form-section-title')).nativeElement as HTMLElement;
    expect(title.textContent).toContain('Seus dados');
    expect(warnSpy).not.toHaveBeenCalledWith(expect.stringContaining('NG0955'));
    warnSpy.mockRestore();
  });

  it('should disable continue until required customer and address fields are valid', () => {
    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.css('.submit-btn')).nativeElement as HTMLButtonElement;
    expect(button.disabled).toBe(true);

    fixture.componentInstance.fullName.set('Maria Oliveira');
    fixture.componentInstance.onPhoneInput('(22) 99999-9999');
    fixture.componentInstance.email.set('maria@email.com');
    fixture.componentInstance.cep.set('28000-000');
    fixture.componentInstance.city.set('Campos dos Goytacazes');
    fixture.componentInstance.state.set('RJ');
    fixture.componentInstance.neighborhood.set('Centro');
    fixture.componentInstance.street.set('Rua Principal');
    fixture.componentInstance.number.set('123');
    fixture.detectChanges();

    expect(button.disabled).toBe(false);
  });

  it('should show inline criticism after a required field is touched', () => {
    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.componentInstance.markTouched('fullName');
    fixture.detectChanges();

    const error = fixture.debugElement.query(By.css('#fullName-error')).nativeElement as HTMLElement;
    const input = fixture.debugElement.query(By.css('input[name="fullName"]')).nativeElement as HTMLInputElement;

    expect(error.textContent).toContain('Informe seu nome completo.');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe('fullName-error');
  });

  it('should create a customer session and navigate to payment when form is valid', () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.componentInstance.fullName.set('Maria Oliveira');
    fixture.componentInstance.onPhoneInput('(22) 99999-9999');
    fixture.componentInstance.email.set('maria@email.com');
    fixture.componentInstance.cep.set('28000-000');
    fixture.componentInstance.city.set('Campos dos Goytacazes');
    fixture.componentInstance.state.set('RJ');
    fixture.componentInstance.neighborhood.set('Centro');
    fixture.componentInstance.street.set('Rua Principal');
    fixture.componentInstance.number.set('123');

    fixture.componentInstance.continue();

    expect(checkoutServiceMock.createCustomerSession).toHaveBeenCalledWith(expect.objectContaining({
      storeId: 'store-1',
      customer: expect.objectContaining({ email: 'maria@email.com' }),
    }));
    expect(authServiceMock.saveToken).toHaveBeenCalledWith(expect.objectContaining({ accessToken: 'access-token' }));
    expect(checkoutServiceMock.customerAddressId()).toBe('addr1');
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'checkout', 'pagamento']);
  });
});
