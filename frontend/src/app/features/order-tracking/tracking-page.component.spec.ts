import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';

import { TrackingPageComponent, OrderStatusUpdateEvent } from './tracking-page.component';
import { CartService } from '../../core/services/cart.service';
import { OrderService } from '../../core/services/order.service';
import { SignalRService } from '../../core/services/signalr.service';
import { StoreContextService } from '../../core/services/store-context.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { OrderDetails } from '../../shared/models/order.model';

describe('TrackingPageComponent', () => {
  const baseOrder: OrderDetails = {
    id: 'order1',
    code: 'ABC123',
    storeId: 'store1',
    fulfillmentType: FulfillmentType.Delivery,
    status: OrderStatus.Preparing,
    paymentMethod: PaymentMethod.CardOnDelivery,
    subtotal: 20,
    deliveryFee: 6.99,
    total: 26.99,
    createdAtUtc: '2026-07-28T12:00:00Z',
    addressStreet: 'Rua A',
    addressNumber: '123',
    addressNeighborhood: 'Centro',
    addressCity: 'Campos',
    addressState: 'RJ',
    items: [{ productName: 'X-burguer', quantity: 1, unitPrice: 20, totalPrice: 20 }],
    history: [{ createdAtUtc: '2026-07-28T12:05:00Z', previousStatus: OrderStatus.Received, newStatus: OrderStatus.Preparing }],
  };

  const tick = () => new Promise<void>((resolve) => setTimeout(resolve));

  let getOrderMock: jest.Mock;
  let confirmDeliveryMock: jest.Mock;
  let startCustomerHubMock: jest.Mock;
  let onCustomerEventMock: jest.Mock;
  let removeCustomerListenerMock: jest.Mock;
  let stopCustomerHubMock: jest.Mock;
  let routerMock: { navigate: jest.Mock; url: string };
  let listeners: Record<string, (data: OrderStatusUpdateEvent) => void>;

  beforeEach(async () => {
    localStorage.clear();

    getOrderMock = jest.fn().mockReturnValue(of(baseOrder));
    confirmDeliveryMock = jest.fn();
    startCustomerHubMock = jest.fn().mockResolvedValue(undefined);
    listeners = {};
    onCustomerEventMock = jest.fn((name: string, cb: (data: OrderStatusUpdateEvent) => void) => {
      listeners[name] = cb;
    });
    removeCustomerListenerMock = jest.fn();
    stopCustomerHubMock = jest.fn();
    routerMock = { navigate: jest.fn(), url: '/loja/pedido/order1' };

    await TestBed.configureTestingModule({
      imports: [TrackingPageComponent],
      providers: [
        CartService,
        { provide: OrderService, useValue: { getOrder: getOrderMock, confirmDelivery: confirmDeliveryMock } },
        {
          provide: SignalRService,
          useValue: {
            startCustomerHub: startCustomerHubMock,
            onCustomerEvent: onCustomerEventMock,
            removeCustomerListener: removeCustomerListenerMock,
            stopCustomerHub: stopCustomerHubMock,
          },
        },
        { provide: StoreContextService, useValue: { phoneNumber: signal('22999999999'), storeName: signal('Loja Teste') } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: jest.fn().mockReturnValue('order1') } } } },
        { provide: Router, useValue: routerMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('should expose tracking steps as a list with the current step marked', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const list = fixture.debugElement.query(By.css('.track-line[role="list"]'));
    const current = fixture.debugElement.query(By.css('.track-step[aria-current="step"]'));

    expect(list).not.toBeNull();
    expect(current.nativeElement.textContent).toContain('Preparando seu pedido');

    fixture.destroy();
  });

  it('should render help action as a button instead of a link without href', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const helpButton = fixture.debugElement.query(By.css('button.help-card'));
    const helpAnchor = fixture.debugElement.query(By.css('a.help-card'));

    expect(helpButton).not.toBeNull();
    expect(helpButton.nativeElement.type).toBe('button');
    expect(helpAnchor).toBeNull();

    fixture.destroy();
  });

  it('should reload the matching order when OrderStatusUpdated arrives', async () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    expect(listeners['OrderStatusUpdated']).toBeDefined();

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Preparing,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(getOrderMock).toHaveBeenLastCalledWith('order1');

    fixture.destroy();
  });

  it('should ignore OrderStatusUpdated for a different order id', async () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    listeners['OrderStatusUpdated']({
      orderId: 'other-order',
      orderCode: 'XYZ',
      status: OrderStatus.Preparing,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(getOrderMock).toHaveBeenCalledTimes(1);

    fixture.destroy();
  });

  it('should poll the order when SignalR is unavailable', () => {
    jest.useFakeTimers();
    startCustomerHubMock.mockRejectedValueOnce(new Error('unavailable'));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    expect(getOrderMock).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(getOrderMock).toHaveBeenLastCalledWith('order1');

    fixture.destroy();
  });

  it('should show the delivery confirmation action for a delivered delivery order without confirmation', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const fab = fixture.debugElement.query(By.css('.confirm-delivery-fab'));
    expect(fab).not.toBeNull();

    fixture.destroy();
  });

  it('should hide the confirmation action when delivery was already confirmed', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const fab = fixture.debugElement.query(By.css('.confirm-delivery-fab'));
    expect(fab).toBeNull();

    fixture.destroy();
  });

  it('should hide the confirmation action for pickup orders', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      fulfillmentType: FulfillmentType.PickUp,
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const fab = fixture.debugElement.query(By.css('.confirm-delivery-fab'));
    expect(fab).toBeNull();

    fixture.destroy();
  });

  it('should keep the customer on the tracking route after successful confirmation', async () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));
    confirmDeliveryMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    fixture.componentInstance.confirmDelivery();
    await tick();

    expect(confirmDeliveryMock).toHaveBeenCalledWith('order1');
    expect(routerMock.navigate).not.toHaveBeenCalled();

    fixture.destroy();
  });
});
