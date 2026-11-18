import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';

import { OrderService } from '../../core/services/order.service';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { SignalRService } from '../../core/services/signalr.service';
import { SellerOrdersWindowService } from '../../core/services/seller-orders-window.service';
import { ToastService } from '../../core/services/toast.service';
import { AutomaticPrintQueueService } from '../seller-printing/automatic-print-queue.service';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { NotificationType, SellerNotification } from '../../shared/models/seller-notification.model';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { SellerOrdersPageComponent } from './seller-orders-page.component';

const BOARD_PATH = '/api/orders/store';
const DETAILS_PATH_PREFIX = '/api/orders/store/';

const boardPathOf = (url: string): string => new URL(url, 'http://urbeat.local').pathname;
const boardParamsOf = (url: string): URLSearchParams => new URL(url, 'http://urbeat.local').searchParams;

const buildOrder = (overrides: Record<string, unknown> = {}) => ({
  id: 'order-1',
  code: 'URB-1',
  storeId: 'store-1',
  status: OrderStatus.Received,
  fulfillmentType: FulfillmentType.Delivery,
  paymentMethod: PaymentMethod.CashOnDelivery,
  total: 50,
  createdAtUtc: '2026-11-17T18:00:00Z',
  ...overrides,
});

interface BoardResponderResult {
  items: ReturnType<typeof buildOrder>[];
}

describe('SellerOrdersPageComponent realtime board reload', () => {
  let http: HttpTestingController;
  let shell: {
    newOrderPulse: ReturnType<typeof signal<SellerNotification | null>>;
    orderActivityPulse: ReturnType<typeof signal<null>>;
    notifyOrderChanged: jest.Mock;
  };

  const newOrderNotification = (orderId: string): SellerNotification => ({
    id: `notification-${orderId}`,
    orderId,
    type: NotificationType.NewOrder,
    title: 'Novo pedido recebido',
    message: 'Pedido novo',
    isRead: false,
    createdAtUtc: '2026-11-17T18:00:00Z',
  });

  /**
   * Takes the board queries (one per loaded status, including Cancelled for the Concluído
   * column) out of the open queue for a responder to flush.
   */
  const takeBoardRequests = (): ReturnType<HttpTestingController['match']> => {
    const requests = http.match((request) => boardPathOf(request.url) === BOARD_PATH);
    expect(requests.length).toBe(6);
    return requests;
  };

  /** Flushes the given board queries with a responder emulating the real API date/status filter. */
  const respondBoard = (
    requests: ReturnType<HttpTestingController['match']>,
    responder: (status: OrderStatus, params: URLSearchParams) => BoardResponderResult | 'error',
  ): void => {
    for (const request of requests) {
      const params = boardParamsOf(request.request.url);
      const status = Number(params.get('status')) as OrderStatus;
      const result = responder(status, params);

      if (result === 'error') {
        request.flush('board unavailable', { status: 500, statusText: 'Server Error' });
        continue;
      }

      request.flush({
        page: 1,
        pageSize: 50,
        totalItems: result.items.length,
        totalPages: 1,
        items: result.items,
      });
    }
  };

  const flushBoard = (
    responder: (status: OrderStatus, params: URLSearchParams) => BoardResponderResult | 'error',
  ): void => respondBoard(takeBoardRequests(), responder);

  const flushDetailsPrefetch = (): void => {
    const requests = http.match(
      (request) =>
        boardPathOf(request.url).startsWith(DETAILS_PATH_PREFIX) &&
        boardPathOf(request.url) !== BOARD_PATH,
    );
    for (const request of requests) {
      const id = boardPathOf(request.url).slice(DETAILS_PATH_PREFIX.length);
      request.flush({
        ...buildOrder({ id, code: `URB-${id}` }),
        items: [],
        history: [],
      });
    }
  };

  const flushAll = (): void => {
    // The prefetch is best-effort; flush it so no request is left pending between assertions.
    flushDetailsPrefetch();
  };

  beforeEach(() => {
    shell = {
      newOrderPulse: signal<SellerNotification | null>(null),
      orderActivityPulse: signal(null),
      notifyOrderChanged: jest.fn(),
    };

    const detached = signal(false);
    const detachedRoute = signal(false);

    TestBed.configureTestingModule({
      imports: [SellerOrdersPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DeliveryDriverService, useValue: { listStoreDrivers: jest.fn(() => of([])) } },
        {
          provide: SignalRService,
          useValue: { onSellerEvent: jest.fn(), removeSellerListener: jest.fn(), isSellerConnected: signal(false) },
        },
        {
          provide: AutomaticPrintQueueService,
          useValue: {
            enqueueAcceptedOrder: jest.fn().mockResolvedValue(undefined),
            reconcilePendingJobs: jest.fn().mockResolvedValue(undefined),
            statusMessage: signal<string | null>(null),
            statusTone: signal<'info' | 'success' | 'warning' | 'error' | null>(null),
          },
        },
        {
          provide: SellerPrintingService,
          useValue: {
            config: signal({ printerName: 'POS-58 Balcao', connectionType: 'local-agent' }),
            printOrderFromDetails: jest.fn(),
          },
        },
        { provide: ToastService, useValue: { showSuccess: jest.fn(), showError: jest.fn() } },
        { provide: SellerShellFacade, useValue: shell },
        {
          provide: SellerOrdersWindowService,
          useValue: {
            isDetached: detached,
            isDetachedRoute: detachedRoute,
            isDetachedHost: computed(() => detached() && !detachedRoute()),
            secondaryWindowOpen: signal(false),
            openDetachedWindow: jest.fn(() => true),
            closeDetachedWindow: jest.fn(),
            markDetachedRoute: jest.fn(),
            releaseDetachedRoute: jest.fn(),
            dispose: jest.fn(),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}), data: {} } },
        },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('keeps the valid statuses on the board when one status fails during the realtime reload', async () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();
    flushBoard(() => ({ items: [] }));
    flushAll();
    await fixture.whenStable();

    fixture.autoDetectChanges();
    const arriving = buildOrder({ id: 'new-order', code: 'URB-NEW', status: OrderStatus.Received });
    shell.newOrderPulse.set(newOrderNotification('new-order'));
    await fixture.whenStable();

    flushBoard((status) =>
      status === OrderStatus.Preparing
        ? 'error'
        : { items: status === OrderStatus.Received ? [arriving] : [] },
    );
    flushAll();
    await fixture.whenStable();

    expect(
      fixture.nativeElement.querySelector('.order-card[data-order-id="new-order"]'),
    ).not.toBeNull();
  });

  it('includes an order created slightly ahead of the browser clock on the realtime reload', async () => {
    const createdAtUtc = new Date(Date.now() + 5_000).toISOString();
    const arriving = buildOrder({ id: 'new-order', code: 'URB-NEW', status: OrderStatus.Received, createdAtUtc });

    // Mirrors the real API filter: CreatedAtUtc <= endDateUtc, or < endExclusiveUtc when provided.
    const serverBoard = (status: OrderStatus, params: URLSearchParams): BoardResponderResult => {
      if (status !== OrderStatus.Received) return { items: [] };
      const endDateUtc = params.get('endDateUtc');
      const endExclusiveUtc = params.get('endExclusiveUtc');
      const createdAt = new Date(arriving.createdAtUtc).getTime();
      const inRange = endExclusiveUtc
        ? createdAt < new Date(endExclusiveUtc).getTime()
        : endDateUtc
          ? createdAt <= new Date(endDateUtc).getTime()
          : true;
      return { items: inRange ? [arriving] : [] };
    };

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();
    flushBoard(serverBoard);
    flushAll();
    await fixture.whenStable();

    fixture.autoDetectChanges();
    shell.newOrderPulse.set(newOrderNotification('new-order'));
    await fixture.whenStable();

    const reloadRequests = takeBoardRequests();
    const receivedParams = reloadRequests
      .map((request) => boardParamsOf(request.request.url))
      .find((params) => params.get('status') === String(OrderStatus.Received));

    respondBoard(reloadRequests, serverBoard);
    flushAll();
    await fixture.whenStable();

    // The day board must not depend on the browser clock instant to include a brand-new order.
    expect(receivedParams?.get('endExclusiveUtc')).toBeTruthy();
    expect(receivedParams?.get('endDateUtc')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('.order-card[data-order-id="new-order"]'),
    ).not.toBeNull();
  });
});
