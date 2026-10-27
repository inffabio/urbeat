import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { ToastService } from '../../core/services/toast.service';
import {
  SellerDeliveryDriver,
  StoreDeliveryDriverStatus,
} from '../../shared/models/delivery-driver.model';
import { SellerDeliveryDriversPageComponent } from './seller-delivery-drivers-page.component';

const activeDriver: SellerDeliveryDriver = {
  id: 'link-1',
  deliveryDriverId: 'driver-1',
  name: 'Ana Motoboy',
  email: '',
  phoneNumber: '11999999999',
  whatsAppPhoneNumber: '11988887777',
  isActive: true,
  hasAccount: false,
  status: StoreDeliveryDriverStatus.Active,
  canResendInvite: false,
};

describe('SellerDeliveryDriversPageComponent', () => {
  let serviceMock: {
    listStoreDrivers: jest.Mock;
    createDriver: jest.Mock;
    updateDriver: jest.Mock;
    setDriverActive: jest.Mock;
    deleteDriver: jest.Mock;
  };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock };

  beforeEach(async () => {
    serviceMock = {
      listStoreDrivers: jest.fn().mockReturnValue(of([activeDriver])),
      createDriver: jest.fn().mockReturnValue(of(activeDriver)),
      updateDriver: jest.fn().mockReturnValue(of(activeDriver)),
      setDriverActive: jest.fn().mockReturnValue(of({
        ...activeDriver,
        isActive: false,
        status: StoreDeliveryDriverStatus.Revoked,
      })),
      deleteDriver: jest.fn().mockReturnValue(of(void 0)),
    };
    toastMock = {
      showSuccess: jest.fn().mockResolvedValue(undefined),
      showError: jest.fn().mockResolvedValue(undefined),
    };

    await TestBed.configureTestingModule({
      imports: [SellerDeliveryDriversPageComponent],
      providers: [
        { provide: DeliveryDriverService, useValue: serviceMock },
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('lists motoboys with the formatted phone, WhatsApp action and status', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();

    expect(serviceMock.listStoreDrivers).toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ana Motoboy');
    expect(text).toContain('(11) 99999-9999');
    expect(text).toContain('Ativo');
    expect(text).not.toContain('Convidar entregador');

    const whatsApp: HTMLAnchorElement | null = fixture.nativeElement.querySelector('.driver-whatsapp');
    expect(whatsApp).not.toBeNull();
    expect(whatsApp!.href).toBe('https://wa.me/5511988887777');

    const phone: HTMLAnchorElement = fixture.nativeElement.querySelector('.driver-phone');
    expect(phone.href).toBe('tel:11999999999');
  });

  it('shows the empty state and opens the create dialog', () => {
    serviceMock.listStoreDrivers.mockReturnValue(of([]));
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhum motoboy cadastrado');
    fixture.componentInstance.openCreateDialog();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#driver-dialog-title')?.textContent)
      .toContain('Novo motoboy');
    expect(fixture.nativeElement.querySelector('input[formcontrolname="email"]')).toBeNull();
  });

  it('masks both phones while typing and submits normalized digits', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openCreateDialog();
    fixture.detectChanges();

    const phoneInput: HTMLInputElement =
      fixture.nativeElement.querySelector('input[formcontrolname="phoneNumber"]');
    phoneInput.value = '11999998888';
    phoneInput.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();

    expect(component.driverForm.controls.phoneNumber.value).toBe('(11) 99999-8888');

    component.driverForm.controls.name.setValue('Novo Motoboy');
    component.driverForm.controls.whatsAppPhoneNumber.setValue('(11) 98888-7777');
    component.submitDriver();

    expect(serviceMock.createDriver).toHaveBeenCalledWith({
      name: 'Novo Motoboy',
      phoneNumber: '11999998888',
      whatsAppPhoneNumber: '11988887777',
      isActive: true,
    });
    expect(toastMock.showSuccess).toHaveBeenCalled();
  });

  it('rejects a landline number and does not call the API', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openCreateDialog();
    component.driverForm.setValue({
      name: 'Novo Motoboy',
      phoneNumber: '(11) 3333-4444',
      whatsAppPhoneNumber: '',
      isActive: true,
    });
    component.submitDriver();

    expect(component.driverForm.controls.phoneNumber.hasError('mobilePhone')).toBe(true);
    expect(serviceMock.createDriver).not.toHaveBeenCalled();
  });

  it('validates required fields and creates a motoboy without e-mail', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openCreateDialog();
    component.submitDriver();
    expect(serviceMock.createDriver).not.toHaveBeenCalled();
    expect(component.driverForm.controls.name.touched).toBe(true);

    component.driverForm.setValue({
      name: 'Novo Motoboy',
      phoneNumber: '(11) 98888-7777',
      whatsAppPhoneNumber: '',
      isActive: true,
    });
    component.submitDriver();

    expect(serviceMock.createDriver).toHaveBeenCalledWith({
      name: 'Novo Motoboy',
      phoneNumber: '11988887777',
      whatsAppPhoneNumber: null,
      isActive: true,
    });
    expect(toastMock.showSuccess).toHaveBeenCalled();
  });

  it('edits a motoboy with formatted phones and normalized payload', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openEditDialog(activeDriver);
    expect(component.driverForm.controls.phoneNumber.value).toBe('(11) 99999-9999');
    expect(component.driverForm.controls.whatsAppPhoneNumber.value).toBe('(11) 98888-7777');

    component.driverForm.controls.name.setValue('Ana Atualizada');
    component.submitDriver();

    expect(serviceMock.updateDriver).toHaveBeenCalledWith(
      'link-1',
      expect.objectContaining({
        name: 'Ana Atualizada',
        phoneNumber: '11999999999',
        whatsAppPhoneNumber: '11988887777',
      }),
    );
  });

  it('inactivates after confirmation', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.requestStatusChange(activeDriver);
    expect(component.statusTarget()?.id).toBe('link-1');
    component.confirmStatusChange();

    expect(serviceMock.setDriverActive).toHaveBeenCalledWith('link-1', false);
    expect(component.statusTarget()).toBeNull();
  });

  it('deletes a motoboy after confirmation and removes it from the grid', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.requestDelete(activeDriver);
    expect(component.deleteTarget()?.id).toBe('link-1');

    component.confirmDelete();
    fixture.detectChanges();

    expect(serviceMock.deleteDriver).toHaveBeenCalledWith('link-1');
    expect(component.deleteTarget()).toBeNull();
    expect(component.drivers()).toHaveLength(0);
    expect(fixture.nativeElement.textContent).not.toContain('Ana Motoboy');
    expect(toastMock.showSuccess).toHaveBeenCalled();
  });

  it('keeps the motoboy when deletion is blocked by linked orders', () => {
    serviceMock.deleteDriver.mockReturnValue(throwError(() => ({
      error: { error: 'Este motoboy possui pedidos vinculados e não pode ser excluído. Inative-o.' },
    })));
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.requestDelete(activeDriver);
    component.confirmDelete();
    fixture.detectChanges();

    expect(toastMock.showError).toHaveBeenCalledWith(
      expect.stringContaining('Inative-o'),
    );
    expect(component.drivers()).toHaveLength(1);
    expect(component.actionDriverId()).toBeNull();
    expect(component.deleteTarget()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Ana Motoboy');
  });

  it('keeps the list available when saving fails', () => {
    serviceMock.createDriver.mockReturnValue(throwError(() => new Error('fail')));
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openCreateDialog();
    component.driverForm.setValue({
      name: 'Novo Motoboy',
      phoneNumber: '(11) 98888-7777',
      whatsAppPhoneNumber: '',
      isActive: true,
    });
    component.submitDriver();

    expect(toastMock.showError).toHaveBeenCalled();
    expect(component.submitting()).toBe(false);
  });
});
