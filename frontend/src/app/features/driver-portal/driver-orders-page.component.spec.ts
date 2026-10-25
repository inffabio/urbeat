import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import L from 'leaflet';
import { of, throwError } from 'rxjs';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { ToastService } from '../../core/services/toast.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { DriverOrder } from '../../shared/models/delivery-driver.model';
import { DriverOrdersPageComponent } from './driver-orders-page.component';

const buildOrder = (overrides: Partial<DriverOrder> = {}): DriverOrder => ({
  id: 'order-1',
  code: 'ABC12345',
  storeId: 'store-1',
  status: OrderStatus.Ready,
  fulfillmentType: FulfillmentType.Delivery,
  paymentMethod: PaymentMethod.CashOnDelivery,
  customerName: 'Cliente Teste',
  customerPhoneNumber: '11988887777',
  addressStreet: 'Rua Teste',
  addressNumber: '10',
  addressNeighborhood: 'Centro',
  addressCity: 'Sao Paulo',
  addressState: 'SP',
  addressCep: '01001000',
  addressSummary: 'Rua Teste, 10 - Centro',
  originLatitude: -23.55052,
  originLongitude: -46.633308,
  destinationLatitude: -23.561414,
  destinationLongitude: -46.655881,
  subtotal: 20,
  deliveryFee: 4,
  total: 24,
  createdAtUtc: '2026-10-24T12:00:00Z',
  items: [
    { productName: 'Lanche', quantity: 1, unitPrice: 20, totalPrice: 20 },
  ],
  canStartDelivery: true,
  canConfirmPin: false,
  ...overrides,
});

describe('DriverOrdersPageComponent', () => {
  let deliveryDriverServiceMock: {
    getDriverStores: jest.Mock;
    getDriverOrders: jest.Mock;
    startDelivery: jest.Mock;
    confirmPin: jest.Mock;
  };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock };

  beforeEach(async () => {
    jest.clearAllMocks();
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: { getCurrentPosition: jest.fn() },
    });

    deliveryDriverServiceMock = {
      getDriverStores: jest.fn().mockReturnValue(of([
        { storeId: 'store-1', storeName: 'Loja Um', assignedOrderCount: 1 },
      ])),
      getDriverOrders: jest.fn().mockReturnValue(of({ storeNotAvailable: false, orders: [buildOrder()] })),
      startDelivery: jest.fn().mockReturnValue(of({ status: OrderStatus.OnDelivery })),
      confirmPin: jest.fn().mockReturnValue(of({ status: OrderStatus.Delivered })),
    };
    toastMock = {
      showSuccess: jest.fn().mockResolvedValue(undefined),
      showError: jest.fn().mockResolvedValue(undefined),
    };

    await TestBed.configureTestingModule({
      imports: [DriverOrdersPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DeliveryDriverService, useValue: deliveryDriverServiceMock },
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('loads the active store and renders assigned orders with contact and map actions', () => {
    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();

    expect(deliveryDriverServiceMock.getDriverStores).toHaveBeenCalled();
    expect(deliveryDriverServiceMock.getDriverOrders).toHaveBeenCalledWith('store-1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('#ABC12345');
    expect(text).toContain('Cliente Teste');
    expect(text).toContain('Rua Teste, 10');
    expect(text).toContain('Lanche');
    expect(text).toContain('Iniciar entrega');
    expect(fixture.nativeElement.querySelector('a[href^="tel:"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('a[href^="https://www.google.com/maps"]')).not.toBeNull();

    fixture.destroy();
  });

  it('shows the no-store state when the driver has no active link', () => {
    deliveryDriverServiceMock.getDriverStores.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhuma loja vinculada');
    expect(deliveryDriverServiceMock.getDriverOrders).not.toHaveBeenCalled();
    fixture.destroy();
  });

  it('shows the empty state when the store has no assigned orders', () => {
    deliveryDriverServiceMock.getDriverOrders.mockReturnValue(of({ storeNotAvailable: false, orders: [] }));

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhuma entrega no momento');
    fixture.destroy();
  });

  it('shows the load error state', () => {
    deliveryDriverServiceMock.getDriverStores.mockReturnValue(throwError(() => new Error('network')));

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar suas lojas');
    fixture.destroy();
  });

  it('starts a delivery only after confirmation', () => {
    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.requestStart(buildOrder());
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Iniciar entrega');

    component.confirmStart();
    expect(deliveryDriverServiceMock.startDelivery).toHaveBeenCalledWith('order-1');
    fixture.destroy();
  });

  it('validates the PIN length and surfaces wrong-PIN attempts', () => {
    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openPinDialog(buildOrder({ status: OrderStatus.OnDelivery, canStartDelivery: false, canConfirmPin: true }));
    component.pinValue.set('12');
    component.submitPin();
    expect(component.pinError()).toContain('6 dígitos');
    expect(deliveryDriverServiceMock.confirmPin).not.toHaveBeenCalled();

    deliveryDriverServiceMock.confirmPin.mockReturnValueOnce(
      throwError(() => ({ status: 400, error: { error: 'PIN incorreto. Tentativas restantes: 4.' } })),
    );
    component.pinValue.set('123456');
    component.submitPin();
    expect(component.pinError()).toContain('Tentativas restantes');
    fixture.destroy();
  });

  it('surfaces a concurrent PIN conflict with a specific message and a refresh action', () => {
    jest.useFakeTimers();
    deliveryDriverServiceMock.confirmPin.mockReturnValueOnce(
      throwError(() => ({
        status: 409,
        error: { error: 'Outra tentativa de PIN foi registrada ao mesmo tempo. Atualize o pedido e tente novamente.' },
      })),
    );

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const element = fixture.nativeElement as HTMLElement;

    component.openPinDialog(buildOrder({ status: OrderStatus.OnDelivery, canStartDelivery: false, canConfirmPin: true }));
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    component.pinValue.set('123456');
    component.submitPin();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    expect(component.pinConflict()).toBe(true);
    expect(component.pinError()).toContain('Outra confirmação de PIN');
    expect(component.pinError()).toContain('Atualize o pedido');
    expect(component.pinError()).not.toBe('Não foi possível confirmar o PIN.');
    expect(toastMock.showError).not.toHaveBeenCalled();
    expect(component.pinTarget()).not.toBeNull();

    const refresh = Array.from(element.querySelectorAll<HTMLButtonElement>('#pin-delivery-dialog button'))
      .find((button) => button.textContent?.includes('Atualizar pedido'));
    expect(refresh).toBeDefined();

    deliveryDriverServiceMock.getDriverOrders.mockClear();
    refresh!.click();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    expect(component.pinTarget()).toBeNull();
    expect(deliveryDriverServiceMock.getDriverOrders).toHaveBeenCalledWith('store-1');

    fixture.destroy();
  });

  it('confirms the PIN and reloads the list', () => {
    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openPinDialog(buildOrder({ status: OrderStatus.OnDelivery, canStartDelivery: false, canConfirmPin: true }));
    component.pinValue.set('123456');
    component.submitPin();

    expect(deliveryDriverServiceMock.confirmPin).toHaveBeenCalledWith('order-1', '123456');
    expect(toastMock.showSuccess).toHaveBeenCalled();
    fixture.destroy();
  });

  it('opens the in-card route map for only one delivery at a time', () => {
    deliveryDriverServiceMock.getDriverOrders.mockReturnValue(of({
      storeNotAvailable: false,
      orders: [
        buildOrder(),
        buildOrder({ id: 'order-2', code: 'XYZ99999' }),
      ],
    }));

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('app-driver-route-map')).toBeNull();

    const toggles = element.querySelectorAll<HTMLButtonElement>('.map-toggle');
    expect(toggles).toHaveLength(2);

    toggles[0].click();
    fixture.detectChanges();
    expect(element.querySelectorAll('app-driver-route-map')).toHaveLength(1);
    expect(fixture.componentInstance.activeMapOrderId()).toBe('order-1');
    expect(element.querySelector('.driver-map-canvas')).not.toBeNull();

    toggles[1].click();
    fixture.detectChanges();
    expect(element.querySelectorAll('app-driver-route-map')).toHaveLength(1);
    expect(fixture.componentInstance.activeMapOrderId()).toBe('order-2');

    toggles[1].click();
    fixture.detectChanges();
    expect(element.querySelector('app-driver-route-map')).toBeNull();
    expect(fixture.componentInstance.activeMapOrderId()).toBeNull();

    fixture.destroy();
  });

  it('closes the open map when the active store changes', () => {
    deliveryDriverServiceMock.getDriverStores.mockReturnValue(of([
      { storeId: 'store-1', storeName: 'Loja Um', assignedOrderCount: 1 },
      { storeId: 'store-2', storeName: 'Loja Dois', assignedOrderCount: 0 },
    ]));

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    const element = fixture.nativeElement as HTMLElement;

    component.toggleMap(buildOrder());
    fixture.detectChanges();
    expect(element.querySelector('app-driver-route-map')).not.toBeNull();

    component.onStoreChange('store-2');
    fixture.detectChanges();

    expect(component.activeMapOrderId()).toBeNull();
    expect(element.querySelector('app-driver-route-map')).toBeNull();

    fixture.destroy();
  });

  it('shows the coordinate fallback inside the order map block', () => {
    deliveryDriverServiceMock.getDriverOrders.mockReturnValue(of({
      storeNotAvailable: false,
      orders: [buildOrder({
        originLatitude: null,
        originLongitude: null,
        destinationLatitude: null,
        destinationLongitude: null,
      })],
    }));

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector<HTMLButtonElement>('.map-toggle')!.click();
    fixture.detectChanges();

    expect(element.textContent).toContain('Mapa indisponível para este pedido');
    expect(element.textContent).toContain('Abrir rota no Google Maps');

    fixture.destroy();
  });

  it('removes the Leaflet map when the page is destroyed with the map open', () => {
    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.toggleMap(buildOrder());
    fixture.detectChanges();

    const mapMock = L as unknown as { map: jest.Mock };
    const mapInstance = mapMock.map.mock.results[0].value as { remove: jest.Mock };

    fixture.destroy();

    expect(mapInstance.remove).toHaveBeenCalledTimes(1);
  });

  it('focuses the start dialog, describes it and restores focus on cancel', () => {
    jest.useFakeTimers();
    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = element.querySelector<HTMLButtonElement>('.order-actions .driver-primary-button');
    expect(trigger).not.toBeNull();

    trigger!.focus();
    trigger!.click();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    const dialog = element.querySelector<HTMLElement>('#start-delivery-dialog');
    expect(dialog).not.toBeNull();
    expect(document.activeElement).toBe(dialog);
    expect(dialog!.getAttribute('aria-labelledby')).toBe('start-delivery-title');
    expect(dialog!.getAttribute('aria-describedby')).toBe('start-delivery-description');

    fixture.componentInstance.cancelStart();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    expect(fixture.componentInstance.startTarget()).toBeNull();
    expect(document.activeElement).toBe(trigger);

    fixture.destroy();
  });

  it('traps Tab inside the PIN dialog, wires aria-invalid and closes with Escape', () => {
    jest.useFakeTimers();
    deliveryDriverServiceMock.getDriverOrders.mockReturnValue(of({
      storeNotAvailable: false,
      orders: [buildOrder({
        status: OrderStatus.OnDelivery,
        canStartDelivery: false,
        canConfirmPin: true,
      })],
    }));

    const fixture = TestBed.createComponent(DriverOrdersPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = Array.from(element.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.includes('Confirmar PIN'));
    expect(trigger).toBeDefined();

    trigger!.focus();
    trigger!.click();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    const dialog = element.querySelector<HTMLElement>('#pin-delivery-dialog')!;
    const input = dialog.querySelector<HTMLInputElement>('input')!;
    expect(document.activeElement).toBe(dialog);
    expect(input.getAttribute('aria-describedby')).toBe('pin-delivery-description');
    expect(input.getAttribute('aria-invalid')).toBeNull();

    fixture.componentInstance.pinError.set('PIN incorreto.');
    fixture.detectChanges();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toContain('pin-error-message');

    const buttons = Array.from(dialog.querySelectorAll<HTMLButtonElement>('button'));
    const last = buttons[buttons.length - 1];
    last.focus();
    const tabEvent = {
      key: 'Tab',
      shiftKey: false,
      currentTarget: dialog,
      preventDefault: jest.fn(),
    } as unknown as KeyboardEvent;
    fixture.componentInstance.onDialogKeydown(tabEvent);
    expect(tabEvent.preventDefault).toHaveBeenCalled();
    expect(document.activeElement).toBe(input);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    expect(fixture.componentInstance.pinTarget()).toBeNull();
    expect(document.activeElement).toBe(trigger);

    fixture.destroy();
  });
});
