import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Observable, of, Subject } from 'rxjs';

import {
  CustomerOrderTrackingService,
  OrderStatusUpdateEvent,
  TRACKED_ORDER_IDS_STORAGE_KEY,
} from './customer-order-tracking.service';
import { OrderService } from './order.service';
import { SignalRService } from './signalr.service';
import { AuthService } from './auth.service';
import { OrderDetails } from '../../shared/models/order.model';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';

describe('CustomerOrderTrackingService', () => {
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
    items: [{ productName: 'X-burguer', quantity: 1, unitPrice: 20, totalPrice: 20 }],
    history: [{ createdAtUtc: '2026-07-28T12:05:00Z', previousStatus: OrderStatus.Received, newStatus: OrderStatus.Preparing }],
  };

  let getOrderMock: jest.Mock;
  let startCustomerHubMock: jest.Mock;
  let onCustomerEventMock: jest.Mock;
  let removeCustomerListenerMock: jest.Mock;
  let stopCustomerHubMock: jest.Mock;
  let onCustomerStateChangeMock: jest.Mock;
  let listeners: Record<string, (data: OrderStatusUpdateEvent) => void>;
  let authTokenSignal: ReturnType<typeof signal<string | null>>;
  let tokenSubject: Subject<string | null>;

  beforeEach(() => {
    localStorage.clear();

    getOrderMock = jest.fn();
    startCustomerHubMock = jest.fn().mockResolvedValue(undefined);
    listeners = {};
    onCustomerEventMock = jest.fn((name: string, cb: (data: OrderStatusUpdateEvent) => void) => {
      listeners[name] = cb;
    });
    removeCustomerListenerMock = jest.fn();
    stopCustomerHubMock = jest.fn();
    onCustomerStateChangeMock = jest.fn(() => jest.fn());
    authTokenSignal = signal<string | null>('customer-token');
    tokenSubject = new Subject<string | null>();

    TestBed.configureTestingModule({
      providers: [
        { provide: OrderService, useValue: { getOrder: getOrderMock } },
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
        {
          provide: AuthService,
          useValue: {
            token: authTokenSignal,
            token$: tokenSubject.asObservable(),
            getToken: jest.fn(() => authTokenSignal()),
          },
        },
      ],
    });
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('restores tracked order ids and loads their details', async () => {
    localStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1', 'order2']));
    getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();

    expect(getOrderMock).toHaveBeenCalledWith('order1');
    expect(getOrderMock).toHaveBeenCalledWith('order2');
    expect(service.trackedOrders().map((o) => o.id).sort()).toEqual(['order1', 'order2']);
  });

  it('adds a paid order only once', () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledTimes(1);
    expect(service.trackedOrders().length).toBe(1);
    expect(JSON.parse(localStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual(['order1']);
  });

  it('exposes whether any tracked order ids exist, restored synchronously from storage', async () => {
    localStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1']));
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    expect(service.hasTrackedOrders()).toBe(false);

    await service.start();

    expect(service.hasTrackedOrders()).toBe(true);
  });

  it('updates the matching order when OrderStatusUpdated arrives', async () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Ready }));
    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('ignores events for orders that are not tracked', async () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    listeners['OrderStatusUpdated']({
      orderId: 'other-order',
      orderCode: 'XYZ',
      status: OrderStatus.Preparing,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(getOrderMock).toHaveBeenCalledTimes(1);
  });

  it('removes delivered and cancelled orders from activeOrders but keeps them tracked', () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    getOrderMock.mockReturnValue(of({ ...baseOrder, id: 'order2' }));
    service.trackOrder('order2');

    expect(service.activeOrders().length).toBe(2);

    getOrderMock.mockReturnValue(of({ ...baseOrder, id: 'order1', status: OrderStatus.Delivered }));
    service.refresh('order1');
    getOrderMock.mockReturnValue(of({ ...baseOrder, id: 'order2', status: OrderStatus.Cancelled }));
    service.refresh('order2');

    expect(service.activeOrders().length).toBe(0);
    expect(service.trackedOrders().length).toBe(2);
  });

  it('falls back to refresh polling when the customer hub cannot start', async () => {
    jest.useFakeTimers();
    getOrderMock.mockReturnValue(of(baseOrder));
    startCustomerHubMock.mockRejectedValue(new Error('unavailable'));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).toHaveBeenCalledTimes(2);
  });

  it('stops polling orders that become delivered or cancelled', async () => {
    jest.useFakeTimers();
    startCustomerHubMock.mockRejectedValue(new Error('unavailable'));
    getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    service.trackOrder('order2');

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(service.activeOrders().length).toBe(2);

    getOrderMock.mockImplementation((id: string) =>
      of({ ...baseOrder, id, status: OrderStatus.Delivered }),
    );
    service.refresh('order1');
    service.refresh('order2');

    expect(service.activeOrders().length).toBe(0);

    const callsBeforeFinalized = getOrderMock.mock.calls.length;
    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).toHaveBeenCalledTimes(callsBeforeFinalized);
  });

  it('stops the fallback polling timer when no active orders remain', async () => {
    jest.useFakeTimers();
    startCustomerHubMock.mockRejectedValue(new Error('unavailable'));
    getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    service.trackOrder('order2');

    expect(jest.getTimerCount()).toBe(1);

    getOrderMock.mockImplementation((id: string) =>
      of({ ...baseOrder, id, status: OrderStatus.Delivered }),
    );
    service.refresh('order1');
    service.refresh('order2');

    jest.advanceTimersByTime(30_000);

    expect(jest.getTimerCount()).toBe(0);
    expect(service.activeOrders().length).toBe(0);
  });

  it('untracks an order and removes it from state and storage', () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    expect(service.trackedOrders().length).toBe(1);

    service.untrackOrder('order1');

    expect(service.trackedOrders().length).toBe(0);
    expect(JSON.parse(localStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);
  });

  it('deduplicates and filters restored order ids', async () => {
    localStorage.setItem(
      TRACKED_ORDER_IDS_STORAGE_KEY,
      JSON.stringify(['order1', 'order1', '', 'order2', 42, null, 'order2']),
    );
    getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(getOrderMock).toHaveBeenCalledWith('order1');
    expect(getOrderMock).toHaveBeenCalledWith('order2');
    expect(service.trackedOrders().map((o) => o.id).sort()).toEqual(['order1', 'order2']);
  });

  it('does not start polling when stopped before the hub start settles', async () => {
    jest.useFakeTimers();
    getOrderMock.mockReturnValue(of(baseOrder));

    let rejectStart: ((error: unknown) => void) | undefined;
    startCustomerHubMock.mockImplementation(
      () =>
        new Promise<void>((_resolve, reject) => {
          rejectStart = reject;
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    const startPromise = service.start();
    service.stop();
    rejectStart?.(new Error('unavailable'));
    await startPromise;

    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).not.toHaveBeenCalled();
  });

  it('ignores a stale response for an id that was untracked and tracked again', () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    service.untrackOrder('order1');
    service.trackOrder('order1');

    pending[1]({ ...baseOrder, status: OrderStatus.Ready });
    pending[0]({ ...baseOrder, status: OrderStatus.Preparing });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('discards a stale refresh response when a newer refresh for the same order resolves first', () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    service.refresh('order1');

    pending[1]({ ...baseOrder, status: OrderStatus.Ready });
    pending[0]({ ...baseOrder, status: OrderStatus.Preparing });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('starts polling when the customer connection drops and stops on reconnect', async () => {
    jest.useFakeTimers();
    getOrderMock.mockReturnValue(of(baseOrder));

    let stateChangeCallback: ((connected: boolean) => void) | undefined;
    onCustomerStateChangeMock.mockImplementation((cb: (connected: boolean) => void) => {
      stateChangeCallback = cb;
      return jest.fn();
    });

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(getOrderMock).toHaveBeenCalledTimes(1);

    stateChangeCallback?.(false);
    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(2);

    stateChangeCallback?.(true);
    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(2);
  });

  it('ignores a refresh response that settles after stop and restart', async () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.refresh('order1');
    service.stop();
    await service.start();

    pending[0]({ ...baseOrder, status: OrderStatus.Preparing });

    expect(service.trackedOrders()).toEqual([]);
  });

  it('does not issue a duplicate poll request for an order already in flight', async () => {
    jest.useFakeTimers();
    startCustomerHubMock.mockRejectedValue(new Error('unavailable'));
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).toHaveBeenCalledTimes(1);

    pending[0]({ ...baseOrder, status: OrderStatus.Preparing });
    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).toHaveBeenCalledTimes(2);
  });

  it('still refreshes on a SignalR event while a poll request is in flight', async () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(getOrderMock).toHaveBeenCalledTimes(2);
  });

  it('applies the OrderStatusUpdated status immediately before the refresh response', async () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    pending[0]({ ...baseOrder });

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);

    pending[1]({ ...baseOrder, status: OrderStatus.Ready });
  });

  it('starts recovery polling when a refresh fails while the hub is connected', async () => {
    jest.useFakeTimers();
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          subscriber.error(new Error('api unavailable'));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(2);

    getOrderMock.mockReturnValue(of(baseOrder));
    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(3);

    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(3);
  });

  it('treats a not-found order as a terminable unavailable state instead of recovery polling', async () => {
    jest.useFakeTimers();
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          subscriber.error({ status: 404 });
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledTimes(1);
    expect(service.isOrderUnavailable('order1')).toBe(true);
    expect(service.trackedOrders().length).toBe(0);

    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(1);
  });

  it('removes a previously loaded order when a later refresh returns not found', async () => {
    jest.useFakeTimers();
    startCustomerHubMock.mockRejectedValue(new Error('unavailable'));
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(service.trackedOrders()).toHaveLength(1);

    getOrderMock.mockImplementation(
      () => new Observable<OrderDetails>((subscriber) => subscriber.error({ status: 404 })),
    );
    service.refresh('order1');

    expect(service.trackedOrders()).toEqual([]);
    expect(service.isOrderUnavailable('order1')).toBe(true);

    const callsBeforePolling = getOrderMock.mock.calls.length;
    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(callsBeforePolling);
  });

  it('clears the unavailable state when a later refresh succeeds', async () => {
    let notFound = true;
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          if (notFound) {
            subscriber.error({ status: 404 });
          } else {
            subscriber.next(baseOrder);
          }
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(service.isOrderUnavailable('order1')).toBe(true);

    notFound = false;
    service.refresh('order1');

    expect(service.isOrderUnavailable('order1')).toBe(false);
    expect(service.trackedOrders().map((o) => o.id)).toEqual(['order1']);
  });

  it('does not regress to a stale API response after a newer SignalR status', async () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    pending[0]({ ...baseOrder });

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);

    pending[1]({ ...baseOrder, status: OrderStatus.Preparing });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('keeps recovery polling for a failed initial restore even after the hub connects', async () => {
    jest.useFakeTimers();
    localStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1']));

    let shouldFail = true;
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          if (shouldFail) {
            subscriber.error(new Error('api down'));
          } else {
            subscriber.next(baseOrder);
          }
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();

    expect(getOrderMock).toHaveBeenCalledTimes(1);
    expect(service.trackedOrders().length).toBe(0);

    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(2);

    shouldFail = false;
    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(3);
    expect(service.trackedOrders().length).toBe(1);

    const settled = getOrderMock.mock.calls.length;
    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(settled);
  });

  it('does not regress status when a response without history arrives after a SignalR status', async () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    pending[0]({ ...baseOrder });

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);

    pending[1]({ ...baseOrder, history: [], status: OrderStatus.Preparing });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('does not regress status when a same-version response arrives after a SignalR status', async () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    pending[0]({ ...baseOrder });

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);

    pending[1]({
      ...baseOrder,
      status: OrderStatus.Preparing,
      history: [
        {
          createdAtUtc: '2026-07-28T12:10:00Z',
          previousStatus: OrderStatus.Preparing,
          newStatus: OrderStatus.Ready,
        },
      ],
    });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('does not regress to an older status when a stale same-version event arrives', async () => {
    getOrderMock.mockReturnValue(
      of({
        ...baseOrder,
        status: OrderStatus.Ready,
        history: [
          {
            createdAtUtc: '2026-07-28T12:10:00Z',
            previousStatus: OrderStatus.Preparing,
            newStatus: OrderStatus.Ready,
          },
        ],
      }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Preparing,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('does not overwrite a newer status with a response that has no history', () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(
      () =>
        new Observable<OrderDetails>((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    pending[0]({
      ...baseOrder,
      status: OrderStatus.Ready,
      history: [
        {
          createdAtUtc: '2026-07-28T12:10:00Z',
          previousStatus: OrderStatus.Preparing,
          newStatus: OrderStatus.Ready,
        },
      ],
    });
    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);

    service.refresh('order1');
    pending[1]({ ...baseOrder, status: OrderStatus.Preparing, history: [] });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('exposes finished orders separately from active orders', () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    getOrderMock.mockReturnValue(of({ ...baseOrder, id: 'order2' }));
    service.trackOrder('order2');

    expect(service.activeOrders().length).toBe(2);
    expect(service.finishedOrders().length).toBe(0);

    getOrderMock.mockReturnValue(of({ ...baseOrder, id: 'order1', status: OrderStatus.Delivered }));
    service.refresh('order1');
    getOrderMock.mockReturnValue(of({ ...baseOrder, id: 'order2', status: OrderStatus.Cancelled }));
    service.refresh('order2');

    expect(service.activeOrders().length).toBe(0);
    expect(service.finishedOrders().map((o) => o.id).sort()).toEqual(['order1', 'order2']);
    expect(service.trackedOrders().length).toBe(2);
  });

  it('clears tracked orders, storage and stops the hub on reset', async () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(service.trackedOrders().length).toBe(1);

    service.reset();

    expect(service.trackedOrders()).toEqual([]);
    expect(JSON.parse(localStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);
    expect(stopCustomerHubMock).toHaveBeenCalled();
  });

  describe('teardown', () => {
    it('removes the order status listener and stops the customer hub on stop', async () => {
      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();

      service.stop();

      expect(removeCustomerListenerMock).toHaveBeenCalledWith('OrderStatusUpdated', expect.any(Function));
      expect(stopCustomerHubMock).toHaveBeenCalled();
    });

    it('starts the customer hub once even when start is called repeatedly', async () => {
      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();
      await service.start();

      expect(startCustomerHubMock).toHaveBeenCalledTimes(1);
    });

    it('stops the customer hub unconditionally on a single stop (shell-owned lifecycle)', async () => {
      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();

      service.stop();

      expect(stopCustomerHubMock).toHaveBeenCalledTimes(1);
    });

    it('tears down after start, tracking and switching orders without a shared consumer leak', async () => {
      getOrderMock.mockReturnValue(of(baseOrder));

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();
      service.trackOrder('order1');
      service.trackOrder('order2');
      service.stop();

      expect(stopCustomerHubMock).toHaveBeenCalledTimes(1);
      expect(removeCustomerListenerMock).toHaveBeenCalledWith('OrderStatusUpdated', expect.any(Function));
    });

    it('clears the fallback polling timer on stop', async () => {
      jest.useFakeTimers();
      startCustomerHubMock.mockRejectedValue(new Error('unavailable'));
      getOrderMock.mockReturnValue(of(baseOrder));

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();
      service.trackOrder('order1');

      expect(jest.getTimerCount()).toBe(1);

      service.stop();

      expect(jest.getTimerCount()).toBe(0);
    });
  });

  describe('auth gating', () => {
    it('does not load persisted ids or call the API when there is no token', async () => {
      localStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1', 'order2']));
      getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));
      authTokenSignal.set(null);

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();

      expect(getOrderMock).not.toHaveBeenCalled();
      expect(service.trackedOrders()).toEqual([]);
      expect(service.hasTrackedOrders()).toBe(false);
    });

    it('rehydrates persisted ids when the token appears after login', async () => {
      localStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1']));
      getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));
      authTokenSignal.set(null);

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();
      expect(getOrderMock).not.toHaveBeenCalled();

      authTokenSignal.set('customer-token');
      tokenSubject.next('customer-token');

      expect(getOrderMock).toHaveBeenCalledWith('order1');
      expect(service.trackedOrders().map((o) => o.id)).toEqual(['order1']);
    });

    it('does not fetch an order tracked before login until the token appears', async () => {
      getOrderMock.mockReturnValue(of(baseOrder));
      authTokenSignal.set(null);

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();
      service.trackOrder('order1');

      expect(getOrderMock).not.toHaveBeenCalled();

      authTokenSignal.set('customer-token');
      tokenSubject.next('customer-token');

      expect(getOrderMock).toHaveBeenCalledWith('order1');
    });

    it('clears tracked state and storage when the token is cleared', async () => {
      getOrderMock.mockReturnValue(of(baseOrder));

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();
      service.trackOrder('order1');
      expect(service.trackedOrders().length).toBe(1);

      authTokenSignal.set(null);
      tokenSubject.next(null);

      expect(service.trackedOrders()).toEqual([]);
      expect(JSON.parse(localStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);
      expect(stopCustomerHubMock).toHaveBeenCalled();
    });
  });
});
