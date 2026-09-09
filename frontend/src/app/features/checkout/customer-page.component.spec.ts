import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { CustomerPageComponent } from './customer-page.component';
import { AddressService } from '../../core/services/address.service';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { StoreService } from '../../core/services/store.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';

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
  let authServiceMock: {
    saveToken: jest.Mock;
    customerProfile: ReturnType<typeof signal<any>>;
    restoreCustomerSession: jest.Mock;
    updateCustomerProfile: jest.Mock;
  };
  let routerMock: { navigate: jest.Mock; url: string };
  let storeServiceMock: { getStoreById: jest.Mock };
  let addressServiceMock: { lookupCep: jest.Mock; list: jest.Mock; update: jest.Mock; create: jest.Mock };
  let activatedRouteMock: { snapshot: { routeConfig: { path: string } } };
  let signalRServiceMock: { startCustomerHub: jest.Mock; invokeCustomerMethod: jest.Mock; joinStore: jest.Mock; leaveStore: jest.Mock; onCustomerEvent: jest.Mock; removeCustomerListener: jest.Mock };

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
        expiresAtUtc: '2026-07-28T22:45:00.000Z',
        customerAddressId: 'addr1',
      })),
    };
    authServiceMock = {
      saveToken: jest.fn(),
      customerProfile: signal(null),
      restoreCustomerSession: jest.fn(),
      updateCustomerProfile: jest.fn(),
    };
    addressServiceMock = {
      lookupCep: jest.fn(),
      list: jest.fn().mockReturnValue(of([])),
      update: jest.fn(),
      create: jest.fn().mockReturnValue(of({ id: 'addr1' })),
    };
    activatedRouteMock = { snapshot: { routeConfig: { path: 'checkout/cadastro' } } };
    routerMock = { navigate: jest.fn(), url: '/loja/checkout/cadastro' };
    storeServiceMock = {
      getStoreById: jest.fn().mockReturnValue(of({
        id: 'store-1',
        address: { city: 'Campos dos Goytacazes', zipCode: '28010-000', neighborhood: 'Centro' },
      })),
    };
    signalRServiceMock = {
      startCustomerHub: jest.fn().mockResolvedValue(undefined),
      invokeCustomerMethod: jest.fn(),
      joinStore: jest.fn().mockResolvedValue(undefined),
      leaveStore: jest.fn().mockResolvedValue(undefined),
      onCustomerEvent: jest.fn(),
      removeCustomerListener: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [CustomerPageComponent],
      providers: [
        CartService,
        { provide: CheckoutService, useValue: checkoutServiceMock },
        { provide: AddressService, useValue: addressServiceMock },
        { provide: AuthService, useValue: authServiceMock },
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ApiService, useValue: { get: jest.fn().mockReturnValue(of({ covered: true, deliveryFee: 0 })) } },
        { provide: SignalRService, useValue: signalRServiceMock },
        { provide: ToastService, useValue: { showWarning: jest.fn(), showGrouped: jest.fn(), showError: jest.fn(), showSuccess: jest.fn() } },
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: activatedRouteMock },
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

    const button = fixture.debugElement.query(By.css('app-sticky-action-bar button')).nativeElement as HTMLButtonElement;
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

  it('should show the delivery coverage modal instead of navigating when the neighborhood is not covered', () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;
    component.fullName.set('Maria Oliveira');
    component.onPhoneInput('(22) 99999-9999');
    component.email.set('maria@email.com');
    component.cep.set('28000-000');
    component.city.set('Campos dos Goytacazes');
    component.state.set('RJ');
    component.neighborhood.set('Jardim Aurora');
    component.street.set('Rua Principal');
    component.number.set('123');
    component.deliveryNotCovered.set(true);
    fixture.detectChanges();

    component.continue();
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.delivery-coverage-modal'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.delivery-coverage-message')).nativeElement.textContent)
      .toContain('Esta loja não faz entrega neste bairro');
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('loads and saves only the changed profile section in account mode', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    authServiceMock.updateCustomerProfile.mockReturnValue(of({
      fullName: 'Maria Atualizada',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    }));
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));
    addressServiceMock.update.mockReturnValue(of({ id: 'addr1' }));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.fullName.set('Maria Atualizada');
    component.updateAccountDirty();

    expect(component.isAccountEdit()).toBe(true);
    expect(component.canSaveAccount()).toBe(true);
    component.continue();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledWith(expect.objectContaining({ fullName: 'Maria Atualizada' }));
    expect(addressServiceMock.update).not.toHaveBeenCalled();
  });

  it('saves only the address section when only the address changed in account mode', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));
    addressServiceMock.update.mockReturnValue(of({ id: 'addr1' }));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.street.set('Rua Nova');
    component.updateAccountDirty();

    expect(component.canSaveAccount()).toBe(true);
    component.continue();

    expect(addressServiceMock.update).toHaveBeenCalledWith('addr1', expect.objectContaining({ street: 'Rua Nova' }));
    expect(authServiceMock.updateCustomerProfile).not.toHaveBeenCalled();
  });

  it('enables a profile-only save in account mode when the primary address is missing', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: null,
    });
    authServiceMock.updateCustomerProfile.mockReturnValue(of({
      fullName: 'Maria Atualizada',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: null,
    }));
    addressServiceMock.list.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.fullName.set('Maria Atualizada');
    component.updateAccountDirty();

    expect(component.canSaveAccount()).toBe(true);
    component.continue();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledWith(expect.objectContaining({ fullName: 'Maria Atualizada' }));
    expect(addressServiceMock.create).not.toHaveBeenCalled();
  });

  it('keeps the address dirty and reports a partial update when the address fails after the profile succeeded', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    authServiceMock.updateCustomerProfile.mockReturnValue(of({
      fullName: 'Maria Atualizada',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    }));
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));
    addressServiceMock.update.mockReturnValue(throwError(() => new Error('falha')));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.fullName.set('Maria Atualizada');
    component.street.set('Rua Nova');
    component.updateAccountDirty();

    expect(component.canSaveAccount()).toBe(true);
    component.continue();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledWith(expect.objectContaining({ fullName: 'Maria Atualizada' }));
    expect(addressServiceMock.update).toHaveBeenCalled();

    expect(component.accountProfileDirty()).toBe(false);
    expect(component.accountAddressDirty()).toBe(true);

    const toast = TestBed.inject(ToastService) as unknown as { showWarning: jest.Mock };
    expect(toast.showWarning).toHaveBeenCalledWith(expect.stringContaining('não foi possível salvar o endereço'));
  });

  it('does not save any section when the profile is changed but invalid', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.fullName.set('Ma');
    component.street.set('Rua Nova');
    component.updateAccountDirty();

    expect(component.accountProfileDirty()).toBe(true);
    expect(component.accountAddressDirty()).toBe(true);
    expect(component.canSaveAccount()).toBe(false);

    component.continue();

    expect(authServiceMock.updateCustomerProfile).not.toHaveBeenCalled();
    expect(addressServiceMock.update).not.toHaveBeenCalled();
    expect(component.attemptedSubmit()).toBe(true);
  });

  it('does not save any section when the address is changed but invalid', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.fullName.set('Maria Atualizada');
    component.street.set('');
    component.updateAccountDirty();

    expect(component.canSaveAccount()).toBe(false);

    component.continue();

    expect(authServiceMock.updateCustomerProfile).not.toHaveBeenCalled();
    expect(addressServiceMock.update).not.toHaveBeenCalled();
  });

  it('updates the profile baseline after a successful profile-only save so a second submit is a no-op', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    authServiceMock.updateCustomerProfile.mockReturnValue(of({
      fullName: 'Maria Atualizada',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    }));
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.fullName.set('Maria Atualizada');
    component.updateAccountDirty();

    component.continue();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledTimes(1);
    expect(component.accountProfileDirty()).toBe(false);
    expect(component.canSaveAccount()).toBe(false);

    component.continue();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledTimes(1);
  });

  it('updates the address baseline after a successful address-only save so a second submit is a no-op', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));
    addressServiceMock.update.mockReturnValue(of({ id: 'addr1' }));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.street.set('Rua Nova');
    component.updateAccountDirty();

    component.continue();

    expect(addressServiceMock.update).toHaveBeenCalledTimes(1);
    expect(component.accountAddressDirty()).toBe(false);
    expect(component.canSaveAccount()).toBe(false);

    component.continue();

    expect(addressServiceMock.update).toHaveBeenCalledTimes(1);
  });

  it('updates both baselines after a combined save so a second submit is a no-op', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    authServiceMock.updateCustomerProfile.mockReturnValue(of({
      fullName: 'Maria Atualizada',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    }));
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));
    addressServiceMock.update.mockReturnValue(of({ id: 'addr1' }));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.fullName.set('Maria Atualizada');
    component.street.set('Rua Nova');
    component.updateAccountDirty();

    component.continue();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledTimes(1);
    expect(addressServiceMock.update).toHaveBeenCalledTimes(1);
    expect(component.accountProfileDirty()).toBe(false);
    expect(component.accountAddressDirty()).toBe(false);
    expect(component.canSaveAccount()).toBe(false);

    component.continue();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledTimes(1);
    expect(addressServiceMock.update).toHaveBeenCalledTimes(1);
  });

  it('loads customer profile and address and scrolls to top when opening account edit', async () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const scrollSpy = jest.fn();
    (fixture.componentInstance as any).content = { scrollToTop: scrollSpy };
    await new Promise((resolve) => setTimeout(resolve, 0));

    const component = fixture.componentInstance;
    expect(component.isAccountEdit()).toBe(true);
    expect(component.fullName()).toBe('Maria Oliveira');
    expect(component.email()).toBe('maria@email.com');
    expect(component.phone()).toBe('22999999999');
    expect(component.cep()).toBe('28000000');
    expect(component.street()).toBe('Rua Principal');
    expect(scrollSpy).toHaveBeenCalled();
  });

  it('does not scroll to top during checkout', async () => {
    activatedRouteMock.snapshot.routeConfig.path = 'checkout/cadastro';

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const scrollSpy = jest.fn();
    (fixture.componentInstance as any).content = { scrollToTop: scrollSpy };
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(scrollSpy).not.toHaveBeenCalled();
  });

  it('renders a single Salvar button at the end of the form and no shared sticky action bar in account mode', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('app-sticky-action-bar'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.sticky-action-bar'))).toBeNull();

    const buttons = fixture.debugElement.queryAll(By.css('.account-save-btn'));
    expect(buttons.length).toBe(1);

    const form = fixture.debugElement.query(By.css('.checkout-form')).nativeElement as HTMLElement;
    const button = buttons[0].nativeElement as HTMLButtonElement;
    expect(form.contains(button)).toBe(true);
    expect(button.textContent).toContain('Salvar');
  });

  it('disables the account Salvar button while clean and enables it after a valid change', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.css('.account-save-btn')).nativeElement as HTMLButtonElement;
    expect(button.disabled).toBe(true);

    fixture.componentInstance.fullName.set('Maria Atualizada');
    fixture.componentInstance.updateAccountDirty();
    fixture.detectChanges();

    expect(button.disabled).toBe(false);
  });

  it('keeps the shared sticky action bar with the Continuar action in checkout mode', () => {
    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();

    const bar = fixture.debugElement.query(By.css('app-sticky-action-bar'));
    expect(bar).not.toBeNull();
    expect((bar.nativeElement as HTMLElement).textContent).toContain('Continuar');
    expect(fixture.debugElement.query(By.css('.account-save-btn'))).toBeNull();
  });

  it('submits the account save flow through the Salvar button', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    });
    authServiceMock.updateCustomerProfile.mockReturnValue(of({
      fullName: 'Maria Atualizada',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: 'addr1',
    }));
    addressServiceMock.list.mockReturnValue(of([{
      id: 'addr1', cep: '28000000', street: 'Rua Principal', number: '10',
      neighborhood: 'Centro', city: 'Campos', state: 'RJ', isPrimary: true,
    }]));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.fullName.set('Maria Atualizada');
    component.updateAccountDirty();
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.css('.account-save-btn')).nativeElement as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(authServiceMock.updateCustomerProfile).toHaveBeenCalledWith(expect.objectContaining({ fullName: 'Maria Atualizada' }));
  });

  it('removes the DeliveryAreaUpdated listener when subscribing again and on destroy', () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance as any;

    component.subscribeStoreDeliveryUpdates();
    component.subscribeStoreDeliveryUpdates();
    fixture.destroy();

    expect(signalRServiceMock.removeCustomerListener).toHaveBeenCalledTimes(2);
    expect(signalRServiceMock.removeCustomerListener).toHaveBeenNthCalledWith(1, 'DeliveryAreaUpdated', expect.any(Function));
    expect(signalRServiceMock.removeCustomerListener).toHaveBeenNthCalledWith(2, 'DeliveryAreaUpdated', expect.any(Function));
  });

  it('registers DeliveryAreaUpdated before starting the customer hub', () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance as any;

    component.subscribeStoreDeliveryUpdates();

    expect(signalRServiceMock.onCustomerEvent).toHaveBeenCalledWith('DeliveryAreaUpdated', expect.any(Function));
    expect(signalRServiceMock.startCustomerHub).toHaveBeenCalled();

    const onCustomerEventOrder = signalRServiceMock.onCustomerEvent.mock.invocationCallOrder[0];
    const startCustomerHubOrder = signalRServiceMock.startCustomerHub.mock.invocationCallOrder[0];
    expect(onCustomerEventOrder).toBeLessThan(startCustomerHubOrder);

    fixture.destroy();
  });

  it('does not JoinStore after the component is destroyed', async () => {
    cart.setStore('store-1', 'Loja', '');
    let resolveStart: (() => void) | undefined;
    signalRServiceMock.startCustomerHub.mockReturnValue(
      new Promise<void>((resolve) => {
        resolveStart = resolve;
      }),
    );

    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance as any;
    component.subscribeStoreDeliveryUpdates();
    fixture.destroy();

    resolveStart?.();
    await Promise.resolve();

    expect(signalRServiceMock.joinStore).not.toHaveBeenCalledWith('store-1');
  });

  it('joins the store only once across repeated subscriptions', async () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance as any;

    component.subscribeStoreDeliveryUpdates();
    component.subscribeStoreDeliveryUpdates();
    await Promise.resolve();

    expect(signalRServiceMock.joinStore).toHaveBeenCalledTimes(1);
    expect(signalRServiceMock.joinStore).toHaveBeenCalledWith('store-1');

    fixture.destroy();
  });

  it('leaves the store on destroy only after joining', async () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance as any;

    component.subscribeStoreDeliveryUpdates();
    await Promise.resolve();
    fixture.destroy();

    expect(signalRServiceMock.leaveStore).toHaveBeenCalledWith('store-1');
  });

  it('does not leave a store it never joined', () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.destroy();

    expect(signalRServiceMock.leaveStore).not.toHaveBeenCalled();
  });

  it('handles a JoinStore rejection without unhandled rejection', async () => {
    cart.setStore('store-1', 'Loja', '');
    signalRServiceMock.joinStore.mockRejectedValue(new Error('offline'));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance as any;
    component.subscribeStoreDeliveryUpdates();
    await Promise.resolve();

    expect(signalRServiceMock.joinStore).toHaveBeenCalledWith('store-1');

    fixture.destroy();
  });

  it('handles a LeaveStore rejection on destroy without unhandled rejection', async () => {
    cart.setStore('store-1', 'Loja', '');
    signalRServiceMock.leaveStore.mockRejectedValue(new Error('closed'));

    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance as any;
    component.subscribeStoreDeliveryUpdates();
    await Promise.resolve();
    fixture.destroy();
    await Promise.resolve();

    expect(signalRServiceMock.leaveStore).toHaveBeenCalledWith('store-1');
  });

  it('discards a stale CEP lookup response when a newer lookup resolves first', () => {
    const pending: ((res: any) => void)[] = [];
    addressServiceMock.lookupCep.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((res) => subscriber.next(res));
        }),
    );

    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;

    component.onCepInput('28000-000');
    component.onCepInput('28010-001');

    pending[1]({ street: 'Rua Nova', neighborhood: 'Jardim Aurora', city: 'Campos', state: 'RJ' });
    pending[0]({ street: 'Rua Antiga', neighborhood: 'Centro', city: 'Campos', state: 'RJ' });

    expect(component.street()).toBe('Rua Nova');
    expect(component.neighborhood()).toBe('Jardim Aurora');
  });

  it('does not apply a stale CEP lookup response after the CEP becomes incomplete', () => {
    const pending: ((res: any) => void)[] = [];
    addressServiceMock.lookupCep.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((res) => subscriber.next(res));
        }),
    );

    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;

    component.onCepInput('28000-000');
    component.onCepInput('28000');

    pending[0]({ street: 'Rua Antiga', neighborhood: 'Centro', city: 'Campos', state: 'RJ' });

    expect(component.street()).toBe('');
    expect(component.neighborhood()).toBe('');
  });

  it('discards a stale delivery coverage response when the neighborhood changes', () => {
    const apiServiceMock = TestBed.inject(ApiService) as unknown as { get: jest.Mock };
    const pending: ((res: any) => void)[] = [];
    apiServiceMock.get.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((res) => subscriber.next(res));
        }),
    );

    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;

    component.neighborhood.set('Centro');
    (component as any).checkDeliveryCoverage();
    component.neighborhood.set('Jardim Aurora');
    (component as any).checkDeliveryCoverage();

    pending[1]({ covered: false, deliveryFee: 5 });
    pending[0]({ covered: true, deliveryFee: 0 });

    expect(component.deliveryNotCovered()).toBe(true);
  });

  it('clears delivery check loading when the neighborhood changed before the response', () => {
    const apiServiceMock = TestBed.inject(ApiService) as unknown as { get: jest.Mock };
    const pending: ((res: any) => void)[] = [];
    apiServiceMock.get.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((res) => subscriber.next(res));
        }),
    );

    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;

    component.neighborhood.set('Centro');
    (component as any).checkDeliveryCoverage();
    component.neighborhood.set('Jardim Aurora');

    pending[0]({ covered: true, deliveryFee: 0 });

    expect(component.deliveryCheckLoading()).toBe(false);
    expect(component.deliveryNotCovered()).toBe(false);
  });

  it('clears deliveryNotCovered when the neighborhood is manually edited', () => {
    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;
    component.deliveryNotCovered.set(true);

    component.onAddressFieldChange('neighborhood', 'Centro');

    expect(component.deliveryNotCovered()).toBe(false);
  });

  it('discards an in-flight delivery check when the address is manually edited', () => {
    const apiServiceMock = TestBed.inject(ApiService) as unknown as { get: jest.Mock };
    const pending: ((res: any) => void)[] = [];
    apiServiceMock.get.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((res) => subscriber.next(res));
        }),
    );

    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;

    component.neighborhood.set('Centro');
    (component as any).checkDeliveryCoverage();
    component.onAddressFieldChange('street', 'Rua Nova');

    pending[0]({ covered: false, deliveryFee: 5 });

    expect(component.deliveryNotCovered()).toBe(false);
    expect(component.deliveryCheckLoading()).toBe(false);
  });

  it('does not update or navigate after destroy when createCustomerSession resolves', () => {
    const pending: ((session: any) => void)[] = [];
    checkoutServiceMock.createCustomerSession.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((session) => subscriber.next(session));
        }),
    );

    cart.setStore('store-1', 'Loja', '');
    const fixture = TestBed.createComponent(CustomerPageComponent);
    const component = fixture.componentInstance;
    component.fullName.set('Maria Oliveira');
    component.onPhoneInput('(22) 99999-9999');
    component.email.set('maria@email.com');
    component.cep.set('28000-000');
    component.city.set('Campos');
    component.state.set('RJ');
    component.neighborhood.set('Centro');
    component.street.set('Rua Principal');
    component.number.set('123');

    component.continue();
    fixture.destroy();

    pending[0]({ succeeded: true, accessToken: 't', customerAddressId: 'addr1' });

    expect(authServiceMock.saveToken).not.toHaveBeenCalled();
    expect(checkoutServiceMock.customerAddressId()).toBeNull();
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('cancels the account address list subscription when the page is destroyed', () => {
    activatedRouteMock.snapshot.routeConfig.path = 'conta/cadastro';
    authServiceMock.customerProfile.set({
      fullName: 'Maria Oliveira',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
      primaryAddressId: null,
    });
    let unsubscribed = false;
    addressServiceMock.list.mockReturnValue(
      new Observable(() => () => {
        unsubscribed = true;
      }),
    );

    const fixture = TestBed.createComponent(CustomerPageComponent);
    fixture.detectChanges();
    fixture.destroy();

    expect(unsubscribed).toBe(true);
  });
});

describe('CustomerPageComponent footer clearance', () => {
  it('keeps the account form clear of the fixed footer via the measured variable', () => {
    const styles = readFileSync(resolve(__dirname, 'customer-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.checkout-form--account\s*\{[\s\S]*padding-bottom:\s*calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 24px\)/);
  });

  it('keeps the shared-fixed-action-bar form clear by reserving footer clearance plus action-bar space', () => {
    const styles = readFileSync(resolve(__dirname, 'customer-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.checkout-form\s*\{[\s\S]*padding:\s*0 18px calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 56px\)/);
    expect(styles).not.toMatch(/--checkout-action-space/);
  });
});
