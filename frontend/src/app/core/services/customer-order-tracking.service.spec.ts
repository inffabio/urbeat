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

function createMemoryStorage(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key: string) => (store.has(key) ? store.get(key)! : null),
    key: (index: number) => Array.from(store.keys())[index] ?? null,
    removeItem: (key: string) => {
      store.delete(key);
    },
    setItem: (key: string, value: string) => {
      store.set(key, String(value));
    },
  } as Storage;
}

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
  let originalSessionStorage: Storage;

  const configureTestBed = (): void => {
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
  };

  const createService = (): CustomerOrderTrackingService => {
    TestBed.resetTestingModule();
    configureTestBed();
    return TestBed.inject(CustomerOrderTrackingService);
  };

  beforeEach(() => {
    originalSessionStorage = window.sessionStorage;
    localStorage.clear();
    sessionStorage.clear();

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

    configureTestBed();
  });

  afterEach(() => {
    jest.useRealTimers();
    Object.defineProperty(window, 'sessionStorage', { configurable: true, value: originalSessionStorage });
    localStorage.clear();
    sessionStorage.clear();
  });

  it('restores tracked order ids and loads their details', async () => {
    sessionStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1', 'order2']));
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
    expect(JSON.parse(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual(['order1']);
  });

  it('exposes whether any tracked order ids exist, restored synchronously from storage', async () => {
    sessionStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1']));
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
    // The event already updated the visible status, so the follow-up detail
    // fetch is automatic and must not surface a transient error toast.
    expect(getOrderMock).toHaveBeenLastCalledWith('order1', { background: true });
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

  it('keeps the initial load in the foreground and requests fallback polling in the background', async () => {
    jest.useFakeTimers();
    startCustomerHubMock.mockRejectedValue(new Error('unavailable'));
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledWith('order1');

    getOrderMock.mockClear();
    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).toHaveBeenCalledWith('order1', { background: true });
  });

  it('requests recovery polling refreshes in the background', async () => {
    jest.useFakeTimers();
    let failing = true;
    getOrderMock.mockImplementation(() =>
      failing
        ? new Observable<OrderDetails>((subscriber) => subscriber.error(new Error('api unavailable')))
        : of(baseOrder),
    );

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledWith('order1');

    failing = false;
    jest.advanceTimersByTime(30_000);

    expect(getOrderMock).toHaveBeenCalledWith('order1', { background: true });
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
    expect(JSON.parse(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);
  });

  it('deduplicates and filters restored order ids', async () => {
    sessionStorage.setItem(
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

  it('starts polling when the customer connection drops and reconciles once on reconnect', async () => {
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
    // Reconnect runs one authoritative read of the active order, in the background.
    expect(getOrderMock).toHaveBeenCalledTimes(3);
    expect(getOrderMock).toHaveBeenLastCalledWith('order1', { background: true });

    jest.advanceTimersByTime(30_000);
    expect(getOrderMock).toHaveBeenCalledTimes(3);
  });

  it('reconciles tracked orders only once per reconnect transition', async () => {
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
    stateChangeCallback?.(true);
    expect(getOrderMock).toHaveBeenCalledTimes(2);

    // A duplicated "connected" notification must not trigger another load.
    stateChangeCallback?.(true);
    expect(getOrderMock).toHaveBeenCalledTimes(2);
  });

  it('does not reconcile orders that can no longer change after a reconnect', async () => {
    getOrderMock.mockImplementation((id: string, options?: { background?: boolean }) => {
      if (!options?.background) return of({ ...baseOrder, id });
      return of({
        ...baseOrder,
        id,
        status: OrderStatus.Delivered,
        deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
      });
    });

    let stateChangeCallback: ((connected: boolean) => void) | undefined;
    onCustomerStateChangeMock.mockImplementation((cb: (connected: boolean) => void) => {
      stateChangeCallback = cb;
      return jest.fn();
    });

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    // The background refresh completes the order while the customer is offline.
    stateChangeCallback?.(false);
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));
    service.refresh('order1', { background: true });
    expect(service.activeOrders()).toHaveLength(0);

    getOrderMock.mockClear();
    stateChangeCallback?.(true);

    expect(getOrderMock).not.toHaveBeenCalled();
  });

  it('does not duplicate an in-flight request while reconciling after a reconnect', async () => {
    const pending: ((order: OrderDetails) => void)[] = [];
    getOrderMock.mockImplementation(() =>
      new Observable<OrderDetails>((subscriber) => {
        pending.push((order) => subscriber.next(order));
      }),
    );

    let stateChangeCallback: ((connected: boolean) => void) | undefined;
    onCustomerStateChangeMock.mockImplementation((cb: (connected: boolean) => void) => {
      stateChangeCallback = cb;
      return jest.fn();
    });

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    pending[0]({ ...baseOrder });
    expect(getOrderMock).toHaveBeenCalledTimes(1);

    stateChangeCallback?.(false);
    stateChangeCallback?.(true);
    expect(getOrderMock).toHaveBeenCalledTimes(2);

    // The reconciliation request is still pending, so a second reconnect must not
    // stack another read for the same order.
    stateChangeCallback?.(false);
    stateChangeCallback?.(true);
    expect(getOrderMock).toHaveBeenCalledTimes(2);

    pending[1]({ ...baseOrder, status: OrderStatus.Ready });
    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('ignores a duplicated OrderStatusUpdated event without another request', async () => {
    getOrderMock.mockReturnValue(of(baseOrder));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    const event: OrderStatusUpdateEvent = {
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    };
    listeners['OrderStatusUpdated'](event);
    listeners['OrderStatusUpdated'](event);

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
  });

  it('does not request or regress status for an out-of-order OrderStatusUpdated event', async () => {
    getOrderMock.mockReturnValue(of(baseOrder));

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

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Preparing,
      changedAtUtc: '2026-07-28T12:05:00Z',
    });

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
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

  it('keeps a pending order unchanged and retries a transient background poll failure', async () => {
    jest.useFakeTimers();
    startCustomerHubMock.mockRejectedValue(new Error('unavailable'));
    getOrderMock
      .mockReturnValueOnce(of({ ...baseOrder, status: OrderStatus.PendingPayment }))
      .mockReturnValueOnce(new Observable<OrderDetails>((subscriber) => {
        subscriber.error({ status: 503, statusText: 'Service Unavailable' });
      }))
      .mockReturnValue(of({ ...baseOrder, status: OrderStatus.Received }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    // Waiting for gateway confirmation: the first successful read keeps the
    // order pending and is not turned into a confirmed state by the UI.
    expect(service.trackedOrders()[0].status).toBe(OrderStatus.PendingPayment);

    jest.advanceTimersByTime(30_000);

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.PendingPayment);
    expect(service.isOrderUnavailable('order1')).toBe(false);

    // The retry succeeds and the server-authoritative status is applied.
    jest.advanceTimersByTime(30_000);

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Received);
    expect(service.isOrderUnavailable('order1')).toBe(false);
    expect(service.activeOrders().length).toBe(1);
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
    sessionStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1']));

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
    expect(JSON.parse(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);
    expect(stopCustomerHubMock).toHaveBeenCalled();
  });

  it('keeps a finished order tracked in this tab when it is opened again from history', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    service.trackOrder('order1');

    expect(service.trackedOrders().map((order) => order.id)).toEqual(['order1']);
    expect(service.finishedOrders().map((order) => order.id)).toEqual(['order1']);
    expect(JSON.parse(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual(['order1']);
  });

  it('restores finished orders after a stop/start cycle in the same tab (session reload)', async () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(service.finishedOrders().map((order) => order.id)).toEqual(['order1']);

    service.stop();
    await service.start();

    expect(service.trackedOrders().map((order) => order.id)).toEqual(['order1']);
    expect(service.finishedOrders().map((order) => order.id)).toEqual(['order1']);
    expect(getOrderMock.mock.calls.filter(([id]) => id === 'order1').length).toBeGreaterThan(1);
    expect(JSON.parse(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual(['order1']);
  });

  it('applies fresh details after a repeated delivered event (delivery confirmation)', async () => {
    const deliveredAt = '2026-07-28T12:10:00Z';
    const history = [
      ...baseOrder.history,
      { createdAtUtc: deliveredAt, previousStatus: OrderStatus.OnDelivery, newStatus: OrderStatus.Delivered },
    ];
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered, history }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(service.trackedOrders()[0].deliveryConfirmedAtUtc).toBeUndefined();

    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      history,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));
    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Delivered,
      changedAtUtc: '2026-07-28T12:30:00Z',
    });

    expect(service.trackedOrders()[0].deliveryConfirmedAtUtc).toBe('2026-07-28T12:30:00Z');
  });

  it('retries a failed detail refresh for a delivered order that is still awaiting confirmation', async () => {
    jest.useFakeTimers();
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');
    expect(service.finishedOrders()).toHaveLength(1);

    getOrderMock.mockReturnValue(new Observable<OrderDetails>((subscriber) => subscriber.error(new Error('api down'))));
    service.refresh('order1', { background: true });
    const callsAfterFailure = getOrderMock.mock.calls.length;

    jest.advanceTimersByTime(30_000);

    expect(getOrderMock.mock.calls.length).toBe(callsAfterFailure + 1);

    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));
    jest.advanceTimersByTime(30_000);

    expect(service.trackedOrders()[0].deliveryConfirmedAtUtc).toBe('2026-07-28T12:30:00Z');

    const settled = getOrderMock.mock.calls.length;
    jest.advanceTimersByTime(30_000);
    expect(getOrderMock.mock.calls.length).toBe(settled);
  });

  it('does not keep retrying a delivered order whose confirmation already arrived', async () => {
    jest.useFakeTimers();
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    await service.start();
    service.trackOrder('order1');

    getOrderMock.mockReturnValue(new Observable<OrderDetails>((subscriber) => subscriber.error(new Error('api down'))));
    service.refresh('order1', { background: true });
    const callsAfterFailure = getOrderMock.mock.calls.length;

    jest.advanceTimersByTime(30_000);

    expect(getOrderMock.mock.calls.length).toBe(callsAfterFailure);
  });

  it('refreshes a reopened delivered order that still awaits confirmation', () => {
    getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    expect(getOrderMock).toHaveBeenCalledTimes(1);

    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledTimes(2);
    expect(getOrderMock).toHaveBeenLastCalledWith('order1', { background: true });
    expect(service.trackedOrders()[0].deliveryConfirmedAtUtc).toBe('2026-07-28T12:30:00Z');
  });

  it('does not refetch a reopened delivered order whose confirmation already arrived', () => {
    getOrderMock.mockReturnValue(of({
      ...baseOrder,
      status: OrderStatus.Delivered,
      deliveryConfirmedAtUtc: '2026-07-28T12:30:00Z',
    }));

    const service = TestBed.inject(CustomerOrderTrackingService);
    service.trackOrder('order1');
    service.trackOrder('order1');

    expect(getOrderMock).toHaveBeenCalledTimes(1);
  });

  it('does not let an in-flight initial load overwrite a status event that arrived first', async () => {
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
    expect(pending).toHaveLength(1);

    listeners['OrderStatusUpdated']({
      orderId: 'order1',
      orderCode: 'ABC123',
      status: OrderStatus.Ready,
      changedAtUtc: '2026-07-28T12:10:00Z',
    });

    pending[0]({ ...baseOrder, status: OrderStatus.Preparing });
    expect(service.trackedOrders()).toEqual([]);

    pending[1]({
      ...baseOrder,
      status: OrderStatus.Ready,
      history: [
        { createdAtUtc: '2026-07-28T12:10:00Z', previousStatus: OrderStatus.Preparing, newStatus: OrderStatus.Ready },
      ],
    });

    expect(service.trackedOrders()[0].status).toBe(OrderStatus.Ready);
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
      sessionStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1', 'order2']));
      getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));
      authTokenSignal.set(null);

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();

      expect(getOrderMock).not.toHaveBeenCalled();
      expect(service.trackedOrders()).toEqual([]);
      expect(service.hasTrackedOrders()).toBe(false);
    });

    it('rehydrates persisted ids when the token appears after login', async () => {
      sessionStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['order1']));
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
      expect(JSON.parse(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);
      expect(stopCustomerHubMock).toHaveBeenCalled();
    });

    it('clears finished orders, storage and the hub on logout', async () => {
      getOrderMock.mockReturnValue(of({ ...baseOrder, status: OrderStatus.Delivered }));

      const service = TestBed.inject(CustomerOrderTrackingService);
      await service.start();
      service.trackOrder('order1');
      expect(service.finishedOrders().length).toBe(1);

      authTokenSignal.set(null);
      tokenSubject.next(null);

      expect(service.trackedOrders()).toEqual([]);
      expect(service.finishedOrders()).toEqual([]);
      expect(JSON.parse(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);
      expect(stopCustomerHubMock).toHaveBeenCalled();
    });
  });

  describe('tab-scoped persistence', () => {
    it('persists tracked order ids in sessionStorage, not shared localStorage', async () => {
      getOrderMock.mockReturnValue(of(baseOrder));

      const service = TestBed.inject(CustomerOrderTrackingService);
      service.trackOrder('order1');
      await service.start();

      expect(sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)).toContain('order1');
      expect(localStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)).toBeNull();
    });

    it('ignores and removes legacy localStorage tracked ids instead of migrating them', async () => {
      localStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(['legacy-order']));
      getOrderMock.mockReturnValue(of(baseOrder));

      const service = createService();
      await service.start();

      expect(getOrderMock).not.toHaveBeenCalledWith('legacy-order');
      expect(service.trackedOrders()).toEqual([]);
      expect(localStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)).toBeNull();
    });

    it('keeps tracked orders isolated between two tabs sharing the same key', async () => {
      const tabAStorage = sessionStorage;
      getOrderMock.mockImplementation((id: string) => of({ ...baseOrder, id }));

      const serviceA = createService();
      serviceA.trackOrder('order-a');
      await serviceA.start();

      const tabBStorage = createMemoryStorage();
      Object.defineProperty(window, 'sessionStorage', { configurable: true, value: tabBStorage });

      const serviceB = createService();
      await serviceB.start();

      expect(serviceB.trackedOrders()).toEqual([]);
      expect(JSON.parse(tabBStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)!)).toEqual([]);

      serviceB.trackOrder('order-b');

      expect(tabAStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)).toContain('order-a');
      expect(tabAStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)).not.toContain('order-b');
      expect(tabBStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)).toContain('order-b');
      expect(tabBStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY)).not.toContain('order-a');
    });
  });
});
