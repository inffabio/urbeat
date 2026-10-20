import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { OrderService } from '../../core/services/order.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { OrderDetails } from '../../shared/models/order.model';
import {
  AUTOMATIC_PRINT_RETRY_DELAYS_MS,
  AutomaticPrintQueueService,
} from './automatic-print-queue.service';
import { PendingPrintJob, PrintingConfig } from './seller-printing.models';
import { SellerPrintingService } from './seller-printing.service';

jest.mock('../../core/utils/platform.helper', () => ({
  ...jest.requireActual('../../core/utils/platform.helper'),
  isWindowsPlatform: jest.fn(() => false),
}));

import { isWindowsPlatform } from '../../core/utils/platform.helper';

const isWindowsPlatformMock = isWindowsPlatform as jest.Mock;

const BASE_CONFIG: PrintingConfig = {
  presetId: 'generic-pos-58-usb',
  printerName: 'POS-58 Balcao',
  connectionType: 'local-agent',
  paperWidth: '58mm',
  copies: 1,
  autoCut: false,
  autoPrint: true,
  printKitchenCopy: true,
  printCounterCopy: true,
  printCustomerReceipt: false,
  printLogo: false,
  highlightOrderNumber: true,
  footerText: '',
  savedMacAddress: '',
  adapterId: 'local-agent',
  logoUrl: '',
};

describe('AutomaticPrintQueueService', () => {
  let printingMock: {
    config: ReturnType<typeof signal<PrintingConfig>>;
    agentHealth: ReturnType<typeof signal<{ available: boolean; mode: 'local-agent'; printers: string[]; message: string }>>;
    loadStoreConfig: jest.Mock;
    printerProfile: jest.Mock;
    printOrderFromDetails: jest.Mock;
    refreshLocalAgent: jest.Mock;
  };
  let orderServiceMock: { getStoreOrder: jest.Mock };
  let apiServiceMock: { get: jest.Mock; post: jest.Mock };

  const buildDetails = (overrides: Partial<OrderDetails> = {}): OrderDetails => ({
    id: 'order-1',
    code: 'URB-1',
    storeId: 'store-1',
    fulfillmentType: FulfillmentType.Delivery,
    status: OrderStatus.Preparing,
    paymentMethod: PaymentMethod.PixOnline,
    subtotal: 20,
    deliveryFee: 0,
    total: 20,
    createdAtUtc: '2026-10-19T11:59:00Z',
    items: [],
    history: [
      {
        createdAtUtc: '2026-10-19T11:59:00Z',
        previousStatus: OrderStatus.Created,
        newStatus: OrderStatus.Received,
      },
      {
        createdAtUtc: '2026-10-19T12:00:00Z',
        previousStatus: OrderStatus.Received,
        newStatus: OrderStatus.Preparing,
      },
    ],
    ...overrides,
  });

  const pendingJob = (overrides: Partial<PendingPrintJob> = {}): PendingPrintJob => ({
    cursor: 1,
    orderId: 'order-1',
    storeId: 'store-1',
    orderCode: 'URB-1',
    acceptanceSequence: 2,
    acceptedAtUtc: '2026-10-19T12:00:00Z',
    status: 'pending',
    attemptCount: 0,
    ...overrides,
  });

  const flush = async (): Promise<void> => {
    for (let i = 0; i < 8; i += 1) await Promise.resolve();
  };

  beforeEach(() => {
    localStorage.clear();
    jest.useRealTimers();
    isWindowsPlatformMock.mockReturnValue(false);

    printingMock = {
      config: signal<PrintingConfig>({ ...BASE_CONFIG }),
      agentHealth: signal({ available: true, mode: 'local-agent' as const, printers: ['POS-58 Balcao'], message: 'ok' }),
      loadStoreConfig: jest.fn().mockResolvedValue(undefined),
      printerProfile: jest.fn((config: PrintingConfig) => config.presetId || 'pos-58'),
      printOrderFromDetails: jest.fn().mockResolvedValue({
        ok: true,
        mode: 'local-agent',
        message: 'enviado',
        printedAtUtc: '2026-10-19T12:00:01Z',
      }),
      refreshLocalAgent: jest.fn().mockResolvedValue(undefined),
    };
    orderServiceMock = {
      getStoreOrder: jest.fn().mockReturnValue(of(buildDetails())),
    };
    apiServiceMock = {
      get: jest.fn().mockReturnValue(of({ items: [], nextCursor: 0, hasMore: false })),
      post: jest.fn().mockReturnValue(of({})),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: OrderService, useValue: orderServiceMock },
        { provide: ApiService, useValue: apiServiceMock },
        // The queue only reads these members; a plain object keeps the test focused.
        { provide: SellerPrintingService, useValue: printingMock },
      ],
    });
  });

  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
  });

  it('never prints when autoPrint is disabled', async () => {
    printingMock.config.set({ ...BASE_CONFIG, autoPrint: false });
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await queue.reconcilePendingJobs();
    await flush();

    expect(printingMock.printOrderFromDetails).not.toHaveBeenCalled();
    expect(orderServiceMock.getStoreOrder).not.toHaveBeenCalled();
    expect(queue.jobs()).toHaveLength(0);
  });

  it('deduplicates duplicate acceptance events for the same order', async () => {
    const queue = TestBed.inject(AutomaticPrintQueueService);
    const event = { orderId: 'order-1', orderCode: 'URB-1', sequence: 2, acceptedAtUtc: '2026-10-19T12:00:00Z' };

    await queue.enqueueAcceptedOrder(event);
    await queue.enqueueAcceptedOrder(event);
    await flush();

    expect(orderServiceMock.getStoreOrder).toHaveBeenCalledTimes(1);
    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);
    expect(queue.jobs()).toHaveLength(1);
    expect(queue.jobs()[0].status).toBe('printed');
  });

  it('sends the authoritative details and a stable operation key to the printer', async () => {
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await flush();

    expect(printingMock.printOrderFromDetails).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'order-1', code: 'URB-1' }),
      expect.stringContaining('order:order-1:accept:2026-10-19T12:00:00Z:seq:3:printer:POS-58-Balcao:profile:generic-pos-58-usb'),
    );
  });

  it('acknowledges a successful print to the backend-authoritative job state', async () => {
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await flush();

    expect(apiServiceMock.post).toHaveBeenCalledWith(
      '/api/seller/printing/jobs/order-1/result',
      expect.objectContaining({ success: true, operationKey: expect.stringContaining('order:order-1') }),
    );
  });

  it('retries after 2s, 5s and 15s and reports final failure without touching the order', async () => {
    jest.useFakeTimers();
    printingMock.printOrderFromDetails.mockResolvedValue({
      ok: false,
      mode: 'local-agent',
      message: 'Impressora sem papel',
      printedAtUtc: '2026-10-19T12:00:01Z',
    });
    const queue = TestBed.inject(AutomaticPrintQueueService);

    const enqueue = queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await enqueue;
    await jest.advanceTimersByTimeAsync(0);

    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);

    await jest.advanceTimersByTimeAsync(AUTOMATIC_PRINT_RETRY_DELAYS_MS[0] - 1);
    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);

    await jest.advanceTimersByTimeAsync(1);
    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(2);

    await jest.advanceTimersByTimeAsync(AUTOMATIC_PRINT_RETRY_DELAYS_MS[1]);
    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(3);

    await jest.advanceTimersByTimeAsync(AUTOMATIC_PRINT_RETRY_DELAYS_MS[2]);
    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(4);
    await jest.advanceTimersByTimeAsync(0);

    const failure = queue.lastFailure();
    expect(failure).not.toBeNull();
    expect(failure!.status).toBe('failed');
    expect(failure!.message).toContain('Impressora sem papel');
    expect(apiServiceMock.post).toHaveBeenLastCalledWith(
      '/api/seller/printing/jobs/order-1/result',
      expect.objectContaining({ success: false }),
    );
  });

  it('never uses the browser dialog for automatic printing on Windows', async () => {
    isWindowsPlatformMock.mockReturnValue(true);
    printingMock.config.set({ ...BASE_CONFIG, connectionType: 'browser-print', adapterId: 'browser-print' });
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await flush();

    expect(printingMock.printOrderFromDetails).not.toHaveBeenCalled();
    expect(queue.lastFailure()?.message).toContain('agente local');
  });

  it('recovers a pending acceptance on a brand-new instance without any local watermark', async () => {
    apiServiceMock.get.mockReturnValue(of({
      items: [pendingJob()],
      nextCursor: 1,
      hasMore: false,
    }));
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.reconcilePendingJobs();
    await flush();

    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);
    expect(apiServiceMock.get).toHaveBeenCalledWith('/api/seller/printing/jobs?afterCursor=0&limit=50');
  });

  it('does not discard an old acceptance just because its timestamp is in the past', async () => {
    apiServiceMock.get.mockReturnValue(of({
      items: [pendingJob({ acceptedAtUtc: '2025-01-01T00:00:00Z' })],
      nextCursor: 1,
      hasMore: false,
    }));
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.reconcilePendingJobs();
    await flush();

    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);
  });

  it('paginates with the server cursor until the pending scan is complete', async () => {
    apiServiceMock.get
      .mockReturnValueOnce(of({
        items: [pendingJob({ cursor: 1, orderId: 'order-1', orderCode: 'URB-1' })],
        nextCursor: 1,
        hasMore: true,
      }))
      .mockReturnValueOnce(of({
        items: [pendingJob({ cursor: 2, orderId: 'order-2', orderCode: 'URB-2' })],
        nextCursor: 2,
        hasMore: false,
      }));
    orderServiceMock.getStoreOrder.mockImplementation((orderId: string) => of(buildDetails({
      id: orderId,
      code: orderId === 'order-1' ? 'URB-1' : 'URB-2',
    })));
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.reconcilePendingJobs();
    await flush();

    expect(apiServiceMock.get).toHaveBeenNthCalledWith(1, '/api/seller/printing/jobs?afterCursor=0&limit=50');
    expect(apiServiceMock.get).toHaveBeenNthCalledWith(2, '/api/seller/printing/jobs?afterCursor=1&limit=50');
    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(2);
  });

  it('deduplicates a job returned twice during a cursor race', async () => {
    apiServiceMock.get
      .mockReturnValueOnce(of({
        items: [pendingJob({ cursor: 1 })],
        nextCursor: 1,
        hasMore: true,
      }))
      .mockReturnValueOnce(of({
        items: [pendingJob({ cursor: 2 })],
        nextCursor: 2,
        hasMore: false,
      }));
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.reconcilePendingJobs();
    await flush();

    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);
  });

  it('does not duplicate a pending job across two reconciliations in the same session', async () => {
    apiServiceMock.get.mockReturnValue(of({
      items: [pendingJob()],
      nextCursor: 1,
      hasMore: false,
    }));
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.reconcilePendingJobs();
    await flush();
    await queue.reconcilePendingJobs();
    await flush();

    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);
  });

  it('never retries an agent-ambiguous result and reports it as ambiguous to the backend', async () => {
    printingMock.printOrderFromDetails.mockResolvedValue({
      ok: false,
      mode: 'local-agent',
      message: 'Resultado físico desconhecido.',
      printedAtUtc: '2026-10-19T12:00:01Z',
      retryable: false,
      uncertain: true,
    });
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await flush();

    expect(printingMock.printOrderFromDetails).toHaveBeenCalledTimes(1);
    expect(queue.jobs()[0].status).toBe('failed');
    expect(apiServiceMock.post).toHaveBeenLastCalledWith(
      '/api/seller/printing/jobs/order-1/result',
      expect.objectContaining({ success: false, ambiguous: true }),
    );
  });

  it('does not auto-print a backend job already marked ambiguous', async () => {
    apiServiceMock.get.mockReturnValue(of({
      items: [pendingJob({ status: 'ambiguous', lastError: 'Resultado físico desconhecido.' })],
      nextCursor: 1,
      hasMore: false,
    }));
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.reconcilePendingJobs();
    await flush();

    expect(printingMock.printOrderFromDetails).not.toHaveBeenCalled();
    expect(queue.jobs()[0].status).toBe('failed');
  });

  it('skips a job already persisted as completed', async () => {
    const queue = TestBed.inject(AutomaticPrintQueueService);
    const operationKey = 'order:order-1:accept:2026-10-19T12:00:00Z:seq:3:printer:POS-58-Balcao:profile:generic-pos-58-usb';
    localStorage.setItem('urbeat:seller-printing:completed-operation-keys', JSON.stringify([operationKey]));

    await queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await flush();

    expect(printingMock.printOrderFromDetails).not.toHaveBeenCalled();
    expect(queue.jobs()[0].status).toBe('printed');
  });

  it('keeps the local result when the backend acknowledgement fails and leaves recovery to reconciliation', async () => {
    apiServiceMock.post.mockReturnValue(throwError(() => new Error('offline')));
    const queue = TestBed.inject(AutomaticPrintQueueService);

    await queue.enqueueAcceptedOrder({ orderId: 'order-1' });
    await flush();

    expect(queue.jobs()[0].status).toBe('printed');
    expect(queue.jobs()[0].message).toContain('reconciliado');
  });
});
