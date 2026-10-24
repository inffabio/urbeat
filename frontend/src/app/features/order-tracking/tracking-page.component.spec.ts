import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { TrackingPageComponent } from './tracking-page.component';
import {
  CustomerOrderTrackingService,
  OrderStatusUpdateEvent,
} from '../../core/services/customer-order-tracking.service';
import { OrderService } from '../../core/services/order.service';
import { AuthService } from '../../core/services/auth.service';
import { CartService } from '../../core/services/cart.service';
import { CartCheckoutActionService } from '../../core/services/cart-checkout-action.service';
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
  let onCustomerStateChangeMock: jest.Mock;
  let routerMock: { navigate: jest.Mock; url: string };
  let logoutMock: jest.Mock;
  let listeners: Record<string, (data: OrderStatusUpdateEvent) => void>;
  let paramMap: BehaviorSubject<ParamMap>;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

    getOrderMock = jest.fn().mockReturnValue(of(baseOrder));
    confirmDeliveryMock = jest.fn();
    startCustomerHubMock = jest.fn().mockResolvedValue(undefined);
    listeners = {};
    onCustomerEventMock = jest.fn((name: string, cb: (data: OrderStatusUpdateEvent) => void) => {
      listeners[name] = cb;
    });
    removeCustomerListenerMock = jest.fn();
    stopCustomerHubMock = jest.fn();
    onCustomerStateChangeMock = jest.fn(() => jest.fn());
    routerMock = { navigate: jest.fn(), url: '/loja/pedido/order1' };
    logoutMock = jest.fn();
    paramMap = new BehaviorSubject<ParamMap>(makeParamMap('order1'));

    await TestBed.configureTestingModule({
      imports: [TrackingPageComponent],
      providers: [
        CustomerOrderTrackingService,
        { provide: OrderService, useValue: { getOrder: getOrderMock, confirmDelivery: confirmDeliveryMock } },
        { provide: AuthService, useValue: { token: signal('customer-token'), token$: of('customer-token'), getToken: () => 'customer-token', logout: logoutMock } },
        {
          provide: SignalRService,
          useValue: {
            startCustomerHub: startCustomerHubMock,
            onCustomerEvent: onCustomerEventMock,
            removeCustomerListener: removeCustomerListenerMock,
            stopCustomerHub: stopCustomerHubMock,
            onCustomerStateChange: onCustomerStateChangeMock,
          },
        },
        { provide: StoreContextService, useValue: { storeId: signal('store1'), phoneNumber: signal('22999999999'), storeName: signal('Loja Teste') } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: jest.fn().mockReturnValue('order1') } }, paramMap: paramMap.asObservable() } },
        { provide: Router, useValue: routerMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('should expose tracking steps as a horizontal list with the current step marked', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const list = fixture.debugElement.query(By.css('.status-bar[role="list"]'));
    const current = fixture.debugElement.query(By.css('.status-step[aria-current="step"]'));

    expect(list).not.toBeNull();
    expect(current.nativeElement.textContent).toContain('Preparando');

    fixture.destroy();
  });

  it('renders the full delivery status sequence including Pronto', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const labels = fixture.debugElement
      .queryAll(By.css('.status-step .status-label'))
      .map((el) => el.nativeElement.textContent.trim());

    expect(labels).toEqual(['Recebido', 'Preparando', 'Pronto', 'Saiu para entregar', 'Entregue']);

    fixture.destroy();
  });

  it('keeps the delivered step inside the timeline as the current step, never as a completed future one', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));
    const steps = fixture.debugElement.queryAll(By.css('.status-step'));
    const last = steps[steps.length - 1];

    expect(scroll.nativeElement.classList).toContain('is-delivered');
    expect(last.query(By.css('.status-label')).nativeElement.textContent.trim()).toBe('Entregue');
    expect(last.nativeElement.getAttribute('aria-current')).toBe('step');
    expect(last.nativeElement.classList).not.toContain('done');
    expect(fixture.debugElement.queryAll(By.css('.status-step.done')).length).toBe(steps.length - 1);

    fixture.destroy();
  });

  it('renders a decorative track that stops at the authoritative current step', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const track = fixture.debugElement.query(By.css('.status-track'));
    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));

    expect(track).not.toBeNull();
    expect(track.nativeElement.getAttribute('aria-hidden')).toBe('true');
    expect(track.query(By.css('.status-track-progress'))).not.toBeNull();
    expect(track.query(By.css('.status-track-sweep'))).not.toBeNull();

    // Preparing is the 2nd of 5 steps: the track runs from 10% to 90% and the
    // current ratio is 0.25, which drives both the static fill and the sweep.
    expect(scroll.nativeElement.style.getPropertyValue('--track-inset')).toBe('10%');
    expect(scroll.nativeElement.style.getPropertyValue('--track-ratio')).toBe('0.25');

    fixture.destroy();
  });

  it('advances the track with the current status without marking any future step as done', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Ready }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));
    const done = fixture.debugElement.queryAll(By.css('.status-step.done'));
    const current = fixture.debugElement.query(By.css('.status-step[aria-current="step"]'));

    expect(scroll.nativeElement.style.getPropertyValue('--track-ratio')).toBe('0.5');
    expect(done.length).toBe(2);
    expect(current.query(By.css('.status-label')).nativeElement.textContent.trim()).toBe('Pronto');
    expect(fixture.debugElement.queryAll(By.css('.status-step')).at(-1)?.nativeElement.classList).not.toContain('done');

    fixture.destroy();
  });

  it.each([
    OrderStatus.Received,
    OrderStatus.Preparing,
    OrderStatus.Ready,
    OrderStatus.OnDelivery,
  ])('keeps the accordion sweep running while the order is not delivered (status %s)', (status) => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));

    expect(scroll.nativeElement.classList).not.toContain('is-delivered');
    expect(scroll.nativeElement.classList).not.toContain('is-cancelled');
    expect(fixture.debugElement.query(By.css('.status-track-sweep'))).not.toBeNull();

    fixture.destroy();
  });

  it('marks a delivered order as terminal and keeps the filled track on the last point', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));
    const current = fixture.debugElement.query(By.css('.status-step[aria-current="step"]'));

    expect(scroll.nativeElement.classList).toContain('is-delivered');
    expect(scroll.nativeElement.classList).not.toContain('is-cancelled');
    // Delivered is the last step, so the static fill covers the whole track.
    expect(scroll.nativeElement.style.getPropertyValue('--track-ratio')).toBe('1');
    expect(current.query(By.css('.status-label')).nativeElement.textContent.trim()).toBe('Entregue');

    fixture.destroy();
  });

  it('does not mark a cancelled order as delivered', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Cancelled }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));

    expect(scroll.nativeElement.classList).toContain('is-cancelled');
    expect(scroll.nativeElement.classList).not.toContain('is-delivered');

    fixture.destroy();
  });

  it('omits the delivery step from the sequence for pickup orders', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, fulfillmentType: FulfillmentType.PickUp }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const labels = fixture.debugElement
      .queryAll(By.css('.status-step .status-label'))
      .map((el) => el.nativeElement.textContent.trim());

    expect(labels).toEqual(['Recebido', 'Preparando', 'Pronto', 'Entregue']);

    fixture.destroy();
  });

  it('ends the four-step pickup track at the last dot when the order is delivered', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      fulfillmentType: FulfillmentType.PickUp,
      status: OrderStatus.Delivered,
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));
    const steps = fixture.debugElement.queryAll(By.css('.status-step'));

    expect(steps.length).toBe(4);
    expect(scroll.nativeElement.style.getPropertyValue('--track-inset')).toBe('12.5%');
    expect(scroll.nativeElement.style.getPropertyValue('--track-ratio')).toBe('1');
    expect(scroll.nativeElement.classList).toContain('is-delivered');
    expect(steps[3].nativeElement.getAttribute('aria-current')).toBe('step');

    fixture.destroy();
  });

  it('marks the current step as active for a Ready order', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Ready }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const current = fixture.debugElement.query(By.css('.status-step[aria-current="step"] .status-label'));
    expect(current.nativeElement.textContent.trim()).toBe('Pronto');

    fixture.destroy();
  });

  it('does not mark any step as completed for a cancelled order', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Cancelled }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.queryAll(By.css('.status-step.done')).length).toBe(0);
    expect(fixture.debugElement.query(By.css('.status-step[aria-current="step"]'))).toBeNull();

    fixture.destroy();
  });

  it('shows a clear cancelled state instead of the completed flow for a cancelled order', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Cancelled }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.cancelled-header'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.success-header'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.status-step.done'))).toBeNull();

    fixture.destroy();
  });

  it('shows the delivery ETA row for delivery orders', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const etaRow = fixture.debugElement.query(By.css('.eta-row'));
    expect(etaRow.nativeElement.textContent).toContain('Previsão de entrega');
    expect(etaRow.nativeElement.textContent).toContain('Hoje, entre');

    fixture.destroy();
  });

  it('replaces the delivery ETA row with pickup copy for pickup orders', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, fulfillmentType: FulfillmentType.PickUp }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const etaRow = fixture.debugElement.query(By.css('.eta-row'));
    expect(etaRow.nativeElement.textContent).toContain('Retirada no local');
    expect(etaRow.nativeElement.textContent).toContain('A loja avisará quando estiver pronto');
    expect(etaRow.nativeElement.textContent).not.toContain('Previsão de entrega');
    expect(etaRow.nativeElement.textContent).not.toContain('Hoje, entre');

    fixture.destroy();
  });

  it('wraps the status list in a labelled region that keeps list semantics without an inert tab stop', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const scroll = fixture.debugElement.query(By.css('.status-bar-scroll'));
    const list = fixture.debugElement.query(By.css('.status-bar-scroll .status-bar[role="list"]'));

    expect(scroll).not.toBeNull();
    expect(scroll.nativeElement.hasAttribute('tabindex')).toBe(false);
    expect(scroll.nativeElement.getAttribute('role')).toBe('region');
    expect(scroll.nativeElement.getAttribute('aria-label')).toBe('Acompanhamento do pedido');
    expect(list).not.toBeNull();
    expect(list.attributes['aria-label']).toBe('Status do pedido');
    expect(fixture.debugElement.queryAll(By.css('.status-bar-scroll .status-step')).length).toBeGreaterThan(0);

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

  it('should track the route order through the shared tracking service and render it', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const tracking = TestBed.inject(CustomerOrderTrackingService);
    expect(tracking.trackedOrders().map((o) => o.id)).toContain('order1');
    expect(getOrderMock).toHaveBeenCalledWith('order1');
    expect(fixture.debugElement.query(By.css('.status-bar[role="list"]'))).not.toBeNull();

    fixture.destroy();
  });

  it('should update the displayed order when OrderStatusUpdated arrives via the shared service', async () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    await tracking.start();

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    expect(listeners['OrderStatusUpdated']).toBeDefined();

    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Ready }));
    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });
    await tick();

    expect(fixture.componentInstance.order()?.status).toBe(OrderStatus.Ready);

    fixture.destroy();
  });

  it('should ignore OrderStatusUpdated for a different order id', async () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    await tracking.start();

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    const callsBefore = getOrderMock.mock.calls.length;

    listeners['OrderStatusUpdated']({
      orderId: 'other-order',
      orderCode: 'XYZ',
      status: OrderStatus.Preparing,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });
    await tick();

    expect(getOrderMock).toHaveBeenCalledTimes(callsBefore);

    fixture.destroy();
  });

  it('should not stop the customer hub when the page is destroyed', async () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    expect(startCustomerHubMock).not.toHaveBeenCalled();

    fixture.destroy();

    expect(stopCustomerHubMock).not.toHaveBeenCalled();
  });

  it('does not start or stop the shared tracking service lifecycle across order switches', async () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const startSpy = jest.spyOn(tracking, 'start');
    const stopSpy = jest.spyOn(tracking, 'stop');

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    paramMap.next(makeParamMap('order2'));
    fixture.detectChanges();
    await tick();

    fixture.destroy();

    expect(startSpy).not.toHaveBeenCalled();
    expect(stopSpy).not.toHaveBeenCalled();
  });

  it('should refresh through the shared tracking service', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const refreshSpy = jest.spyOn(tracking, 'refresh');

    fixture.componentInstance.refresh();

    expect(refreshSpy).toHaveBeenCalledWith('order1');

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

  it('reserves extra scroll clearance while the confirmation FAB is shown so the last link clears it', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const fab = fixture.debugElement.query(By.css('.confirm-delivery-fab'));
    const content = fixture.debugElement.query(By.css('.screen-padding'));

    expect(fab).not.toBeNull();
    expect(content.nativeElement.classList).toContain('has-confirm-fab');

    fixture.destroy();
  });

  it('keeps the base scroll clearance when the confirmation FAB is hidden', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const fab = fixture.debugElement.query(By.css('.confirm-delivery-fab'));
    const content = fixture.debugElement.query(By.css('.screen-padding'));

    expect(fab).toBeNull();
    expect(content.nativeElement.classList).not.toContain('has-confirm-fab');

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

  it('reacts to orderId changes via paramMap and selects the new order without recreating', async () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    expect(fixture.componentInstance.order()?.id).toBe('order1');

    const order2 = { ...baseOrder, id: 'order2', code: 'XYZ999' };
    getOrderMock.mockReturnValue(of(order2));
    paramMap.next(makeParamMap('order2'));
    fixture.detectChanges();
    await tick();

    expect(getOrderMock).toHaveBeenCalledWith('order2');
    expect(fixture.componentInstance.order()?.id).toBe('order2');
    expect(fixture.componentInstance.orderCode()).toBe('Pedido #XYZ999');

    fixture.destroy();
  });

  it('does not refresh a stale order id after switching orders while a confirmation is pending', async () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));
    let settleConfirm: (() => void) | undefined;
    confirmDeliveryMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          settleConfirm = () =>
            subscriber.next({ ...baseOrder, status: OrderStatus.Delivered, deliveryConfirmedAtUtc: 'x' });
        }),
    );

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    fixture.componentInstance.confirmDelivery();
    paramMap.next(makeParamMap('order2'));
    fixture.detectChanges();

    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const refreshSpy = jest.spyOn(tracking, 'refresh');
    refreshSpy.mockClear();

    settleConfirm?.();
    await tick();

    expect(refreshSpy).not.toHaveBeenCalled();

    fixture.destroy();
  });

  it('stops reacting to paramMap after the component is destroyed', async () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();
    await tick();

    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const trackSpy = jest.spyOn(tracking, 'trackOrder');

    fixture.destroy();
    trackSpy.mockClear();

    paramMap.next(makeParamMap('order2'));
    await tick();

    expect(trackSpy).not.toHaveBeenCalled();
  });

  it('renders each item separately with its quantity', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      items: [
        { productName: 'X-burguer', quantity: 2, unitPrice: 20, totalPrice: 40 },
        { productName: 'Refri', quantity: 1, unitPrice: 8, totalPrice: 8 },
      ],
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const items = fixture.debugElement.queryAll(By.css('.order-item'));
    expect(items.length).toBe(2);
    expect(items[0].query(By.css('.item-qty')).nativeElement.textContent).toContain('2');
    expect(items[0].query(By.css('.item-name')).nativeElement.textContent).toContain('X-burguer');
    expect(items[1].query(By.css('.item-qty')).nativeElement.textContent).toContain('1');

    fixture.destroy();
  });

  it('renders selected options with their prices for each item', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      items: [
        {
          productName: 'X-burguer',
          quantity: 1,
          unitPrice: 20,
          totalPrice: 26,
          optionPrices: [
            { name: 'Bacon', price: 4 },
            { name: 'Queijo extra', price: 2 },
          ],
        },
      ],
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const options = fixture.debugElement.queryAll(By.css('.order-item .item-option'));
    expect(options.length).toBe(2);
    expect(options[0].query(By.css('.option-name')).nativeElement.textContent).toContain('Bacon');
    expect(options[0].query(By.css('.option-price')).nativeElement.textContent).toContain('4,00');
    expect(options[1].query(By.css('.option-name')).nativeElement.textContent).toContain('Queijo extra');
    expect(options[1].query(By.css('.option-price')).nativeElement.textContent).toContain('2,00');

    fixture.destroy();
  });

  it('falls back to legacy option names when optionPrices are absent', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      items: [
        {
          productName: 'X-burguer',
          quantity: 1,
          unitPrice: 20,
          totalPrice: 20,
          choiceOptionName: 'Com fritas',
          additionalNames: 'Bacon, Queijo',
        },
      ],
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const options = fixture.debugElement.queryAll(By.css('.order-item .item-option'));
    expect(options.length).toBe(2);
    expect(options[0].nativeElement.textContent).toContain('Com fritas');
    expect(options[1].nativeElement.textContent).toContain('Bacon, Queijo');

    fixture.destroy();
  });

  it('renders subtotal, freight and total for delivery orders', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const subtotalRow = fixture.debugElement.query(By.css('.total-row.subtotal'));
    const freightRow = fixture.debugElement.query(By.css('.total-row.freight'));
    const totalRow = fixture.debugElement.query(By.css('.total-row.grand'));

    expect(subtotalRow).not.toBeNull();
    expect(freightRow).not.toBeNull();
    expect(subtotalRow.nativeElement.textContent).toContain('20,00');
    expect(freightRow.nativeElement.textContent).toContain('6,99');
    expect(totalRow.nativeElement.textContent).toContain('26,99');

    fixture.destroy();
  });

  it('omits the freight row for pickup orders', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      fulfillmentType: FulfillmentType.PickUp,
      deliveryFee: 0,
      total: 20,
    }));

    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const freightRow = fixture.debugElement.query(By.css('.total-row.freight'));
    expect(freightRow).toBeNull();

    fixture.destroy();
  });

  describe('cross-store order validation', () => {
    it('shows an invalid state and does not leak details for an order from another store', async () => {
      const storeContext = TestBed.inject(StoreContextService) as unknown as {
        storeId: ReturnType<typeof signal<string | null>>;
      };
      storeContext.storeId.set('other-store');

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      expect(fixture.componentInstance.order()).toBeNull();
      expect(fixture.componentInstance.invalidOrder()).toBe(true);

      expect(fixture.debugElement.query(By.css('.invalid-order'))).not.toBeNull();
      expect(fixture.debugElement.query(By.css('.order-summary-card'))).toBeNull();
      expect(fixture.debugElement.query(By.css('.status-bar[role="list"]'))).toBeNull();

      fixture.destroy();
    });

    it('returns to the menu from the invalid order state', async () => {
      const storeContext = TestBed.inject(StoreContextService) as unknown as {
        storeId: ReturnType<typeof signal<string | null>>;
      };
      storeContext.storeId.set('other-store');

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      const backButton = fixture.debugElement.query(By.css('.invalid-order button'));
      backButton.nativeElement.click();

      expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);

      fixture.destroy();
    });

    it('does not flag an order from the current store as invalid', async () => {
      const storeContext = TestBed.inject(StoreContextService) as unknown as {
        storeId: ReturnType<typeof signal<string | null>>;
      };
      storeContext.storeId.set('store1');

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      expect(fixture.componentInstance.invalidOrder()).toBe(false);
      expect(fixture.componentInstance.order()?.id).toBe('order1');

      fixture.destroy();
    });
  });

  describe('not-found order handling', () => {
    it('shows an unavailable state instead of a spinner when the order does not exist', async () => {
      getOrderMock.mockImplementation(
        () =>
          new Observable<OrderDetails>((subscriber) => subscriber.error({ status: 404 })),
      );

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      expect(fixture.componentInstance.order()).toBeNull();
      expect(fixture.componentInstance.unavailable()).toBe(true);
      expect(fixture.componentInstance.loading()).toBe(false);

      expect(fixture.debugElement.query(By.css('.invalid-order'))).not.toBeNull();
      expect(fixture.debugElement.query(By.css('.loading-overlay'))).toBeNull();

      fixture.destroy();
    });

    it('returns to the menu from the unavailable order state', async () => {
      getOrderMock.mockImplementation(
        () =>
          new Observable<OrderDetails>((subscriber) => subscriber.error({ status: 404 })),
      );

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      const backButton = fixture.debugElement.query(By.css('.invalid-order button'));
      backButton.nativeElement.click();

      expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);

      fixture.destroy();
    });

    it('does not render stale order details after a loaded order becomes unavailable', async () => {
      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      getOrderMock.mockImplementation(
        () => new Observable<OrderDetails>((subscriber) => subscriber.error({ status: 404 })),
      );
      TestBed.inject(CustomerOrderTrackingService).refresh('order1');
      fixture.detectChanges();

      expect(fixture.debugElement.query(By.css('.invalid-order'))).not.toBeNull();
      expect(fixture.debugElement.query(By.css('.order-summary-card'))).toBeNull();
      expect(fixture.debugElement.query(By.css('.loading-overlay'))).toBeNull();

      fixture.destroy();
    });
  describe('delivered celebration and local reset', () => {
    it('renders the single return-to-menu action and no duplicated lower link', () => {
      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();

      const primaryActions = fixture.debugElement.queryAll(By.css('section.screen-padding > button.primary-btn'));
      expect(primaryActions.length).toBe(1);
      expect(primaryActions[0].nativeElement.textContent).toContain('VOLTAR AO CARDÁPIO');
      expect(fixture.debugElement.query(By.css('app-back-to-menu-link'))).toBeNull();

      primaryActions[0].nativeElement.click();

      expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);

      fixture.destroy();
    });

    it('does not show the celebration dialog while the order is still in progress', () => {
      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();

      expect(fixture.debugElement.query(By.css('.delivered-celebration-overlay'))).toBeNull();
      expect(fixture.componentInstance.deliveredCelebrationOpen()).toBe(false);

      fixture.destroy();
    });

    it('shows an accessible delivered celebration with the explicit return action', () => {
      getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();

      const dialog = fixture.debugElement.query(By.css('[role="dialog"].delivered-celebration-modal'));
      expect(dialog).not.toBeNull();
      expect(dialog.nativeElement.getAttribute('aria-modal')).toBe('true');
      expect(dialog.nativeElement.getAttribute('aria-labelledby')).toBe('delivered-celebration-title');

      const title = dialog.query(By.css('#delivered-celebration-title'));
      expect(title.nativeElement.textContent.trim()).toBe('Bom apetite!');

      const action = dialog.query(By.css('.celebration-action'));
      expect(action.nativeElement.textContent.trim()).toBe('Voltar ao cardápio');
      expect(action.nativeElement.tagName).toBe('BUTTON');

      fixture.destroy();
    });

    it('clears only local storefront state and returns to the menu without logging out', () => {
      getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();

      const tracking = TestBed.inject(CustomerOrderTrackingService);
      const cart = TestBed.inject(CartService);
      const checkoutAction = TestBed.inject(CartCheckoutActionService);
      const trackingReset = jest.spyOn(tracking, 'reset');
      const cartClear = jest.spyOn(cart, 'clearAll');
      const actionReset = jest.spyOn(checkoutAction, 'reset');

      fixture.debugElement
        .query(By.css('.delivered-celebration-modal .celebration-action'))
        .nativeElement.click();
      fixture.detectChanges();

      expect(trackingReset).toHaveBeenCalledTimes(1);
      expect(cartClear).toHaveBeenCalledTimes(1);
      expect(actionReset).toHaveBeenCalledTimes(1);
      expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
      expect(logoutMock).not.toHaveBeenCalled();
      expect(fixture.componentInstance.deliveredCelebrationOpen()).toBe(false);
      expect(tracking.trackedOrders()).toEqual([]);
      // The shared hub lifecycle belongs to the store shell; reset stops it,
      // so the page restarts it without re-tracking the finished order.
      expect(startCustomerHubMock).toHaveBeenCalled();

      fixture.destroy();
    });

    it('opens the celebration once and does not reopen it for realtime refreshes of the same order', async () => {
      const tracking = TestBed.inject(CustomerOrderTrackingService);
      await tracking.start();

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      getOrderMock.mockReturnValue(of({
        ...baseOrder,
        status: OrderStatus.Delivered,
        history: [
          ...baseOrder.history,
          { createdAtUtc: '2026-07-28T12:10:00Z', previousStatus: OrderStatus.OnDelivery, newStatus: OrderStatus.Delivered },
        ],
      }));

      listeners['OrderStatusUpdated']({
        orderId: 'order1',
        orderCode: 'ABC123',
        status: OrderStatus.Delivered,
        changedAtUtc: '2026-07-28T12:10:00Z',
      });
      await tick();
      fixture.detectChanges();

      expect(fixture.componentInstance.deliveredCelebrationOpen()).toBe(true);

      fixture.componentInstance.deliveredCelebrationOpen.set(false);
      fixture.detectChanges();

      listeners['OrderStatusUpdated']({
        orderId: 'order1',
        orderCode: 'ABC123',
        status: OrderStatus.Delivered,
        changedAtUtc: '2026-07-28T12:15:00Z',
      });
      await tick();
      fixture.detectChanges();

      expect(fixture.componentInstance.deliveredCelebrationOpen()).toBe(false);

      fixture.destroy();
    });

    it('celebrates each delivered order once, not only the first one in the session', async () => {
      getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

      const fixture = TestBed.createComponent(TrackingPageComponent);
      fixture.detectChanges();
      await tick();

      expect(fixture.componentInstance.deliveredCelebrationOpen()).toBe(true);

      fixture.componentInstance.deliveredCelebrationOpen.set(false);
      fixture.detectChanges();

      getOrderMock.mockReturnValue(of({ ...baseOrder, id: 'order2', code: 'XYZ999', status: OrderStatus.Delivered }));
      paramMap.next(makeParamMap('order2'));
      fixture.detectChanges();
      await tick();
      fixture.detectChanges();

      expect(fixture.componentInstance.order()?.id).toBe('order2');
      expect(fixture.componentInstance.deliveredCelebrationOpen()).toBe(true);

      fixture.destroy();
    });
  });
});

describe('TrackingPageComponent mobile footer clearance', () => {
  it('keeps the footer clearance inside the ion-content scrollport', () => {
    const styles = readFileSync(resolve(__dirname, 'tracking-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.screen-padding\s*\{[\s\S]*padding:\s*0 18px calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 20px\)/);
  });

  it('adds the confirmation FAB reserve on top of the footer clearance when it is shown', () => {
    const styles = readFileSync(resolve(__dirname, 'tracking-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.screen-padding\.has-confirm-fab\s*\{[\s\S]*padding-bottom:\s*calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 84px\)/);
  });

  it('keeps the confirm-delivery FAB above the fixed footer without breaking the safe area', () => {
    const styles = readFileSync(resolve(__dirname, 'tracking-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.confirm-delivery-fab\s*\{[\s\S]*bottom:\s*var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\);/);
    expect(styles).toMatch(/\.confirm-delivery-fab\s*\{[\s\S]*z-index:\s*40/);
  });
});

describe('TrackingPageComponent responsive timeline', () => {
  const styles = readFileSync(resolve(__dirname, 'tracking-page.component.scss'), 'utf8');

  it('distributes the steps in fluid equal-width columns instead of forcing a fixed-width track', () => {
    expect(styles).toMatch(/\.status-step\s*\{[\s\S]*?flex:\s*1 1 0;/);
    expect(styles).toMatch(/\.status-step\s*\{[\s\S]*?min-width:\s*0;/);
    expect(styles).not.toMatch(/min-width:\s*max-content/);
    expect(styles).not.toMatch(/flex:\s*0 0 76px/);
    expect(styles).not.toMatch(/min-width:\s*76px/);
  });

  it('lets the last label wrap at word boundaries so dot and title cannot be clipped', () => {
    expect(styles).toMatch(/\.status-label\s*\{[\s\S]*?width:\s*100%;/);
    expect(styles).toMatch(/\.status-label\s*\{[\s\S]*?overflow-wrap:\s*normal;/);
    expect(styles).toMatch(/\.status-label\s*\{[\s\S]*?hyphens:\s*manual;/);
    expect(styles).toMatch(/\.status-step:last-child \.status-label\s*\{[^}]*white-space:\s*nowrap;/);
    expect(styles).toMatch(/\.status-bar-scroll\s*\{[\s\S]*?overflow:\s*visible;/);
  });

  it('does not expose the non-scrollable region as an inert keyboard stop', () => {
    expect(styles).not.toMatch(/\.status-bar-scroll:focus-visible/);
    expect(styles).not.toMatch(/\.status-bar-scroll\s*\{[^}]*overflow-x:\s*auto/);
  });

  it('draws the track inset from the first and last dot centers', () => {
    expect(styles).toMatch(/\.status-track::before\s*\{[\s\S]*?inset:\s*0 var\(--track-inset/);
    expect(styles).toMatch(/\.status-track-progress\s*\{[\s\S]*?left:\s*var\(--track-inset/);
    expect(styles).toMatch(/\.status-track-progress\s*\{[\s\S]*?transform:\s*scaleX\(var\(--track-ratio/);
    expect(styles).toMatch(/\.status-track-sweep\s*\{[\s\S]*?right:\s*var\(--track-inset/);
  });

  it('animates the brand sweep to the end of the line and back to the current point', () => {
    expect(styles).toMatch(/\.status-track-sweep\s*\{[^}]*animation:\s*tracking-timeline-sweep/);
    expect(styles).toMatch(/@keyframes tracking-timeline-sweep\s*\{[\s\S]*?scaleX\(1\)/);
    expect(styles).toMatch(/@keyframes tracking-timeline-sweep\s*\{[\s\S]*?scaleX\(var\(--track-ratio/);
  });

  it('stops the accordion sweep only for delivered orders while keeping the static fill', () => {
    expect(styles).toMatch(/\.status-bar-scroll\.is-delivered \.status-track-sweep\s*\{[^}]*animation:\s*none;/);
    expect(styles).toMatch(/\.status-bar-scroll\.is-delivered \.status-track-sweep\s*\{[^}]*opacity:\s*0;/);
    // The delivered state must not hide or shrink the authoritative red fill.
    expect(styles).not.toMatch(/\.status-bar-scroll\.is-delivered \.status-track-progress/);
  });

  it('keeps the sweep stopped for cancelled orders', () => {
    expect(styles).toMatch(/\.status-bar-scroll\.is-cancelled \.status-track-sweep\s*\{[^}]*animation:\s*none;/);
  });

  it('stacks the timeline vertically on narrow phones instead of breaking labels', () => {
    expect(styles).toMatch(/@media \(max-width: 400px\)\s*\{[\s\S]*?\.status-bar\s*\{[^}]*flex-direction:\s*column/);
    expect(styles).toMatch(/@media \(max-width: 400px\)\s*\{[\s\S]*?\.status-bar-scroll\s*\{[^}]*padding-block:\s*0/);
    expect(styles).toMatch(/@media \(max-width: 400px\)\s*\{[\s\S]*?\.status-step\s*\{[^}]*min-height:\s*44px/);
    expect(styles).toMatch(/@media \(max-width: 400px\)\s*\{[\s\S]*?\.status-track-progress\s*\{[^}]*transform:\s*scaleY\(var\(--track-ratio/);
    expect(styles).toMatch(/@keyframes tracking-timeline-sweep-y\s*\{[\s\S]*?scaleY\(var\(--track-ratio/);
  });

  it('keeps the track static at the current point when reduced motion is requested', () => {
    expect(styles).toMatch(/@media \(prefers-reduced-motion: reduce\)\s*\{[\s\S]*?\.status-track-sweep\s*\{[^}]*animation:\s*none !important/);
    expect(styles).toMatch(/@media \(prefers-reduced-motion: reduce\)\s*\{[\s\S]*?\.status-track-progress\s*\{[^}]*transition:\s*none/);
  });
});

function makeParamMap(orderId: string | null): ParamMap {
  return {
    keys: orderId ? ['orderId'] : [],
    get: (key: string) => (key === 'orderId' ? orderId : null),
    has: (key: string) => key === 'orderId' && orderId !== null,
    getAll: () => [],
  } as ParamMap;
}
