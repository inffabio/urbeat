import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AddressService } from '../../../core/services/address.service';
import { CustomerAddress } from '../../models/address.model';
import { customerProfileFieldError } from '../../utils/customer-profile.rules';
import { CustomerProfileFormComponent } from './customer-profile-form.component';

describe('CustomerProfileFormComponent', () => {
  let fixture: ComponentFixture<CustomerProfileFormComponent>;
  let component: CustomerProfileFormComponent;
  let addressService: {
    lookupCep: jest.Mock;
    list: jest.Mock;
    create: jest.Mock;
    update: jest.Mock;
  };

  const primaryAddress: CustomerAddress = {
    id: 'addr-1',
    cep: '24000000',
    street: 'Avenida Brasil',
    number: '100',
    neighborhood: 'Centro',
    city: 'Niterói',
    state: 'RJ',
    complement: 'Apto 1',
    isPrimary: true,
  };

  beforeEach(async () => {
    addressService = {
      lookupCep: jest.fn().mockReturnValue(of({ cep: '24000-000', street: 'Rua A', neighborhood: 'Centro', city: 'Niterói', state: 'RJ' })),
      list: jest.fn(),
      create: jest.fn(),
      update: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [CustomerProfileFormComponent],
      providers: [{ provide: AddressService, useValue: addressService }],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomerProfileFormComponent);
    component = fixture.componentInstance;
  });

  function fillValidForm(): void {
    component.onFieldInput('fullName', 'Maria Oliveira');
    component.onPhoneInput('22999999999');
    component.onFieldInput('email', 'maria@email.com');
    component.onAddressFieldChange('cep', '24000000');
    component.onAddressFieldChange('city', 'Niterói');
    component.onAddressFieldChange('state', 'rj');
    component.onAddressFieldChange('neighborhood', 'Centro');
    component.onAddressFieldChange('street', 'Avenida Brasil');
    component.onAddressFieldChange('number', '100');
  }

  it('loads the initial profile and address in edit mode', () => {
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initialProfile', { fullName: 'Maria Oliveira', email: 'maria@email.com', phoneNumber: '22999999999' });
    fixture.componentRef.setInput('initialAddress', primaryAddress);
    fixture.detectChanges();

    expect(component.fullName()).toBe('Maria Oliveira');
    expect(component.email()).toBe('maria@email.com');
    expect(component.phone()).toBe('22999999999');
    expect(component.street()).toBe('Avenida Brasil');
    expect(component.city()).toBe('Niterói');
    expect(component.canSubmit()).toBe(false);
  });

  it('keeps save disabled while clean and enables it after a valid change', () => {
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initialProfile', { fullName: 'Maria Oliveira', email: 'maria@email.com', phoneNumber: '22999999999' });
    fixture.componentRef.setInput('initialAddress', primaryAddress);
    fixture.detectChanges();

    expect(component.canSubmit()).toBe(false);

    component.onAddressFieldChange('number', '200');
    fixture.detectChanges();

    expect(component.canSubmit()).toBe(true);
  });

  it('allows saving only the profile when the account has no address yet', () => {
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initialProfile', { fullName: 'Maria Oliveira', email: 'maria@email.com', phoneNumber: '22999999999' });
    fixture.detectChanges();

    component.onFieldInput('fullName', 'Maria Souza');
    fixture.detectChanges();

    expect(component.isProfileDirty()).toBe(true);
    expect(component.isAddressDirty()).toBe(false);
    expect(component.canSubmit()).toBe(true);

    const submit = jest.fn();
    component.validSubmit.subscribe(submit);
    component.submit();

    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ saveProfile: true, saveAddress: false }));
  });

  it('allows saving only the address when the profile is untouched', () => {
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initialProfile', { fullName: 'Maria Oliveira', email: 'maria@email.com', phoneNumber: '22999999999' });
    fixture.componentRef.setInput('initialAddress', primaryAddress);
    fixture.detectChanges();

    component.onAddressFieldChange('number', '200');
    fixture.detectChanges();

    expect(component.isAddressDirty()).toBe(true);
    expect(component.isProfileDirty()).toBe(false);
    expect(component.canSubmit()).toBe(true);

    const submit = jest.fn();
    component.validSubmit.subscribe(submit);
    component.submit();

    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ saveProfile: false, saveAddress: true }));
  });

  it('keeps checkout requiring both profile and address', () => {
    component.onFieldInput('fullName', 'Maria Oliveira');
    component.onPhoneInput('22999999999');
    component.onFieldInput('email', 'maria@email.com');
    fixture.detectChanges();

    expect(component.canSubmit()).toBe(false);

    component.onAddressFieldChange('cep', '24000000');
    component.onAddressFieldChange('city', 'Niterói');
    component.onAddressFieldChange('state', 'rj');
    component.onAddressFieldChange('neighborhood', 'Centro');
    component.onAddressFieldChange('street', 'Avenida Brasil');
    component.onAddressFieldChange('number', '100');
    fixture.detectChanges();

    expect(component.canSubmit()).toBe(true);
  });

  it('emits dirtyChange reflecting valid dirty state', () => {
    const dirty = jest.fn();
    component.dirtyChange.subscribe(dirty);

    fillValidForm();
    fixture.detectChanges();

    expect(dirty).toHaveBeenLastCalledWith(true);
  });

  it('does not submit while invalid', () => {
    const submit = jest.fn();
    component.validSubmit.subscribe(submit);

    component.onFieldInput('fullName', 'M');
    component.submit();

    expect(submit).not.toHaveBeenCalled();
    expect(component.showFieldError('fullName')).toBe(true);
  });

  it('emits the validated profile and address on submit', () => {
    const submit = jest.fn();
    component.validSubmit.subscribe(submit);

    fillValidForm();
    fixture.detectChanges();
    component.submit();

    expect(submit).toHaveBeenCalledWith({
      profile: { fullName: 'Maria Oliveira', email: 'maria@email.com', phoneNumber: '22999999999' },
      address: {
        cep: '24000000',
        street: 'Avenida Brasil',
        number: '100',
        complement: undefined,
        neighborhood: 'Centro',
        city: 'Niterói',
        state: 'RJ',
        isPrimary: true,
      },
      saveProfile: true,
      saveAddress: true,
    });
  });

  it('fills the address from the CEP lookup and clears on failure', () => {
    component.onCepInput('24000000');
    fixture.detectChanges();

    expect(addressService.lookupCep).toHaveBeenCalledWith('24000000');
    expect(component.street()).toBe('Rua A');
    expect(component.cepError()).toBe(false);

    addressService.lookupCep.mockReturnValueOnce(throwError(() => new Error('down')));
    component.onCepInput('24000001');
    fixture.detectChanges();

    expect(component.cepError()).toBe(true);
  });

  it('formats the phone mask while typing', () => {
    component.onPhoneInput('22999999999');
    expect(component.phone()).toBe('(22) 99999-9999');
  });

  it('preserves address edits when the profile input refreshes after a partial save', () => {
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initialProfile', { fullName: 'Maria Oliveira', email: 'maria@email.com', phoneNumber: '22999999999' });
    fixture.componentRef.setInput('initialAddress', primaryAddress);
    fixture.detectChanges();

    component.onFieldInput('fullName', 'Maria Souza');
    component.onAddressFieldChange('number', '200');
    fixture.detectChanges();

    expect(component.isAddressDirty()).toBe(true);
    expect(component.canSubmit()).toBe(true);

    fixture.componentRef.setInput('initialProfile', { fullName: 'Maria Souza', email: 'maria@email.com', phoneNumber: '22999999999' });
    fixture.detectChanges();

    expect(component.fullName()).toBe('Maria Souza');
    expect(component.number()).toBe('200');
    expect(component.isAddressDirty()).toBe(true);
    expect(component.canSubmit()).toBe(true);
  });

  it('resets the dirty baseline after acceptChanges', () => {
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initialProfile', { fullName: 'Maria Oliveira', email: 'maria@email.com', phoneNumber: '22999999999' });
    fixture.componentRef.setInput('initialAddress', primaryAddress);
    fixture.detectChanges();

    component.onAddressFieldChange('number', '200');
    fixture.detectChanges();
    expect(component.canSubmit()).toBe(true);

    component.acceptChanges();
    expect(component.canSubmit()).toBe(false);
  });

  it('uses the shared profile rules for every field error so account and checkout cannot diverge', () => {
    component.onFieldInput('fullName', 'M');
    component.onPhoneInput('123');
    component.onFieldInput('email', 'bad');
    component.onAddressFieldChange('cep', '123');
    component.onAddressFieldChange('state', 'r');

    const fields = {
      fullName: component.fullName(),
      phone: component.phone(),
      email: component.email(),
      cep: component.cep(),
      street: component.street(),
      number: component.number(),
      complement: component.complement(),
      neighborhood: component.neighborhood(),
      city: component.city(),
      state: component.state(),
    };

    for (const field of ['fullName', 'phone', 'email', 'cep', 'city', 'state', 'neighborhood', 'street', 'number'] as const) {
      expect(component.fieldError(field)).toBe(customerProfileFieldError(field, fields));
    }
  });
});
