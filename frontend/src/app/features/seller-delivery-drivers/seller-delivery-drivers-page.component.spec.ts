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

  it('lists motoboys with phone, WhatsApp action and status', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();

    expect(serviceMock.listStoreDrivers).toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ana Motoboy');
    expect(text).toContain('11999999999');
    expect(text).toContain('Ativo');
    expect(text).not.toContain('Convidar entregador');

    const whatsApp: HTMLAnchorElement | null = fixture.nativeElement.querySelector('.driver-whatsapp');
    expect(whatsApp).not.toBeNull();
    expect(whatsApp!.href).toBe('https://wa.me/5511988887777');
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

  it('validates and creates a motoboy without e-mail', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openCreateDialog();
    component.submitDriver();
    expect(serviceMock.createDriver).not.toHaveBeenCalled();
    expect(component.driverForm.controls.name.touched).toBe(true);

    component.driverForm.setValue({
      name: 'Novo Motoboy',
      phoneNumber: '11988887777',
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

  it('edits a motoboy', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openEditDialog(activeDriver);
    component.driverForm.controls.name.setValue('Ana Atualizada');
    component.submitDriver();

    expect(serviceMock.updateDriver).toHaveBeenCalledWith(
      'link-1',
      expect.objectContaining({ name: 'Ana Atualizada', whatsAppPhoneNumber: '11988887777' }),
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

  it('keeps the list available when saving fails', () => {
    serviceMock.createDriver.mockReturnValue(throwError(() => new Error('fail')));
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openCreateDialog();
    component.driverForm.setValue({
      name: 'Novo Motoboy',
      phoneNumber: '11988887777',
      whatsAppPhoneNumber: null,
      isActive: true,
    });
    component.submitDriver();

    expect(toastMock.showError).toHaveBeenCalled();
    expect(component.submitting()).toBe(false);
  });
});
