import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';

import { AddressService } from '../../core/services/address.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { CustomerAddress } from '../../shared/models/address.model';
import { CustomerProfileResponse } from '../../shared/models/auth.model';
import { CustomerProfileFormComponent } from '../../shared/components/customer-profile-form/customer-profile-form.component';
import { CustomerAccountPageComponent } from './customer-account-page.component';

describe('CustomerAccountPageComponent', () => {
  let fixture: ComponentFixture<CustomerAccountPageComponent>;
  let component: CustomerAccountPageComponent;

  const profile: CustomerProfileResponse = {
    fullName: 'Maria Oliveira',
    email: 'maria@email.com',
    phoneNumber: '22999999999',
    primaryAddressId: 'addr-1',
  };

  const primaryAddress: CustomerAddress = {
    id: 'addr-1',
    cep: '24000000',
    street: 'Avenida Brasil',
    number: '100',
    neighborhood: 'Centro',
    city: 'Niterói',
    state: 'RJ',
    isPrimary: true,
  };

  const authMock = {
    customerProfile: signal<CustomerProfileResponse | null>(profile),
    restoreCustomerSession: jest.fn().mockReturnValue(of(profile)),
    updateCustomerProfile: jest.fn().mockReturnValue(of({ ...profile, fullName: 'Maria Souza' })),
  };

  const addressMock = {
    list: jest.fn().mockReturnValue(of([primaryAddress])),
    update: jest.fn().mockReturnValue(of({ ...primaryAddress, number: '200' })),
    create: jest.fn().mockReturnValue(of(primaryAddress)),
    lookupCep: jest.fn(),
  };

  const toastMock = { showError: jest.fn(), showWarning: jest.fn(), showSuccess: jest.fn() };
  const routerMock = { navigate: jest.fn(), url: '/loja/conta/cadastro' };
  const locationMock = { back: jest.fn() };

  beforeEach(async () => {
    jest.clearAllMocks();
    authMock.customerProfile.set(profile);
    authMock.updateCustomerProfile.mockReturnValue(of({ ...profile, fullName: 'Maria Souza' }));
    addressMock.list.mockReturnValue(of([primaryAddress]));

    await TestBed.configureTestingModule({
      imports: [CustomerAccountPageComponent],
      providers: [
        { provide: AuthService, useValue: authMock },
        { provide: AddressService, useValue: addressMock },
        { provide: ToastService, useValue: toastMock },
        { provide: Router, useValue: routerMock },
        { provide: Location, useValue: locationMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomerAccountPageComponent);
    component = fixture.componentInstance;
  });

  function child(): CustomerProfileFormComponent {
    return fixture.debugElement.query(By.directive(CustomerProfileFormComponent)).componentInstance;
  }

  it('loads the authenticated profile and primary address', () => {
    fixture.detectChanges();

    expect(component.loading()).toBe(false);
    expect(component.primaryAddress()).toEqual(primaryAddress);
    expect(child().fullName()).toBe('Maria Oliveira');
    expect(child().street()).toBe('Avenida Brasil');
  });

  it('redirects to the storefront when the session cannot be restored', () => {
    authMock.customerProfile.set(null);
    authMock.restoreCustomerSession.mockReturnValueOnce(throwError(() => new Error('no session')));

    fixture.detectChanges();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('tracks the valid dirty state from the shared form', () => {
    fixture.detectChanges();

    expect(component.canSave()).toBe(false);

    child().onAddressFieldChange('number', '200');
    fixture.detectChanges();

    expect(component.canSave()).toBe(true);
  });

  it('saves the profile then the primary address', () => {
    fixture.detectChanges();

    component.onValidSubmit({
      profile: { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: { cep: '24000000', street: 'Avenida Brasil', number: '200', neighborhood: 'Centro', city: 'Niterói', state: 'RJ', isPrimary: true },
      saveProfile: true,
      saveAddress: true,
    });

    expect(authMock.updateCustomerProfile).toHaveBeenCalledWith({
      fullName: 'Maria Souza',
      email: 'maria@email.com',
      phoneNumber: '22999999999',
    });
    expect(addressMock.update).toHaveBeenCalledWith('addr-1', expect.objectContaining({ number: '200' }));
    expect(toastMock.showSuccess).toHaveBeenCalled();
    expect(component.saving()).toBe(false);
  });

  it('creates the address when the customer has none yet', () => {
    addressMock.list.mockReturnValueOnce(of([]));
    fixture.detectChanges();

    component.onValidSubmit({
      profile: { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: { cep: '24000000', street: 'Avenida Brasil', number: '200', neighborhood: 'Centro', city: 'Niterói', state: 'RJ', isPrimary: true },
      saveProfile: true,
      saveAddress: true,
    });

    expect(addressMock.create).toHaveBeenCalled();
  });

  it('recovers when the profile update fails', () => {
    authMock.updateCustomerProfile.mockReturnValueOnce(throwError(() => new Error('down')));
    fixture.detectChanges();

    component.onValidSubmit({
      profile: { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: { cep: '24000000', street: 'Avenida Brasil', number: '200', neighborhood: 'Centro', city: 'Niterói', state: 'RJ', isPrimary: true },
      saveProfile: true,
      saveAddress: true,
    });

    expect(toastMock.showError).toHaveBeenCalled();
    expect(component.saving()).toBe(false);
    expect(addressMock.update).not.toHaveBeenCalled();
  });

  it('warns when the address update fails after the profile is saved', () => {
    addressMock.update.mockReturnValueOnce(throwError(() => new Error('down')));
    fixture.detectChanges();

    component.onValidSubmit({
      profile: { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: { cep: '24000000', street: 'Avenida Brasil', number: '200', neighborhood: 'Centro', city: 'Niterói', state: 'RJ', isPrimary: true },
      saveProfile: true,
      saveAddress: true,
    });

    expect(toastMock.showWarning).toHaveBeenCalled();
    expect(component.saving()).toBe(false);
  });

  it('saves only the profile when only the profile changed', () => {
    fixture.detectChanges();

    component.onValidSubmit({
      profile: { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: { cep: '24000000', street: 'Avenida Brasil', number: '200', neighborhood: 'Centro', city: 'Niterói', state: 'RJ', isPrimary: true },
      saveProfile: true,
      saveAddress: false,
    });

    expect(authMock.updateCustomerProfile).toHaveBeenCalled();
    expect(addressMock.update).not.toHaveBeenCalled();
    expect(addressMock.create).not.toHaveBeenCalled();
    expect(toastMock.showSuccess).toHaveBeenCalled();
    expect(component.saving()).toBe(false);
  });

  it('saves only the address when only the address changed', () => {
    fixture.detectChanges();

    component.onValidSubmit({
      profile: { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: { cep: '24000000', street: 'Avenida Brasil', number: '200', neighborhood: 'Centro', city: 'Niterói', state: 'RJ', isPrimary: true },
      saveProfile: false,
      saveAddress: true,
    });

    expect(authMock.updateCustomerProfile).not.toHaveBeenCalled();
    expect(addressMock.update).toHaveBeenCalledWith('addr-1', expect.objectContaining({ number: '200' }));
    expect(toastMock.showSuccess).toHaveBeenCalled();
    expect(component.saving()).toBe(false);
  });

  it('reports an address-only failure without claiming the profile was saved', () => {
    addressMock.update.mockReturnValueOnce(throwError(() => new Error('down')));
    fixture.detectChanges();

    component.onValidSubmit({
      profile: { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: { cep: '24000000', street: 'Avenida Brasil', number: '200', neighborhood: 'Centro', city: 'Niterói', state: 'RJ', isPrimary: true },
      saveProfile: false,
      saveAddress: true,
    });

    expect(authMock.updateCustomerProfile).not.toHaveBeenCalled();
    expect(toastMock.showError).toHaveBeenCalled();
    expect(toastMock.showWarning).not.toHaveBeenCalled();
    expect(component.saving()).toBe(false);
  });
});
