import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { SellerNotificationService } from '../../core/services/seller-notification.service';
import { SignalRService } from '../../core/services/signalr.service';
import { StoreService } from '../../core/services/store.service';
import { NotificationType, SellerNotification } from '../../shared/models/seller-notification.model';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { OrderSoundAlertService } from './order-sound-alert.service';
import { SellerShellFacade } from './seller-shell.facade';

describe('SellerShellFacade', () => {
  let facade: SellerShellFacade;
  let storeServiceMock: { getMyStore: jest.Mock };
  let notificationServiceMock: { list: jest.Mock };
  let signalRServiceMock: { startSellerHub: jest.Mock; stopSellerHub: jest.Mock; onSellerEvent: jest.Mock };
  let soundServiceMock: { enabled: any; needsActivation: any; playNewOrder: jest.Mock; enable: jest.Mock; disable: jest.Mock };
  let printingServiceMock: { autoPrintOrder: jest.Mock; config: jest.Mock };

  beforeEach(() => {
    storeServiceMock = { getMyStore: jest.fn() };
    notificationServiceMock = { list: jest.fn() };
    signalRServiceMock = {
      startSellerHub: jest.fn().mockResolvedValue(undefined),
      stopSellerHub: jest.fn(),
      onSellerEvent: jest.fn(),
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
    };

    TestBed.configureTestingModule({
      providers: [
        SellerShellFacade,
        { provide: StoreService, useValue: storeServiceMock },
        { provide: SellerNotificationService, useValue: notificationServiceMock },
        { provide: SignalRService, useValue: signalRServiceMock },
        { provide: OrderSoundAlertService, useValue: soundServiceMock },
        { provide: SellerPrintingService, useValue: printingServiceMock },
      ],
    });

    facade = TestBed.inject(SellerShellFacade);
  });

  it('should start seller hub and play sound when a NewOrder notification arrives', async () => {
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
        description: 'Loja teste',
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
        description: 'Loja teste',
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
        description: 'Loja teste',
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
      description: 'Loja teste',
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
        description: 'Loja teste',
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
    facade.reset();
    resolveHubStart?.();
    await initPromise;

    expect(signalRServiceMock.stopSellerHub).toHaveBeenCalledTimes(1);
    expect(signalRServiceMock.onSellerEvent).not.toHaveBeenCalled();
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
        description: 'Loja teste',
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
});
