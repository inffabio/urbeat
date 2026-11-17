import { TestBed } from '@angular/core/testing';
import { EMPTY, of, Subject, throwError } from 'rxjs';
import { SellerNotificationService } from '../../core/services/seller-notification.service';
import { SignalRService } from '../../core/services/signalr.service';
import { StoreService } from '../../core/services/store.service';
import { AdminStoreSessionService } from '../../core/services/admin-store-session.service';
import { NotificationType, SellerNotification } from '../../shared/models/seller-notification.model';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { OrderSoundAlertService } from './order-sound-alert.service';
import { SellerShellFacade } from './seller-shell.facade';

describe('SellerShellFacade', () => {
  let facade: SellerShellFacade;
  let storeServiceMock: { getMyStore: jest.Mock };
  let notificationServiceMock: { list: jest.Mock };
  let signalRServiceMock: {
    startSellerHub: jest.Mock;
    stopSellerHub: jest.Mock;
    onSellerEvent: jest.Mock;
    removeSellerListener: jest.Mock;
    onSellerStateChange: jest.Mock;
    isSellerConnected: jest.Mock;
  };
  let soundServiceMock: { enabled: any; needsActivation: any; playNewOrder: jest.Mock; enable: jest.Mock; disable: jest.Mock };
  let printingServiceMock: { autoPrintOrder: jest.Mock; config: jest.Mock };
  let adminSessionMock: { session: jest.Mock; isAdminMode: jest.Mock };

  beforeEach(() => {
    storeServiceMock = { getMyStore: jest.fn() };
    notificationServiceMock = { list: jest.fn() };
    signalRServiceMock = {
      startSellerHub: jest.fn().mockResolvedValue(undefined),
      stopSellerHub: jest.fn(),
      onSellerEvent: jest.fn(),
      removeSellerListener: jest.fn(),
      onSellerStateChange: jest.fn(() => jest.fn()),
      isSellerConnected: jest.fn(() => false),
    };
    soundServiceMock = {
      enabled: jest.fn(() => true),
      needsActivation: jest.fn(() => false),
      playNewOrder: jest.fn().mockResolvedValue(true),
      enable: jest.fn().mockResolvedValue(true),
      disable: jest.fn(),
    };
    printingServiceMock = {
      autoPrintOrder: jest.fn().mockResolvedValue(undefined),
      config: jest.fn(() => ({ autoPrint: true })),
      setLogoUrl: jest.fn(),
    };
    adminSessionMock = { session: jest.fn(() => null), isAdminMode: jest.fn(() => false) };

    TestBed.configureTestingModule({
      providers: [
        SellerShellFacade,
        { provide: StoreService, useValue: storeServiceMock },
        { provide: SellerNotificationService, useValue: notificationServiceMock },
        { provide: SignalRService, useValue: signalRServiceMock },
        { provide: OrderSoundAlertService, useValue: soundServiceMock },
        { provide: SellerPrintingService, useValue: printingServiceMock },
        { provide: AdminStoreSessionService, useValue: adminSessionMock },
      ],
    });

    facade = TestBed.inject(SellerShellFacade);
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('should start seller hub and play sound when a NewOrder notification arrives', async () => {    let sellerCallback: ((notification: SellerNotification) => void) | undefined;
    signalRServiceMock.onSellerEvent.mockImplementation(
      (eventName: string, cb: (notification: SellerNotification) => void) => {
        if (eventName === 'ReceiveSellerNotification') sellerCallback = cb;
      },
    );
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    await facade.init();
    sellerCallback?.({
      id: 'n1',
      orderId: 'o1',
      type: NotificationType.NewOrder,
      title: 'Novo pedido recebido',
      message: 'Pedido #123',
      isRead: false,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });

    expect(signalRServiceMock.startSellerHub).toHaveBeenCalled();
    expect(soundServiceMock.playNewOrder).toHaveBeenCalled();
    expect(printingServiceMock.autoPrintOrder).not.toHaveBeenCalled();
    expect(facade.unreadCount()).toBe(1);
    expect(facade.newOrderPulse()?.orderId).toBe('o1');
  });

  it('should not increment unread count twice for the same unread notification', async () => {
    let sellerCallback: ((notification: SellerNotification) => void) | undefined;
    signalRServiceMock.onSellerEvent.mockImplementation(
      (eventName: string, cb: (notification: SellerNotification) => void) => {
        if (eventName === 'ReceiveSellerNotification') sellerCallback = cb;
      },
    );
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));
    const notification: SellerNotification = {
      id: 'n1',
      orderId: 'o1',
      type: NotificationType.NewOrder,
      title: 'Novo pedido recebido',
      message: 'Pedido #123',
      isRead: false,
      createdAtUtc: '2026-07-29T10:00:00Z',
    };

    await facade.init();
    sellerCallback?.(notification);
    sellerCallback?.(notification);

    expect(facade.unreadCount()).toBe(1);
    expect(facade.notifications()).toHaveLength(1);
  });

  it('should expose an order activity pulse for manual order status changes', () => {
    facade.notifyOrderChanged('order1');

    expect(facade.orderActivityPulse()?.orderId).toBe('order1');
    expect(facade.orderActivityPulse()?.source).toBe('manual-status-change');
  });

  it('should clear seller state and stop hub on reset', async () => {
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 1, items: [] }));

    await facade.init();
    facade.reset();

    expect(signalRServiceMock.stopSellerHub).toHaveBeenCalled();
    expect(facade.store()).toBeNull();
    expect(facade.notifications()).toEqual([]);
    expect(facade.unreadCount()).toBe(0);
    expect(facade.orderActivityPulse()).toBeNull();
  });

  it('removes the registered seller notification listener on reset', async () => {
    let sellerCallback: ((notification: SellerNotification) => void) | undefined;
    signalRServiceMock.onSellerEvent.mockImplementation(
      (eventName: string, cb: (notification: SellerNotification) => void) => {
        if (eventName === 'ReceiveSellerNotification') sellerCallback = cb;
      },
    );
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    await facade.init();
    expect(sellerCallback).toBeDefined();

    facade.reset();

    expect(signalRServiceMock.removeSellerListener).toHaveBeenCalledWith(
      'ReceiveSellerNotification',
      sellerCallback,
    );
  });

  it('does not register a duplicate notification listener after reinit', async () => {
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    await facade.init();
    facade.reset();
    await facade.init();
    facade.reset();

    expect(signalRServiceMock.onSellerEvent).toHaveBeenCalledTimes(2);
    expect(signalRServiceMock.removeSellerListener).toHaveBeenCalledTimes(2);
  });

  it('should ignore pending init results after reset', async () => {
    const storeSubject = new Subject<any>();
    const notificationsSubject = new Subject<any>();
    storeServiceMock.getMyStore.mockReturnValue(storeSubject.asObservable());
    notificationServiceMock.list.mockReturnValue(notificationsSubject.asObservable());

    const initPromise = facade.init();
    facade.reset();
    storeSubject.next({
      id: 'store1',
      ownerUserId: 'owner1',
      name: 'Loja Teste',
      slug: 'loja-teste',
      phoneNumber: '11999999999',
      cuisineType: 'Pizzaria',
      isOpen: true,
      isSubscriptionBlocked: false,
      supportsDelivery: true,
      supportsPickup: false,
      minimumOrderValue: 20,
      deliveryAreas: [],
      averageRating: 0,
      totalReviews: 0,
    });
    storeSubject.complete();
    notificationsSubject.next({ unreadCount: 1, items: [] });
    notificationsSubject.complete();
    await initPromise;

    expect(facade.store()).toBeNull();
    expect(facade.unreadCount()).toBe(0);
    expect(signalRServiceMock.startSellerHub).not.toHaveBeenCalled();
  });

  it('should stop seller hub if reset happens while hub start is pending', async () => {
    let resolveHubStart: (() => void) | undefined;
    signalRServiceMock.startSellerHub.mockReturnValue(
      new Promise<void>((resolve) => {
        resolveHubStart = resolve;
      }),
    );
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    const initPromise = facade.init();
    await Promise.resolve();
    const registeredListener = signalRServiceMock.onSellerEvent.mock.calls
      .find(([name]: [string]) => name === 'ReceiveSellerNotification')?.[1];
    expect(registeredListener).toBeDefined();

    facade.reset();
    resolveHubStart?.();
    await initPromise;

    expect(signalRServiceMock.stopSellerHub).toHaveBeenCalledTimes(1);
    expect(signalRServiceMock.removeSellerListener).toHaveBeenCalledWith(
      'ReceiveSellerNotification',
      registeredListener,
    );
  });

  it('clears loading and registers real-time notifications even if the hub start never settles', async () => {
    signalRServiceMock.startSellerHub.mockReturnValue(new Promise<void>(() => undefined));
    storeServiceMock.getMyStore.mockReturnValue(of({
      id: 'store1',
      ownerUserId: 'owner1',
      name: 'Loja Teste',
      slug: 'loja-teste',
      phoneNumber: '11999999999',
      cuisineType: 'Pizzaria',
      isOpen: true,
      isSubscriptionBlocked: false,
      supportsDelivery: true,
      supportsPickup: false,
      minimumOrderValue: 20,
      deliveryAreas: [],
      averageRating: 0,
      totalReviews: 0,
    }));
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    // If init waited for the hub, this would hang until the Jest test timeout.
    await facade.init();

    expect(facade.loading()).toBe(false);
    expect(signalRServiceMock.startSellerHub).toHaveBeenCalled();
    expect(signalRServiceMock.onSellerEvent).toHaveBeenCalledWith(
      'ReceiveSellerNotification',
      expect.any(Function),
    );
  });

  it('retries the seller hub after a realtime disconnect and releases the state subscription on reset', async () => {
    jest.useFakeTimers();
    let sellerStateCallback: ((connected: boolean) => void) | undefined;
    const unsubscribeSellerState = jest.fn();
    signalRServiceMock.onSellerStateChange.mockImplementation((callback: (connected: boolean) => void) => {
      sellerStateCallback = callback;
      return unsubscribeSellerState;
    });
    storeServiceMock.getMyStore.mockReturnValue(of({
      id: 'store1',
      ownerUserId: 'owner1',
      name: 'Loja Teste',
      slug: 'loja-teste',
      phoneNumber: '11999999999',
      cuisineType: 'Pizzaria',
      isOpen: true,
      isSubscriptionBlocked: false,
      supportsDelivery: true,
      supportsPickup: false,
      minimumOrderValue: 20,
      deliveryAreas: [],
      averageRating: 0,
      totalReviews: 0,
    }));
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    await facade.init();
    expect(signalRServiceMock.startSellerHub).toHaveBeenCalledTimes(1);
    expect(sellerStateCallback).toBeDefined();

    sellerStateCallback!(false);
    jest.advanceTimersByTime(10_000);
    await Promise.resolve();

    expect(signalRServiceMock.startSellerHub).toHaveBeenCalledTimes(2);

    facade.reset();

    expect(unsubscribeSellerState).toHaveBeenCalledTimes(1);
  });

  it('finishes initialization when the store and notification requests complete empty', async () => {
    storeServiceMock.getMyStore.mockReturnValue(EMPTY);
    notificationServiceMock.list.mockReturnValue(EMPTY);

    await facade.init();

    expect(facade.loading()).toBe(false);
    expect(facade.error()).not.toBeNull();
    expect(signalRServiceMock.startSellerHub).toHaveBeenCalled();
    expect(signalRServiceMock.onSellerEvent).toHaveBeenCalledWith(
      'ReceiveSellerNotification',
      expect.any(Function),
    );
  });

  it('ends loading when a bootstrap read never emits', async () => {
    jest.useFakeTimers();
    storeServiceMock.getMyStore.mockReturnValue(new Subject().asObservable());
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    const initPromise = facade.init();
    await Promise.resolve();
    jest.advanceTimersByTime(15_000);
    await initPromise;

    expect(facade.loading()).toBe(false);
    expect(facade.error()).not.toBeNull();
    expect(signalRServiceMock.startSellerHub).toHaveBeenCalled();
  });

  it('starts real-time notifications even when the notification list request fails', async () => {
    storeServiceMock.getMyStore.mockReturnValue(of({
      id: 'store1',
      ownerUserId: 'owner1',
      name: 'Loja Teste',
      slug: 'loja-teste',
      phoneNumber: '11999999999',
      cuisineType: 'Pizzaria',
      isOpen: true,
      isSubscriptionBlocked: false,
      supportsDelivery: true,
      supportsPickup: false,
      minimumOrderValue: 20,
      deliveryAreas: [],
      averageRating: 0,
      totalReviews: 0,
    }));
    notificationServiceMock.list.mockReturnValue(throwError(() => new Error('notifications down')));

    await facade.init();

    expect(signalRServiceMock.startSellerHub).toHaveBeenCalled();
    expect(signalRServiceMock.onSellerEvent).toHaveBeenCalledWith(
      'ReceiveSellerNotification',
      expect.any(Function),
    );
    expect(facade.loading()).toBe(false);
  });

  it('should not stop a newer seller hub when an old init finishes after reinit', async () => {
    let resolveOldHubStart: (() => void) | undefined;
    signalRServiceMock.startSellerHub
      .mockReturnValueOnce(
        new Promise<void>((resolve) => {
          resolveOldHubStart = resolve;
        }),
      )
      .mockResolvedValueOnce(undefined);
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    const oldInitPromise = facade.init();
    await Promise.resolve();
    facade.reset();
    await facade.init();
    resolveOldHubStart?.();
    await oldInitPromise;

    expect(signalRServiceMock.stopSellerHub).toHaveBeenCalledTimes(1);
  });

  it('exposes the store response name beside the logo and falls back to Minha loja without a store', async () => {
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja do Contratante',
        slug: 'loja-do-contratante',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      }),
    );
    notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

    await facade.init();

    expect(facade.storeName()).toBe('Loja do Contratante');

    facade.reset();

    expect(facade.storeName()).toBe('Minha loja');
  });

  it('merges a saved store response so storeName reflects the new name without a reinit', () => {
    facade.mergeStore({
      id: 'store1',
      ownerUserId: 'owner1',
      name: 'Loja Renomeada',
      slug: 'loja-renomeada',
      phoneNumber: '11999999999',
      cuisineType: 'Pizzaria',
      isOpen: true,
      isPublished: false,
      isSubscriptionBlocked: false,
      supportsDelivery: true,
      supportsPickup: false,
      minimumOrderValue: 20,
      deliveryAreas: [],
      averageRating: 0,
      totalReviews: 0,
    });

    expect(facade.store()?.id).toBe('store1');
    expect(facade.storeName()).toBe('Loja Renomeada');
    expect(storeServiceMock.getMyStore).not.toHaveBeenCalled();
  });

  it('loads the context store without starting seller notifications, hub or printing in admin mode', async () => {
    adminSessionMock.session.mockReturnValue({
      token: 'opaque-token',
      contextId: 'context-1',
      storeId: 'store1',
      storeName: 'Loja Teste',
      storeSlug: 'loja-teste',
      adminUserId: 'admin-1',
      expiresAtUtc: '2026-11-16T12:30:00Z',
      returnUrl: '/admin/lojas',
    });
    adminSessionMock.isAdminMode.mockReturnValue(true);
    storeServiceMock.getMyStore.mockReturnValue(
      of({
        id: 'store1',
        ownerUserId: 'owner1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        phoneNumber: '11999999999',
        cuisineType: 'Pizzaria',
        isOpen: true,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: false,
        minimumOrderValue: 20,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
        logoUrl: 'https://cdn.example/logo.png',
      }),
    );

    await facade.init();

    expect(facade.isAdminDelegated()).toBe(true);
    expect(storeServiceMock.getMyStore).toHaveBeenCalled();
    expect(facade.store()?.name).toBe('Loja Teste');
    expect(notificationServiceMock.list).not.toHaveBeenCalled();
    expect(signalRServiceMock.startSellerHub).not.toHaveBeenCalled();
    expect(printingServiceMock.setLogoUrl).not.toHaveBeenCalled();
  });
});
