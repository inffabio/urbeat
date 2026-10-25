import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { SignalRService } from '../../core/services/signalr.service';
import { SellerOrdersWindowService } from '../../core/services/seller-orders-window.service';
import { AutomaticPrintQueueService } from '../seller-printing/automatic-print-queue.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { SellerOrdersPageComponent } from './seller-orders-page.component';

jest.mock('../../core/utils/platform.helper', () => ({
  ...jest.requireActual('../../core/utils/platform.helper'),
  isWindowsPlatform: jest.fn(),
}));

import { isWindowsPlatform } from '../../core/utils/platform.helper';

const isWindowsPlatformMock = isWindowsPlatform as jest.Mock;

describe('SellerOrdersPageComponent', () => {
  let orderServiceMock: { getStoreOrders: jest.Mock; getStoreOrder: jest.Mock; updateStoreOrderStatus: jest.Mock; completeOrder: jest.Mock };
  let deliveryDriverServiceMock: {
    listStoreDrivers: jest.Mock;
    assignOrder: jest.Mock;
    removeAssignment: jest.Mock;
  };
  let automaticPrintMock: {
    enqueueAcceptedOrder: jest.Mock;
    reconcilePendingJobs: jest.Mock;
    statusMessage: ReturnType<typeof signal<string | null>>;
    statusTone: ReturnType<typeof signal<'info' | 'success' | 'warning' | 'error' | null>>;
  };
  let toastServiceMock: { showSuccess: jest.Mock; showError: jest.Mock };
  let shellMock: { newOrderPulse: any; notifyOrderChanged: jest.Mock };
  let signalRServiceMock: {
    onSellerEvent: jest.Mock;
    removeSellerListener: jest.Mock;
    isSellerConnected: ReturnType<typeof signal<boolean>>;
  };
  let ordersWindowMock: {
    isDetached: ReturnType<typeof signal<boolean>>;
    isDetachedRoute: ReturnType<typeof signal<boolean>>;
    isDetachedHost: ReturnType<typeof computed<boolean>>;
    secondaryWindowOpen: ReturnType<typeof signal<boolean>>;
    openDetachedWindow: jest.Mock;
    closeDetachedWindow: jest.Mock;
    markDetachedRoute: jest.Mock;
    releaseDetachedRoute: jest.Mock;
    dispose: jest.Mock;
  };
  let activatedRouteMock: {
    snapshot: {
      queryParamMap: ReturnType<typeof convertToParamMap>;
      data: Record<string, unknown>;
    };
  };

  const buildOrder = (overrides: Partial<any> = {}) => ({
    id: 'order-1',
    code: 'ABC',
    storeId: 'store-1',
    status: OrderStatus.Received,
    total: 50,
    createdAtUtc: '2026-08-04T10:00:00Z',
    ...overrides,
  });

  const findOrderCard = (
    fixture: ComponentFixture<SellerOrdersPageComponent>,
    orderId: string,
  ): HTMLElement => {
    const card = fixture.nativeElement.querySelector(
      `.order-card[data-order-id="${orderId}"]`,
    ) as HTMLElement | null;
    expect(card).not.toBeNull();
    return card!;
  };

  const findOrderToggle = (card: HTMLElement): HTMLButtonElement => {
    const toggle = card.querySelector<HTMLButtonElement>('.order-card-toggle');
    expect(toggle).not.toBeNull();
    return toggle!;
  };

  const expandOrderCard = (
    fixture: ComponentFixture<SellerOrdersPageComponent>,
    orderId: string,
  ): HTMLElement => {
    const card = findOrderCard(fixture, orderId);
    findOrderToggle(card).click();
    fixture.detectChanges();
    return card;
  };

  const orderBody = (card: HTMLElement): HTMLElement => {
    const body = card.querySelector<HTMLElement>('.order-card-body');
    expect(body).not.toBeNull();
    return body!;
  };

  beforeEach(async () => {
    isWindowsPlatformMock.mockReturnValue(false);
    orderServiceMock = {
      getStoreOrders: jest.fn().mockReturnValue(of({ items: [], totalItems: 0 })),
      getStoreOrder: jest.fn().mockReturnValue(of({ id: '', code: '', items: [], total: 0, createdAtUtc: '' })),
      updateStoreOrderStatus: jest.fn().mockReturnValue(of({})),
      completeOrder: jest.fn().mockReturnValue(of({})),
    };
    automaticPrintMock = {
      enqueueAcceptedOrder: jest.fn().mockResolvedValue(undefined),
      reconcilePendingJobs: jest.fn().mockResolvedValue(undefined),
      statusMessage: signal<string | null>(null),
      statusTone: signal<'info' | 'success' | 'warning' | 'error' | null>(null),
    };
    toastServiceMock = { showSuccess: jest.fn().mockResolvedValue(undefined), showError: jest.fn().mockResolvedValue(undefined) };
    deliveryDriverServiceMock = {
      listStoreDrivers: jest.fn().mockReturnValue(of([])),
      assignOrder: jest.fn().mockReturnValue(of({ id: 'order-1', code: 'ABC' })),
      removeAssignment: jest.fn().mockReturnValue(of({ id: 'order-1', code: 'ABC' })),
    };
    shellMock = { newOrderPulse: jest.fn(() => null), notifyOrderChanged: jest.fn() };
    signalRServiceMock = { onSellerEvent: jest.fn(), removeSellerListener: jest.fn(), isSellerConnected: signal(false) };
    const detached = signal(false);
    const detachedRoute = signal(false);
    ordersWindowMock = {
      isDetached: detached,
      isDetachedRoute: detachedRoute,
      isDetachedHost: computed(() => detached() && !detachedRoute()),
      secondaryWindowOpen: signal(false),
      openDetachedWindow: jest.fn(() => true),
      closeDetachedWindow: jest.fn(() => detached.set(false)),
      markDetachedRoute: jest.fn(() => detachedRoute.set(true)),
      releaseDetachedRoute: jest.fn(() => {
        detached.set(false);
        detachedRoute.set(false);
      }),
      dispose: jest.fn(),
    };
    activatedRouteMock = { snapshot: { queryParamMap: convertToParamMap({}), data: {} } };

    await TestBed.configureTestingModule({
      imports: [SellerOrdersPageComponent],
      providers: [
        { provide: OrderService, useValue: orderServiceMock },
        { provide: DeliveryDriverService, useValue: deliveryDriverServiceMock },
        { provide: SignalRService, useValue: signalRServiceMock },
        { provide: AutomaticPrintQueueService, useValue: automaticPrintMock },
        { provide: ToastService, useValue: toastServiceMock },
        { provide: SellerShellFacade, useValue: shellMock },
        { provide: SellerOrdersWindowService, useValue: ordersWindowMock },
        { provide: ActivatedRoute, useValue: activatedRouteMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
    isWindowsPlatformMock.mockReturnValue(false);
  });

  it('shows empty new orders panel when no orders exist', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Novos pedidos');
    expect(fixture.nativeElement.textContent).toContain('Nenhum pedido novo');
  });

  it('keeps Received orders in new orders and accepted orders in their status column', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: 'received', code: 'RECEIVED', status: OrderStatus.Received })], totalItems: 1 });
      }
      if (status === OrderStatus.Preparing) {
        return of({
          items: [
            buildOrder({ id: 'accepted', code: 'ACCEPTED', status: OrderStatus.Preparing, customerName: 'Ana' }),
            buildOrder({
              id: 'completed',
              code: 'COMPLETED',
              status: OrderStatus.Preparing,
              sellerCompletedAtUtc: '2026-08-04T11:00:00Z',
            }),
          ],
          totalItems: 2,
        });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.newOrders().map((order) => order.id)).toEqual(['received']);
    expect(fixture.componentInstance.statusGroups().preparing.map((order) => order.id)).toEqual(['accepted']);

    expect(fixture.nativeElement.querySelector('.new-orders-list .order-card[data-order-id="received"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.new-orders-list .order-card[data-order-id="accepted"]')).toBeNull();

    const preparingColumn = Array.from(
      fixture.nativeElement.querySelectorAll<HTMLElement>('.status-col'),
    ).find((col) => col.querySelector('.col-title')?.textContent?.trim() === 'Preparando');
    expect(preparingColumn).not.toBeUndefined();
    expect(preparingColumn!.querySelector('.order-card[data-order-id="accepted"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.order-card[data-order-id="completed"]')).toBeNull();
  });

  it('preserves persisted order codes, including legacy URB prefixes', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: 'legacy-received', code: 'URB-ABC12345' })], totalItems: 1 });
      }
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'new-code', code: 'A7K92PQL', status: OrderStatus.Preparing })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.displayOrderCode({ code: 'A7K92PQL' })).toBe('A7K92PQL');
    expect(fixture.componentInstance.displayOrderCode({ code: 'URB-ABC12345' })).toBe('URB-ABC12345');

    expect(fixture.nativeElement.querySelector('.new-orders-list .order-code').textContent).toContain('#URB-ABC12345');
    expect(
      fixture.nativeElement.querySelector('.status-col .order-card[data-order-id="new-code"] .order-code').textContent,
    ).toContain('#A7K92PQL');
  });

  it('renders accepted orders in the Preparando, Pronto, Em entrega and Concluído columns', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      const items: any[] = [];
      if (status === OrderStatus.Preparing) {
        items.push(buildOrder({ id: 'prep', code: 'PREP', status: OrderStatus.Preparing, customerName: 'Ana Souza', total: 42 }));
      }
      if (status === OrderStatus.Ready) {
        items.push(buildOrder({ id: 'ready', code: 'READY', status: OrderStatus.Ready }));
      }
      if (status === OrderStatus.OnDelivery) {
        items.push(buildOrder({ id: 'deliv', code: 'DELIV', status: OrderStatus.OnDelivery }));
      }
      if (status === OrderStatus.Delivered) {
        items.push(buildOrder({
          id: 'done',
          code: 'DONE',
          status: OrderStatus.Delivered,
          deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z',
        }));
      }
      return of({ items, totalItems: items.length });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.status-flow')).not.toBeNull();
    const columns = Array.from(
      fixture.nativeElement.querySelectorAll<HTMLElement>('.status-grid .status-col'),
    );

    expect(columns.map((col) => col.querySelector('.col-title')?.textContent?.trim()))
      .toEqual(['Preparando', 'Pronto', 'Em entrega', 'Concluído']);
    expect(columns.map((col) => col.querySelectorAll('.status-stack .order-card').length))
      .toEqual([1, 1, 1, 1]);
    expect(columns[0].querySelector('[data-order-id="prep"]')).not.toBeNull();
    expect(columns[1].querySelector('[data-order-id="ready"]')).not.toBeNull();
    expect(columns[2].querySelector('[data-order-id="deliv"]')).not.toBeNull();
    expect(columns[3].querySelector('[data-order-id="done"]')).not.toBeNull();

    const card = findOrderCard(fixture, 'prep');
    expect(card.querySelector('.order-code')?.textContent).toContain('#PREP');
    expect(card.textContent).toContain('Ana Souza');

    expandOrderCard(fixture, 'prep');
    expect(card.textContent).toContain('Marcar como pronto');
    expect(card.textContent!.replace(/\u00a0/g, ' ')).toContain('R$ 42,00');
    expect(fixture.nativeElement.querySelector('.order-details-dialog')).toBeNull();

    expandOrderCard(fixture, 'done');
    expect(fixture.nativeElement.querySelector('.order-card[data-order-id="done"] .board-action.action-green')).not.toBeNull();
  });

  it('tags each status column with its board tone and keeps a readable title', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.boardColumns().map((column) => column.tone))
      .toEqual(['preparing', 'ready', 'on-delivery', 'delivered']);

    const columns = Array.from(
      fixture.nativeElement.querySelectorAll<HTMLElement>('.status-grid .status-col'),
    );

    expect(columns).toHaveLength(4);
    expect(columns.map((column) => column.getAttribute('data-status-tone')))
      .toEqual(['preparing', 'ready', 'on-delivery', 'delivered']);
    expect(columns.map((column) => column.querySelector('.col-title')?.textContent?.trim()))
      .toEqual(['Preparando', 'Pronto', 'Em entrega', 'Concluído']);

    columns.forEach((column) => {
      const title = column.querySelector<HTMLElement>('.col-title');
      expect(title).not.toBeNull();
      expect(title!.textContent?.trim().length).toBeGreaterThan(0);
    });
  });

  it('starts each board order collapsed with a compact code and customer header', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [buildOrder({ id: 'compact', code: 'CPT', status: OrderStatus.Preparing, customerName: 'Ana Souza', total: 42 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.status-grid')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.accepted-orders-list')).toBeNull();
    expect(fixture.componentInstance.isOrderExpanded('compact')).toBe(false);
    expect(fixture.componentInstance.expandedOrderIds().size).toBe(0);

    const card = findOrderCard(fixture, 'compact');
    const toggle = findOrderToggle(card);
    const body = orderBody(card);

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe(body.id);
    expect(toggle.querySelector('.order-code')?.textContent).toContain('#CPT');
    expect(toggle.querySelector('.order-customer')?.textContent).toContain('Ana Souza');
    expect(toggle.querySelector('.order-card-chevron')?.classList.contains('bi-chevron-down')).toBe(true);
    expect(toggle.querySelector('.order-card-chevron')?.classList.contains('bi-chevron-up')).toBe(false);
    expect(body.hasAttribute('hidden')).toBe(true);
    expect(body.querySelector('.card-footer')).not.toBeNull();
  });

  it('toggles an order open to reveal items, totals and the status action, then closes it', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [buildOrder({ id: 'compact', code: 'CPT', status: OrderStatus.Preparing, customerName: 'Ana Souza', total: 42 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: 'compact',
      code: 'CPT',
      customerName: 'Ana Souza',
      items: [{ productName: 'Suco', quantity: 1, unitPrice: 12, totalPrice: 12 }],
      subtotal: 42,
      deliveryFee: 0,
      total: 42,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = findOrderCard(fixture, 'compact');
    const toggle = findOrderToggle(card);
    const body = orderBody(card);

    toggle.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.isOrderExpanded('compact')).toBe(true);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.querySelector('.order-card-chevron')?.classList.contains('bi-chevron-up')).toBe(true);
    expect(toggle.querySelector('.order-card-chevron')?.classList.contains('bi-chevron-down')).toBe(false);
    expect(body.hasAttribute('hidden')).toBe(false);
    expect(body.textContent).toContain('Suco');
    expect(body.textContent!.replace(/\u00a0/g, ' ')).toContain('R$ 42,00');
    expect(body.querySelector('.board-action')?.textContent).toContain('Marcar como pronto');

    toggle.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.isOrderExpanded('compact')).toBe(false);
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(body.hasAttribute('hidden')).toBe(true);
  });

  it('keeps expansion state independent for each order id', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [
            buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, customerName: 'Ana', total: 10 }),
            buildOrder({ id: 'b', code: 'B-2', status: OrderStatus.Preparing, customerName: 'Bia', total: 20 }),
          ],
          totalItems: 2,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockImplementation((id: string) => of({
      id,
      code: id === 'a' ? 'A-1' : 'B-2',
      items: [],
      subtotal: 0,
      deliveryFee: 0,
      total: id === 'a' ? 10 : 20,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const cardA = expandOrderCard(fixture, 'a');
    expect(fixture.componentInstance.isOrderExpanded('a')).toBe(true);
    expect(fixture.componentInstance.isOrderExpanded('b')).toBe(false);
    expect(orderBody(cardA).hasAttribute('hidden')).toBe(false);

    const cardB = expandOrderCard(fixture, 'b');
    expect(fixture.componentInstance.isOrderExpanded('a')).toBe(true);
    expect(fixture.componentInstance.isOrderExpanded('b')).toBe(true);
    expect(orderBody(cardB).hasAttribute('hidden')).toBe(false);

    findOrderToggle(cardA).click();
    fixture.detectChanges();

    expect(fixture.componentInstance.isOrderExpanded('a')).toBe(false);
    expect(fixture.componentInstance.isOrderExpanded('b')).toBe(true);
    expect(orderBody(cardA).hasAttribute('hidden')).toBe(true);
    expect(orderBody(cardB).hasAttribute('hidden')).toBe(false);
  });

  it('does not open details or advance the order when the accordion header is toggled', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [buildOrder({ id: 'compact', code: 'CPT', status: OrderStatus.Preparing, customerName: 'Ana Souza', total: 42 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const toggle = findOrderToggle(findOrderCard(fixture, 'compact'));
    toggle.click();
    fixture.detectChanges();
    toggle.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.order-details-dialog')).toBeNull();
    expect(fixture.componentInstance.pendingAction()).toBeNull();
    expect(orderServiceMock.updateStoreOrderStatus).not.toHaveBeenCalled();
  });

  it('opens details from the expanded card without collapsing the accordion', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [buildOrder({ id: 'compact', code: 'CPT', status: OrderStatus.Preparing, customerName: 'Ana Souza', total: 42 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: 'compact',
      code: 'CPT',
      customerName: 'Ana Souza',
      items: [{ productName: 'Suco', quantity: 1, unitPrice: 12, totalPrice: 12 }],
      subtotal: 42,
      deliveryFee: 0,
      total: 42,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'compact');
    const detailsButton = card.querySelector<HTMLButtonElement>('.details-trigger');
    expect(detailsButton).not.toBeNull();

    detailsButton!.click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain('#CPT');
    expect(fixture.componentInstance.isOrderExpanded('compact')).toBe(true);
    expect(fixture.componentInstance.pendingAction()).toBeNull();
  });

  it('shows new order cards in the top panel', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', customerName: 'Joao', customerPhoneNumber: '119999', paymentMethod: 0 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('ABC');
    expect(fixture.nativeElement.textContent).toContain('Joao');

    const card = expandOrderCard(fixture, '1');
    expect(card.textContent).toContain('Aceitar pedido');
  });

  it('shows accepted orders in their status columns with actions', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: '2', code: 'DEF', status: OrderStatus.Preparing, total: 30, customerName: 'Ana' })], totalItems: 1 });
      }
      if (status === OrderStatus.Ready) {
        return of({ items: [buildOrder({ id: '3', code: 'GHI', status: OrderStatus.Ready, total: 40, customerName: 'Bia' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.status-stack .order-card').length).toBe(2);
    expect(fixture.nativeElement.querySelector('.status-grid')).not.toBeNull();

    expect(expandOrderCard(fixture, '2').textContent).toContain('Marcar como pronto');
    expect(expandOrderCard(fixture, '3').textContent).toContain('Saiu para entrega');
  });

  it('shows confirmation modal when advancing status', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: '2', code: 'DEF', status: OrderStatus.Preparing, total: 30 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '2');
    const btn = card.querySelector<HTMLButtonElement>('.action-orange');
    expect(btn).not.toBeNull();
    btn!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Confirmar');
    expect(fixture.nativeElement.textContent).toContain('Cancelar');
  });

  it('sorts cards inside each group by createdAtUtc descending', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [
            buildOrder({ id: 'older-received', code: 'OLD', createdAtUtc: '2026-08-04T10:00:00Z' }),
            buildOrder({ id: 'newer-received', code: 'NEW', createdAtUtc: '2026-08-04T10:05:00Z' }),
          ],
          totalItems: 2,
        });
      }

      if (status === OrderStatus.Preparing) {
        return of({
          items: [
            buildOrder({ id: 'older-accepted', code: 'P-OLD', status: OrderStatus.Preparing, createdAtUtc: '2026-08-04T10:10:00Z' }),
          ],
          totalItems: 1,
        });
      }

      if (status === OrderStatus.Delivered) {
        return of({
          items: [
            buildOrder({ id: 'newer-accepted', code: 'D-NEW', status: OrderStatus.Delivered, createdAtUtc: '2026-08-04T10:20:00Z' }),
            buildOrder({ id: 'older-delivered', code: 'D-OLD', status: OrderStatus.Delivered, createdAtUtc: '2026-08-04T10:15:00Z' }),
          ],
          totalItems: 2,
        });
      }

      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.newOrders().map((order) => order.id)).toEqual(['newer-received', 'older-received']);
    expect(fixture.componentInstance.statusGroups().preparing.map((order) => order.id)).toEqual(['older-accepted']);
    expect(fixture.componentInstance.boardColumns().map((column) => column.title))
      .toEqual(['Preparando', 'Pronto', 'Em entrega', 'Concluído']);
    expect(fixture.componentInstance.boardColumns()[3].orders.map((order) => order.id))
      .toEqual(['newer-accepted', 'older-delivered']);
  });

  it('prints the order when acceptance moves it from received to preparing', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.confirmAdvance(buildOrder({ id: 'accepted-order' }), OrderStatus.Preparing, 'Aceitar pedido');
    fixture.componentInstance.executeAdvance();

    expect(orderServiceMock.updateStoreOrderStatus).toHaveBeenCalledWith(
      'accepted-order',
      OrderStatus.Preparing,
      'Atualizado pelo painel do lojista',
    );
    expect(automaticPrintMock.enqueueAcceptedOrder).toHaveBeenCalledWith(
      expect.objectContaining({ orderId: 'accepted-order' }),
    );
  });

  it('keeps the acceptance flow running when automatic printing fails', () => {
    automaticPrintMock.enqueueAcceptedOrder.mockRejectedValue(new Error('printer offline'));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.confirmAdvance(buildOrder({ id: 'accepted-order' }), OrderStatus.Preparing, 'Aceitar pedido');
    fixture.componentInstance.executeAdvance();

    expect(orderServiceMock.updateStoreOrderStatus).toHaveBeenCalledWith(
      'accepted-order',
      OrderStatus.Preparing,
      'Atualizado pelo painel do lojista',
    );
    expect(shellMock.notifyOrderChanged).toHaveBeenCalledWith('accepted-order');
    expect(orderServiceMock.getStoreOrders).toHaveBeenCalled();
    expect(toastServiceMock.showError).not.toHaveBeenCalled();
  });

  it('does not print when changing to a status other than preparing', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.confirmAdvance(
      buildOrder({ id: 'ready-order', status: OrderStatus.Preparing }),
      OrderStatus.Ready,
      'Marcar pronto',
    );
    fixture.componentInstance.executeAdvance();

    expect(automaticPrintMock.enqueueAcceptedOrder).not.toHaveBeenCalled();
  });

  it('optimistically moves an order to the next status before the silent reload, even when reload fails', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: 'acc', code: 'ACC', status: OrderStatus.Received })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.statusGroups().received.map((o) => o.id)).toEqual(['acc']);

    orderServiceMock.getStoreOrders.mockReturnValue(throwError(() => new Error('reload failed')));

    fixture.componentInstance.confirmAdvance(
      fixture.componentInstance.statusGroups().received[0],
      OrderStatus.Preparing,
      'Aceitar pedido',
    );
    fixture.componentInstance.executeAdvance();
    fixture.detectChanges();

    expect(fixture.componentInstance.statusGroups().received.length).toBe(0);
    expect(fixture.componentInstance.statusGroups().preparing.map((o) => o.id)).toEqual(['acc']);
  });

  it('consumes the order query param and highlights the target order with focus', () => {
    jest.useFakeTimers();
    activatedRouteMock.snapshot.queryParamMap = convertToParamMap({ order: 'target-order' });

    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: jest.fn(),
    });

    const scrollIntoViewSpy = jest.spyOn(HTMLElement.prototype, 'scrollIntoView').mockImplementation(jest.fn());
    const focusSpy = jest.spyOn(HTMLElement.prototype, 'focus').mockImplementation(jest.fn());

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [
            buildOrder({ id: 'target-order', code: 'TARGET', createdAtUtc: '2026-08-04T10:05:00Z' }),
            buildOrder({ id: 'other-order', code: 'OTHER', createdAtUtc: '2026-08-04T10:00:00Z' }),
          ],
          totalItems: 2,
        });
      }

      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();
    jest.runAllTimers();
    fixture.detectChanges();

    const targetCard = fixture.nativeElement.querySelector('[data-order-id="target-order"]');

    expect(targetCard).not.toBeNull();
    expect(targetCard.classList.contains('is-target')).toBe(true);
    expect(scrollIntoViewSpy).toHaveBeenCalled();
    expect(focusSpy).toHaveBeenCalled();
  });

  it('highlights and focuses an accepted order targeted by the query param', () => {
    jest.useFakeTimers();
    activatedRouteMock.snapshot.queryParamMap = convertToParamMap({ order: 'target-accepted' });

    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: jest.fn(),
    });

    const scrollIntoViewSpy = jest.spyOn(HTMLElement.prototype, 'scrollIntoView').mockImplementation(jest.fn());
    const focusSpy = jest.spyOn(HTMLElement.prototype, 'focus').mockImplementation(jest.fn());

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [buildOrder({ id: 'target-accepted', code: 'TARGET', status: OrderStatus.Preparing })],
          totalItems: 1,
        });
      }

      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();
    jest.runAllTimers();
    fixture.detectChanges();

    const targetCard = fixture.nativeElement.querySelector(
      '.status-col .order-card[data-order-id="target-accepted"]',
    );
    expect(targetCard).not.toBeNull();
    expect(targetCard.classList.contains('is-target')).toBe(true);
    expect(scrollIntoViewSpy).toHaveBeenCalled();
    expect(focusSpy).toHaveBeenCalled();
  });

  it('reloads orders when a new order pulse is emitted', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    shellMock.newOrderPulse.mockReturnValue({ id: 'pulse-1' });
    fixture.componentInstance['effectScheduler']?.();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreOrders).toHaveBeenCalled();
  });

  it('shows code and customer contact on the new-order card in the top panel', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123456', customerName: 'Joao Silva', customerPhoneNumber: '1199999' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const refCard = expandOrderCard(fixture, '1');
    const toggle = findOrderToggle(refCard);
    expect(toggle.querySelector('.order-code')?.textContent).toContain('#URB-123456');
    expect(toggle.querySelector('.order-customer')?.textContent).toContain('Joao Silva');
    expect(refCard.textContent).toContain('1199999');
  });

  it('hides seller-completed orders from the day board', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({
          items: [
            buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, sellerCompletedAtUtc: '2026-08-04T11:00:00Z' }),
            buildOrder({ id: 'pending', code: 'URB-2', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' }),
          ],
          totalItems: 2,
        });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.statusGroups().delivered.map((o) => o.id)).toEqual(['pending']);
    expect(fixture.componentInstance.boardColumns()[3].orders.map((o) => o.id)).toEqual(['pending']);
    expect(fixture.nativeElement.querySelector('.status-col .order-card[data-order-id="done"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('.status-col .order-card[data-order-id="pending"]')).not.toBeNull();
  });

  it('shows the Concluído action for delivered orders confirmed by the customer', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'done');
    expect(card.querySelector('.board-action.action-green')?.textContent).toContain('Concluído');
  });

  it('does not show Concluído for delivered orders without customer confirmation', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'wait', code: 'URB-3', status: OrderStatus.Delivered })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'wait');
    expect(card.querySelector('.board-action.action-green')).toBeNull();
    expect(card.textContent).not.toContain('Concluído');
  });

  it('allows completing a pickup order delivered at the counter without customer confirmation', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'pickup-done', code: 'PU-2', status: OrderStatus.Delivered, fulfillmentType: FulfillmentType.PickUp })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const delivered = fixture.componentInstance.statusGroups().delivered[0];
    expect(fixture.componentInstance.canComplete(delivered)).toBe(true);

    const card = expandOrderCard(fixture, 'pickup-done');
    expect(card.textContent).toContain('Concluir retirada');
    expect(card.textContent).not.toContain('Concluído');
    expect(card.querySelector('.board-action.action-green')).not.toBeNull();
  });

  it('completes a pickup order through the Concluir retirada action', () => {
    orderServiceMock.completeOrder.mockReturnValue(of({ sellerCompletedAtUtc: '2026-08-04T12:00:00Z' }));
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'pickup-done', code: 'PU-2', status: OrderStatus.Delivered, fulfillmentType: FulfillmentType.PickUp })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const action = fixture.componentInstance.cardAction(fixture.componentInstance.statusGroups().delivered[0]);
    expect(action).toEqual({ kind: 'complete', label: 'Concluir retirada', css: 'action-green' });

    const card = expandOrderCard(fixture, 'pickup-done');
    card.querySelector<HTMLButtonElement>('.board-action')!.click();
    fixture.detectChanges();

    expect(orderServiceMock.completeOrder).toHaveBeenCalledWith('pickup-done');
  });

  it('keeps totals and action in the card footer as the last element for differing item counts', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [
            buildOrder({ id: 'many', code: 'MANY', status: OrderStatus.Preparing, total: 90 }),
            buildOrder({ id: 'one', code: 'ONE', status: OrderStatus.Preparing, total: 20 }),
          ],
          totalItems: 2,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockImplementation((id: string) => {
      const items = id === 'many'
        ? [
            { productName: 'Item 1', quantity: 1, unitPrice: 10, totalPrice: 10 },
            { productName: 'Item 2', quantity: 2, unitPrice: 20, totalPrice: 40 },
            { productName: 'Item 3', quantity: 1, unitPrice: 40, totalPrice: 40 },
          ]
        : [{ productName: 'Item 1', quantity: 2, unitPrice: 10, totalPrice: 20 }];
      return of({ id, code: id === 'many' ? 'MANY' : 'ONE', items, subtotal: 0, deliveryFee: 0, total: id === 'many' ? 90 : 20, createdAtUtc: '' });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    for (const id of ['many', 'one']) {
      const card = expandOrderCard(fixture, id);
      expect(card.firstElementChild?.classList.contains('order-card-toggle')).toBe(true);
      const body = orderBody(card);
      const last = body.lastElementChild;
      expect(last?.classList.contains('card-footer')).toBe(true);
      expect(last!.querySelector('.total-row')).not.toBeNull();
      expect(last!.querySelector('.board-action')).not.toBeNull();
      expect(last!.lastElementChild?.classList.contains('board-action')).toBe(true);
    }
  });

  it('completes an order and removes it from the board on success', () => {
    orderServiceMock.completeOrder.mockReturnValue(of({ sellerCompletedAtUtc: '2026-08-04T12:00:00Z' }));
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.completeOrder(fixture.componentInstance.statusGroups().delivered[0]);

    expect(orderServiceMock.completeOrder).toHaveBeenCalledWith('done');
    expect(fixture.componentInstance.statusGroups().delivered.length).toBe(0);
    expect(toastServiceMock.showSuccess).toHaveBeenCalled();
  });

  it('shows a loading state and disables the action while completing', () => {
    const subject = new Subject<any>();
    orderServiceMock.completeOrder.mockReturnValue(subject.asObservable());
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expandOrderCard(fixture, 'done');
    fixture.componentInstance.completeOrder(fixture.componentInstance.statusGroups().delivered[0]);
    fixture.detectChanges();

    expect(fixture.componentInstance.completingOrderId()).toBe('done');
    expect(fixture.nativeElement.textContent).toContain('Concluindo');
    expect(fixture.nativeElement.querySelector('.board-action.action-green').disabled).toBe(true);

    subject.next({ sellerCompletedAtUtc: '2026-08-04T12:00:00Z' });
    subject.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.completingOrderId()).toBeNull();
  });

  it('releases the Concluído button when completion fails', () => {
    orderServiceMock.completeOrder.mockReturnValue(throwError(() => new Error('fail')));
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.completeOrder(fixture.componentInstance.statusGroups().delivered[0]);

    expect(fixture.componentInstance.completingOrderId()).toBeNull();
    expect(fixture.componentInstance.statusGroups().delivered.length).toBe(1);
    expect(toastServiceMock.showError).toHaveBeenCalled();
  });

  it('reloads silently when OrderStatusUpdated arrives for a known order', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: '2', code: 'DEF', status: OrderStatus.Preparing })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const listener = signalRServiceMock.onSellerEvent.mock.calls.find(([name]: [string]) => name === 'OrderStatusUpdated')?.[1];
    expect(listener).toBeDefined();

    const before = orderServiceMock.getStoreOrders.mock.calls.length;
    listener({ orderId: '2', status: OrderStatus.Ready });
    expect(orderServiceMock.getStoreOrders.mock.calls.length).toBeGreaterThan(before);
  });

  it('ignores OrderStatusUpdated for an unknown order', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const listener = signalRServiceMock.onSellerEvent.mock.calls.find(([name]: [string]) => name === 'OrderStatusUpdated')?.[1];
    expect(listener).toBeDefined();

    const before = orderServiceMock.getStoreOrders.mock.calls.length;
    listener({ orderId: 'unknown' });
    expect(orderServiceMock.getStoreOrders.mock.calls.length).toBe(before);
  });

  it('enqueues automatic printing when the seller acceptance event arrives', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const listener = signalRServiceMock.onSellerEvent.mock.calls.find(
      ([name]: [string]) => name === 'OrderAcceptedForPrinting',
    )?.[1];
    expect(listener).toBeDefined();

    listener({ orderId: 'accepted-order', storeId: 'store-1', sequence: 2, acceptedAtUtc: '2026-10-19T12:00:00Z' });

    expect(automaticPrintMock.enqueueAcceptedOrder).toHaveBeenCalledWith(
      expect.objectContaining({ orderId: 'accepted-order', sequence: 2 }),
    );
  });

  it('reconciles accepted orders after loading the board and after reconnecting', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(automaticPrintMock.reconcilePendingJobs).toHaveBeenCalled();

    automaticPrintMock.reconcilePendingJobs.mockClear();
    signalRServiceMock.isSellerConnected.set(true);
    fixture.detectChanges();

    expect(automaticPrintMock.reconcilePendingJobs).toHaveBeenCalled();
  });

  it('removes the OrderStatusUpdated listener on destroy', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const listener = signalRServiceMock.onSellerEvent.mock.calls.find(([name]: [string]) => name === 'OrderStatusUpdated')?.[1];
    expect(listener).toBeDefined();

    fixture.destroy();

    expect(signalRServiceMock.removeSellerListener).toHaveBeenCalledWith('OrderStatusUpdated', listener);
  });

  it('does not show the fullscreen toggle outside Windows', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.kiosk-toggle')).toBeNull();
  });

  describe('fullscreen kiosk on Windows', () => {
    let fullscreenElement: Element | null;
    let requestFullscreenMock: jest.Mock;
    let exitFullscreenMock: jest.Mock;

    beforeEach(() => {
      isWindowsPlatformMock.mockReturnValue(true);
      fullscreenElement = null;

      Object.defineProperty(document, 'fullscreenElement', { configurable: true, get: () => fullscreenElement });

      requestFullscreenMock = jest.fn(() => {
        fullscreenElement = document.documentElement;
        return Promise.resolve();
      });
      Object.defineProperty(document.documentElement, 'requestFullscreen', { configurable: true, value: requestFullscreenMock });

      exitFullscreenMock = jest.fn(() => {
        fullscreenElement = null;
        return Promise.resolve();
      });
      Object.defineProperty(document, 'exitFullscreen', { configurable: true, value: exitFullscreenMock });
    });

    it('shows the fullscreen toggle with an accessible pressed state', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.kiosk-toggle');
      expect(button).not.toBeNull();
      expect(button.getAttribute('aria-pressed')).toBe('false');
    });

    it('enters fullscreen via requestFullscreen and reflects the state', async () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      await fixture.componentInstance.toggleFullscreen();
      fixture.detectChanges();

      expect(requestFullscreenMock).toHaveBeenCalled();
      expect(fixture.componentInstance.isFullscreen()).toBe(true);

      const button = fixture.nativeElement.querySelector('.kiosk-toggle');
      expect(button.getAttribute('aria-pressed')).toBe('true');
    });

    it('exits fullscreen when Escape is pressed', async () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      await fixture.componentInstance.toggleFullscreen();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await Promise.resolve();
      await Promise.resolve();
      fixture.detectChanges();

      expect(exitFullscreenMock).toHaveBeenCalled();
      expect(fixture.componentInstance.isFullscreen()).toBe(false);
    });

    it('ignores non-Escape keys while in fullscreen', async () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      await fixture.componentInstance.toggleFullscreen();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

      expect(exitFullscreenMock).not.toHaveBeenCalled();
    });

    it('cleans up fullscreen listeners on destroy', () => {
      const removeSpy = jest.spyOn(document, 'removeEventListener');

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();
      fixture.destroy();

      expect(removeSpy).toHaveBeenCalledWith('keydown', expect.any(Function));
      expect(removeSpy).toHaveBeenCalledWith('fullscreenchange', expect.any(Function));

      removeSpy.mockRestore();
    });
  });

  it('keeps the newest load result and drops a stale load that resolves later', () => {
    const firstLoadSubjects: Subject<{ items: any[]; totalItems: number }>[] = [];
    let callCount = 0;

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      callCount += 1;
      if (callCount <= 5) {
        const subject = new Subject<{ items: any[]; totalItems: number }>();
        firstLoadSubjects.push(subject);
        return subject.asObservable();
      }
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: 'fresh', code: 'FRESH', customerName: 'Novo' })], totalItems: 1 });
      }
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep', code: 'PREP', status: OrderStatus.Preparing })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.load({ silent: true });
    fixture.detectChanges();

    expect(fixture.componentInstance.orders().map((order) => order.id)).toContain('fresh');

    for (const subject of firstLoadSubjects) {
      subject.next({ items: [], totalItems: 0 });
      subject.complete();
    }
    fixture.detectChanges();

    expect(fixture.componentInstance.orders().map((order) => order.id)).toContain('fresh');
  });

  it('opens full order details from the expanded new-order card', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria', customerPhoneNumber: '119888', addressSummary: 'Av. Principal 100', paymentMethod: PaymentMethod.PixOnline, total: 75 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-123',
      items: [{ productName: 'X-Burger', quantity: 2, unitPrice: 30, totalPrice: 60 }],
      total: 75,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const detailsTrigger = card.querySelector<HTMLButtonElement>('.details-trigger');
    expect(detailsTrigger).not.toBeNull();
    detailsTrigger!.click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain('URB-123');
    expect(dialog.textContent).toContain('Maria');
    expect(dialog.textContent).toContain('119888');
    expect(dialog.textContent).toContain('Av. Principal 100');
    expect(dialog.textContent).toContain('Pix já pago');
    expect(dialog.textContent).toContain('X-Burger');
  });

  it('opens the same full details modal from the expanded top-panel card without triggering accept', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria', customerPhoneNumber: '119888', addressSummary: 'Av. Principal 100', paymentMethod: PaymentMethod.PixOnline, total: 75 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-123',
      items: [{ productName: 'X-Burger', quantity: 2, unitPrice: 30, totalPrice: 60 }],
      total: 75,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const detailsTrigger = card.querySelector<HTMLButtonElement>('.details-trigger');
    expect(detailsTrigger).not.toBeNull();

    const acceptBtn = card.querySelector('.board-action');
    expect(acceptBtn).not.toBeNull();

    detailsTrigger!.click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain('URB-123');
    expect(dialog.textContent).toContain('Maria');
    expect(dialog.textContent).toContain('X-Burger');

    expect(orderServiceMock.updateStoreOrderStatus).not.toHaveBeenCalled();
    expect(fixture.componentInstance.pendingAction()).toBeNull();
  });

  it('shows a details trigger in every status column card that opens details without advancing', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      const items: any[] = [];
      if (status === OrderStatus.Preparing) items.push(buildOrder({ id: 'prep', code: 'PREP', status: OrderStatus.Preparing }));
      if (status === OrderStatus.Ready) items.push(buildOrder({ id: 'ready', code: 'READY', status: OrderStatus.Ready }));
      if (status === OrderStatus.OnDelivery) items.push(buildOrder({ id: 'deliv', code: 'DELIV', status: OrderStatus.OnDelivery }));
      if (status === OrderStatus.Delivered) items.push(buildOrder({ id: 'done', code: 'DONE', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' }));
      return of({ items, totalItems: items.length });
    });
    orderServiceMock.getStoreOrder.mockImplementation((id: string) => of({
      id,
      code: { prep: 'PREP', ready: 'READY', deliv: 'DELIV', done: 'DONE' }[id] ?? '',
      customerName: 'Cliente',
      items: [],
      subtotal: 0,
      deliveryFee: 0,
      total: 0,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    for (const [id, code] of [['prep', 'PREP'], ['ready', 'READY'], ['deliv', 'DELIV'], ['done', 'DONE']] as const) {
      const card = expandOrderCard(fixture, id);
      const detailsButton = card.querySelector<HTMLButtonElement>('.details-trigger');
      expect(detailsButton).not.toBeNull();
      detailsButton!.click();
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
      expect(dialog.textContent).toContain(code);

      fixture.componentInstance.closeOrderDetails();
      fixture.detectChanges();
    }

    expect(orderServiceMock.updateStoreOrderStatus).not.toHaveBeenCalled();
    expect(fixture.componentInstance.pendingAction()).toBeNull();
  });

  it('prefetches order items for accepted orders', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, customerName: 'Carlos', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: 'prep-1',
      code: 'PREP-1',
      items: [{ productName: 'Suco', quantity: 1, unitPrice: 12, totalPrice: 12 }],
      subtotal: 40,
      deliveryFee: 0,
      total: 40,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(orderServiceMock.getStoreOrder).toHaveBeenCalledWith('prep-1');
    const card = expandOrderCard(fixture, 'prep-1');
    expect(card.textContent).toContain('Suco');
  });

  it('closes the details modal via the close button', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('1');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.order-details-dialog')).not.toBeNull();

    fixture.nativeElement.querySelector('.details-close').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.order-details-dialog')).toBeNull();
  });

  it('does not let a late detail response overwrite the newly selected order', () => {
    const aSubject = new Subject<any>();
    const bSubject = new Subject<any>();

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [
            buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, customerName: 'Ana', total: 10 }),
            buildOrder({ id: 'b', code: 'B-2', status: OrderStatus.Preparing, customerName: 'Bia', total: 20 }),
          ],
          totalItems: 2,
        });
      }
      return of({ items: [], totalItems: 0 });
    });

    orderServiceMock.getStoreOrder.mockImplementation((id: string) => {
      if (id === 'a') return aSubject.asObservable();
      if (id === 'b') return bSubject.asObservable();
      return of({ id, code: '', items: [], total: 0, createdAtUtc: '' });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('a');
    fixture.componentInstance.openOrderDetails('b');

    expect(fixture.componentInstance.selectedOrderId()).toBe('b');
    expect(fixture.componentInstance.selectedOrderLoading()).toBe(true);

    aSubject.next({
      id: 'a',
      code: 'A-1',
      customerName: 'Ana',
      items: [{ productName: 'Item A', quantity: 1, unitPrice: 10, totalPrice: 10 }],
      subtotal: 10,
      deliveryFee: 0,
      total: 10,
      createdAtUtc: '',
    });
    aSubject.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedOrderLoading()).toBe(true);
    expect(fixture.componentInstance.orderItems('b').length).toBe(0);

    bSubject.next({
      id: 'b',
      code: 'B-2',
      customerName: 'Bia',
      items: [{ productName: 'Item B', quantity: 2, unitPrice: 10, totalPrice: 20 }],
      subtotal: 20,
      deliveryFee: 5,
      total: 25,
      createdAtUtc: '',
    });
    bSubject.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedOrderLoading()).toBe(false);
    expect(fixture.componentInstance.orderItems('b')[0].productName).toBe('Item B');

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog.textContent).toContain('Item B');
    expect(dialog.textContent).not.toContain('Item A');
  });

  it('ignores a detail response that resolves after the details were closed', () => {
    const subject = new Subject<any>();

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, customerName: 'Ana', total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(subject.asObservable());

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('a');
    fixture.componentInstance.closeOrderDetails();

    subject.next({
      id: 'a',
      code: 'A-1',
      customerName: 'Ana',
      items: [{ productName: 'Item A', quantity: 1, unitPrice: 10, totalPrice: 10 }],
      subtotal: 10,
      deliveryFee: 0,
      total: 10,
      createdAtUtc: '',
    });
    subject.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedOrderId()).toBeNull();
    expect(fixture.nativeElement.querySelector('.order-details-dialog')).toBeNull();
  });

  it('restores focus to the element that opened the details after closing', () => {
    jest.useFakeTimers();

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1', code: 'URB-123', customerName: 'Maria', items: [], subtotal: 0, deliveryFee: 0, total: 0, createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const detailsButton = card.querySelector<HTMLButtonElement>('.details-trigger');
    expect(detailsButton).not.toBeNull();
    const focusSpy = jest.spyOn(detailsButton!, 'focus').mockImplementation(jest.fn());

    detailsButton!.click();
    fixture.detectChanges();

    fixture.componentInstance.closeOrderDetails();
    jest.runAllTimers();

    expect(focusSpy).toHaveBeenCalled();
  });

  it('closes the details when Escape is pressed on the dialog', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1', code: 'URB-123', customerName: 'Maria', items: [], subtotal: 0, deliveryFee: 0, total: 0, createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.order-details-dialog')).toBeNull();
  });

  it('closes the details when the overlay is clicked', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1', code: 'URB-123', customerName: 'Maria', items: [], subtotal: 0, deliveryFee: 0, total: 0, createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('1');
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.order-details-overlay').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.order-details-dialog')).toBeNull();
  });

  it('renders full order details including subtotal, fee, total and item annotations', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria', customerPhoneNumber: '119888', addressSummary: 'Av. Principal 100', paymentMethod: PaymentMethod.PixOnline, total: 75 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-123',
      customerName: 'Maria',
      customerPhoneNumber: '119888',
      addressStreet: 'Av. Principal',
      addressNumber: '100',
      paymentMethod: PaymentMethod.PixOnline,
      subtotal: 70,
      deliveryFee: 5,
      total: 75,
      createdAtUtc: '',
      items: [
        {
          productName: 'X-Burger',
          quantity: 2,
          unitPrice: 30,
          totalPrice: 60,
          notes: 'sem cebola',
          variationName: 'Grande',
          additionalNames: 'Bacon, Queijo',
        },
      ],
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    card.querySelector<HTMLButtonElement>('.details-trigger')!.click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();
    const text = dialog.textContent.replace(/\u00a0/g, ' ');

    expect(text).toContain('Maria');
    expect(text).toContain('119888');
    expect(text).toContain('Av. Principal 100');
    expect(text).toContain('Pix já pago');
    expect(text).toContain('X-Burger');
    expect(text).toContain('sem cebola');
    expect(text).toContain('Grande');
    expect(text).toContain('Bacon, Queijo');
    expect(text).toContain('Subtotal');
    expect(text).toContain('Taxa de entrega');
    expect(text).toContain('R$ 70,00');
    expect(text).toContain('R$ 5,00');
    expect(text).toContain('R$ 75,00');
  });

  it('does not let a stale loadOrderItems response overwrite newer order details', () => {
    const detailSubjects: Subject<any>[] = [];

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: 'x', code: 'X-1', customerName: 'X', total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockImplementation(() => {
      const subject = new Subject<any>();
      detailSubjects.push(subject);
      return subject.asObservable();
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(detailSubjects.length).toBe(1);

    fixture.componentInstance.load({ silent: true });
    fixture.detectChanges();

    expect(detailSubjects.length).toBe(2);

    detailSubjects[1].next({
      id: 'x',
      code: 'X-1',
      customerName: 'X',
      items: [{ productName: 'NEW', quantity: 1, unitPrice: 10, totalPrice: 10 }],
      total: 10,
      createdAtUtc: '',
    });
    detailSubjects[1].complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('x')[0].productName).toBe('NEW');

    detailSubjects[0].next({
      id: 'x',
      code: 'X-1',
      customerName: 'X',
      items: [{ productName: 'OLD', quantity: 1, unitPrice: 5, totalPrice: 5 }],
      total: 5,
      createdAtUtc: '',
    });
    detailSubjects[0].complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('x')[0].productName).toBe('NEW');
  });

  it('does not let a stale prefetch overwrite details loaded manually for the same order', () => {
    const prefetch = new Subject<any>();
    const manual = new Subject<any>();
    let getStoreOrderCalls = 0;

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: 'x', code: 'X-1', customerName: 'X', total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockImplementation((id: string) => {
      if (id !== 'x') return of({ id, code: '', items: [], total: 0, createdAtUtc: '' });
      getStoreOrderCalls += 1;
      return getStoreOrderCalls === 1 ? prefetch.asObservable() : manual.asObservable();
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(getStoreOrderCalls).toBe(1);

    fixture.componentInstance.openOrderDetails('x');

    manual.next({
      id: 'x',
      code: 'X-1',
      customerName: 'X',
      items: [{ productName: 'FRESH', quantity: 1, unitPrice: 10, totalPrice: 10 }],
      subtotal: 10,
      deliveryFee: 0,
      total: 10,
      createdAtUtc: '',
    });
    manual.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('x')[0].productName).toBe('FRESH');

    prefetch.next({
      id: 'x',
      code: 'X-1',
      customerName: 'X',
      items: [{ productName: 'STALE', quantity: 1, unitPrice: 5, totalPrice: 5 }],
      total: 5,
      createdAtUtc: '',
    });
    prefetch.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('x')[0].productName).toBe('FRESH');
  });

  it('does not let a prefetch started after a manual open overwrite the manual detail', () => {
    const manual = new Subject<any>();
    const prefetch = new Subject<any>();
    let getStoreOrderCalls = 0;

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'x', code: 'X-1', status: OrderStatus.Preparing, customerName: 'X', total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockImplementation((id: string) => {
      if (id !== 'x') return of({ id, code: '', items: [], total: 0, createdAtUtc: '' });
      getStoreOrderCalls += 1;
      return getStoreOrderCalls === 1 ? prefetch.asObservable() : manual.asObservable();
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(getStoreOrderCalls).toBe(1);

    fixture.componentInstance.openOrderDetails('x');
    expect(getStoreOrderCalls).toBe(2);

    manual.next({
      id: 'x',
      code: 'X-1',
      customerName: 'X',
      items: [{ productName: 'FRESH', quantity: 1, unitPrice: 10, totalPrice: 10 }],
      subtotal: 10,
      deliveryFee: 0,
      total: 10,
      createdAtUtc: '',
    });
    manual.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('x')[0].productName).toBe('FRESH');

    prefetch.next({
      id: 'x',
      code: 'X-1',
      customerName: 'X',
      items: [{ productName: 'STALE', quantity: 1, unitPrice: 5, totalPrice: 5 }],
      subtotal: 5,
      deliveryFee: 0,
      total: 5,
      createdAtUtc: '',
    });
    prefetch.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('x')[0].productName).toBe('FRESH');
  });

  it('renders full order details including full address, fulfillment type, notes and status', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria', total: 75 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-123',
      customerName: 'Maria',
      customerPhoneNumber: '119888',
      fulfillmentType: FulfillmentType.Delivery,
      status: OrderStatus.Received,
      paymentMethod: PaymentMethod.PixOnline,
      addressCep: '01234-567',
      addressStreet: 'Rua das Flores',
      addressNumber: '100',
      addressNeighborhood: 'Centro',
      addressCity: 'Sao Paulo',
      addressState: 'SP',
      addressComplement: 'Apto 42',
      addressReference: 'Proximo a padaria',
      notes: 'Entregar no portao',
      subtotal: 70,
      deliveryFee: 5,
      total: 75,
      createdAtUtc: '2026-08-04T10:00:00Z',
      items: [
        {
          productName: 'X-Burger',
          quantity: 2,
          unitPrice: 30,
          totalPrice: 60,
          notes: 'sem cebola',
          variationName: 'Grande',
          choiceOptionName: 'Com fritas',
          additionalNames: 'Bacon, Queijo',
        },
      ],
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();
    const text = dialog.textContent.replace(/\u00a0/g, ' ');

    expect(text).toContain('CEP 01234-567');
    expect(text).toContain('Rua das Flores 100');
    expect(text).toContain('Centro');
    expect(text).toContain('Sao Paulo - SP');
    expect(text).toContain('Apto 42');
    expect(text).toContain('Proximo a padaria');
    expect(text).toContain('Entrega');
    expect(text).toContain('Recebido');
    expect(text).toContain('Entregar no portao');
    expect(text).toContain('Com fritas');
  });

  it('shows an explicit error state with retry and close when details fail to load', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, customerName: 'Carlos', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(throwError(() => new Error('boom')));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('prep-1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain('Não foi possível');
    expect(dialog.textContent).toContain('Tentar novamente');
    expect(dialog.textContent).toContain('Fechar');
    expect(dialog.textContent).not.toContain('R$ 0,00');
  });

  it('retries loading details after an error', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, customerName: 'Carlos', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder
      .mockReturnValueOnce(throwError(() => new Error('boom')))
      .mockReturnValueOnce(throwError(() => new Error('boom')))
      .mockReturnValueOnce(of({
        id: 'prep-1',
        code: 'PREP-1',
        customerName: 'Carlos',
        items: [{ productName: 'Suco', quantity: 1, unitPrice: 12, totalPrice: 12 }],
        total: 40,
        createdAtUtc: '',
      }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('prep-1');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.order-details-dialog').textContent).toContain('Tentar novamente');

    fixture.nativeElement.querySelector('.btn-retry').click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog.textContent).toContain('Suco');
    expect(dialog.textContent).not.toContain('Tentar novamente');
  });

  it('preserves the trigger and restores focus to it when closing after a retry', () => {
    jest.useFakeTimers();

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, customerName: 'Carlos', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder
      .mockReturnValueOnce(throwError(() => new Error('boom')))
      .mockReturnValueOnce(throwError(() => new Error('boom')))
      .mockReturnValueOnce(of({
        id: 'prep-1',
        code: 'PREP-1',
        customerName: 'Carlos',
        items: [],
        subtotal: 0,
        deliveryFee: 0,
        total: 40,
        createdAtUtc: '',
      }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'prep-1');
    const detailsButton = card.querySelector<HTMLButtonElement>('.details-trigger');
    expect(detailsButton).not.toBeNull();
    const focusSpy = jest.spyOn(detailsButton!, 'focus').mockImplementation(jest.fn());

    detailsButton!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.btn-retry')).not.toBeNull();

    fixture.nativeElement.querySelector('.btn-retry').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.order-details-dialog').textContent).toContain('Carlos');

    fixture.componentInstance.closeOrderDetails();
    jest.runAllTimers();

    expect(focusSpy).toHaveBeenCalled();
  });

  it('traps focus within the details dialog', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, customerName: 'Carlos', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(throwError(() => new Error('boom')));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('prep-1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();

    const buttons = Array.from(dialog.querySelectorAll('button')).filter((b: HTMLButtonElement) => !b.hasAttribute('disabled'));
    expect(buttons.length).toBeGreaterThan(1);

    const first = buttons[0] as HTMLElement;
    const last = buttons[buttons.length - 1] as HTMLElement;

    last.focus();
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    expect(document.activeElement).toBe(first);

    first.focus();
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));
    expect(document.activeElement).toBe(last);
  });

  it('traps Shift+Tab to the last focusable element when focus is on the dialog itself', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, customerName: 'Carlos', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(throwError(() => new Error('boom')));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('prep-1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();

    const buttons = Array.from(dialog.querySelectorAll('button')).filter((b: HTMLButtonElement) => !b.hasAttribute('disabled'));
    const last = buttons[buttons.length - 1] as HTMLElement;

    dialog.focus();
    expect(document.activeElement).toBe(dialog);

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));

    expect(document.activeElement).toBe(last);
  });

  it('traps forward Tab to the first focusable element when focus is on the dialog itself', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, customerName: 'Carlos', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(throwError(() => new Error('boom')));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('prep-1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog).not.toBeNull();

    const buttons = Array.from(dialog.querySelectorAll('button')).filter((b: HTMLButtonElement) => !b.hasAttribute('disabled'));
    const first = buttons[0] as HTMLElement;

    dialog.focus();
    expect(document.activeElement).toBe(dialog);

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));

    expect(document.activeElement).toBe(first);
  });

  it('shows the updated status in the details modal after advancing and reloading', () => {
    let firstLoad = true;
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received && firstLoad) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123', customerName: 'Maria', total: 75 })], totalItems: 1 });
      }
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123', status: OrderStatus.Preparing, customerName: 'Maria', total: 75 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-123',
      customerName: 'Maria',
      status: OrderStatus.Received,
      fulfillmentType: FulfillmentType.Delivery,
      paymentMethod: PaymentMethod.PixOnline,
      subtotal: 70,
      deliveryFee: 5,
      total: 75,
      createdAtUtc: '',
      items: [{ productName: 'X-Burger', quantity: 2, unitPrice: 30, totalPrice: 60 }],
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('1');
    fixture.detectChanges();

    let dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog.textContent).toContain('Recebido');
    expect(dialog.textContent).toContain('X-Burger');

    firstLoad = false;
    fixture.componentInstance.confirmAdvance(
      buildOrder({ id: '1', code: 'URB-123', status: OrderStatus.Received }),
      OrderStatus.Preparing,
      'Aceitar pedido',
    );
    fixture.componentInstance.executeAdvance();
    fixture.detectChanges();

    dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog.textContent).toContain('Preparando');
    expect(dialog.textContent).not.toContain('Recebido');
    expect(dialog.textContent).toContain('X-Burger');
  });

  it('shows a blue pickup badge on pickup orders in the board cards', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [buildOrder({ id: '1', code: 'URB-1', fulfillmentType: FulfillmentType.PickUp, total: 30 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({ id: '1', code: 'URB-1', items: [], subtotal: 30, deliveryFee: 0, total: 30, createdAtUtc: '' }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const badge = card.querySelector('.pickup-badge');
    expect(badge).not.toBeNull();
    expect(badge!.textContent).toContain('Retirada no balcão');
  });

  it('does not show the pickup badge on delivery orders', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [buildOrder({ id: '1', code: 'URB-1', fulfillmentType: FulfillmentType.Delivery, total: 30 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({ id: '1', code: 'URB-1', items: [], subtotal: 30, deliveryFee: 0, total: 30, createdAtUtc: '' }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.new-orders-list .pickup-badge')).toBeNull();
  });

  it('renders each item line with quantity, name and line total, plus option names and prices', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-1', total: 68 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-1',
      items: [
        {
          productName: 'X-Burger',
          quantity: 2,
          unitPrice: 30,
          totalPrice: 60,
          variationName: 'Grande',
          optionPrices: [
            { name: 'Bacon', price: 4 },
            { name: 'Cheddar', price: 4 },
          ],
        },
      ],
      subtotal: 68,
      deliveryFee: 0,
      total: 68,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const text = card.textContent.replace(/\u00a0/g, ' ');
    expect(text).toContain('2x');
    expect(text).toContain('X-Burger');
    expect(text).toContain('R$ 60,00');
    expect(text).toContain('Grande');
    expect(text).toContain('Bacon');
    expect(text).toContain('R$ 4,00');
    expect(text).toContain('Cheddar');
    expect(text).not.toContain('cada');
    expect(text).not.toContain('Sem opções');
  });

  it('keeps two lines of the same product with different customizations distinct', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-1', total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-1',
      items: [
        { productName: 'Refri', quantity: 1, unitPrice: 10, totalPrice: 10, optionPrices: [{ name: 'Coca', price: 0 }] },
        { productName: 'Refri', quantity: 1, unitPrice: 10, totalPrice: 10, optionPrices: [{ name: 'Guarana', price: 0 }] },
      ],
      subtotal: 20,
      deliveryFee: 0,
      total: 20,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    expect(card.querySelectorAll('.item-row').length).toBe(2);
    expect(card.textContent).toContain('Coca');
    expect(card.textContent).toContain('Guarana');
  });

  it('renders subtotal, delivery fee and total from the backend summary', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [buildOrder({ id: '1', code: 'URB-1', subtotal: 70, deliveryFee: 5, total: 75 })],
          totalItems: 1,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({ id: '1', code: 'URB-1', items: [], subtotal: 70, deliveryFee: 5, total: 75, createdAtUtc: '' }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const text = card.textContent.replace(/\u00a0/g, ' ');
    expect(text).toContain('Subtotal');
    expect(text).toContain('Taxa de entrega');
    expect(text).toContain('Total');
    expect(text).toContain('R$ 70,00');
    expect(text).toContain('R$ 5,00');
    expect(text).toContain('R$ 75,00');
  });

  it('renders the status columns with empty groups and without order cards', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.status-flow')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.status-grid')).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.status-col').length).toBe(4);
    expect(fixture.nativeElement.querySelector('.accepted-orders-list')).toBeNull();
    expect(fixture.nativeElement.querySelector('.order-card')).toBeNull();
    expect(fixture.nativeElement.querySelector('.order-card-toggle')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Novos pedidos');
    expect(fixture.nativeElement.textContent).toContain('Nenhum pedido novo');
    expect(fixture.nativeElement.textContent).not.toContain('Pedidos aceitos');
    expect(fixture.nativeElement.textContent).not.toContain('Nenhum pedido aceito');
    expect(fixture.nativeElement.textContent).toContain('Preparando');
    expect(fixture.nativeElement.textContent).toContain('Em entrega');
    expect(fixture.nativeElement.textContent).toContain('Concluído');
  });

  it('shows detailed items in the accepted-order column card', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep', code: 'PREP', status: OrderStatus.Preparing, total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: 'prep',
      code: 'PREP',
      items: [{ productName: 'Suco', quantity: 1, unitPrice: 12, totalPrice: 12 }],
      subtotal: 40,
      deliveryFee: 0,
      total: 40,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'prep');
    expect(card.textContent).toContain('Suco');
    expect(card.textContent).toContain('Marcar como pronto');
    const detailsButton = card.querySelector('.details-trigger');
    expect(detailsButton).not.toBeNull();
  });

  it('renders optionPrices with individual names and values in the details modal without duplicating legacy names', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-1', total: 68 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-1',
      items: [
        {
          productName: 'X-Burger',
          quantity: 1,
          unitPrice: 60,
          totalPrice: 60,
          choiceOptionName: 'Com fritas',
          additionalNames: 'Bacon, Queijo',
          optionPrices: [
            { name: 'Bacon', price: 4 },
            { name: 'Cheddar', price: 4 },
          ],
        },
      ],
      subtotal: 68,
      deliveryFee: 0,
      total: 68,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    const text = dialog.textContent.replace(/\u00a0/g, ' ');
    expect(text).toContain('Bacon');
    expect(text).toContain('R$ 4,00');
    expect(text).toContain('Cheddar');
    expect(text).not.toContain('Com fritas');
    expect(text).not.toContain('Bacon, Queijo');
  });

  it('renders legacy customization names on the card when optionPrices are absent', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-1', total: 60 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-1',
      items: [
        {
          productName: 'X-Burger',
          quantity: 1,
          unitPrice: 60,
          totalPrice: 60,
          choiceOptionName: 'Com fritas',
          additionalNames: 'Bacon, Queijo',
        },
      ],
      subtotal: 60,
      deliveryFee: 0,
      total: 60,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const text = card.textContent.replace(/\u00a0/g, ' ');
    expect(text).toContain('Com fritas');
    expect(text).toContain('Bacon, Queijo');
    expect(text).not.toContain('R$ 4,00');
  });

  it('offers Concluir retirada for a pickup order in Ready status, advancing straight to Delivered', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Ready) {
        return of({ items: [buildOrder({ id: 'pickup-ready', code: 'PU-1', status: OrderStatus.Ready, fulfillmentType: FulfillmentType.PickUp, total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'pickup-ready');
    expect(card.textContent).toContain('Concluir retirada');
    expect(card.textContent).not.toContain('Saiu para entrega');

    const action = fixture.componentInstance.cardAction(
      fixture.componentInstance.statusGroups().ready[0],
    );
    expect(action).toEqual({ kind: 'advance', label: 'Concluir retirada', nextStatus: OrderStatus.Delivered, css: 'action-blue' });

    fixture.componentInstance.confirmAdvance(
      fixture.componentInstance.statusGroups().ready[0],
      (action as any).nextStatus,
      (action as any).label,
    );
    fixture.componentInstance.executeAdvance();

    expect(orderServiceMock.updateStoreOrderStatus).toHaveBeenCalledWith('pickup-ready', OrderStatus.Delivered, 'Atualizado pelo painel do lojista');
  });

  it('keeps other order details when a single prefetch fails and exposes a retry state', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({
          items: [
            buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, total: 10 }),
            buildOrder({ id: 'b', code: 'B-2', status: OrderStatus.Preparing, total: 20 }),
          ],
          totalItems: 2,
        });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockImplementation((id: string) => {
      if (id === 'a') return throwError(() => new Error('boom'));
      return of({ id, code: 'B-2', customerName: 'Bia', items: [{ productName: 'Suco', quantity: 1, unitPrice: 20, totalPrice: 20 }], subtotal: 20, deliveryFee: 0, total: 20, createdAtUtc: '' });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('b')[0].productName).toBe('Suco');
    expect(fixture.componentInstance.hasItemsError('a')).toBe(true);

    const cardB = expandOrderCard(fixture, 'b');
    expect(cardB.textContent).toContain('Suco');

    const cardA = expandOrderCard(fixture, 'a');
    expect(cardA.querySelector('.items-retry')).not.toBeNull();
  });

  it('retries loading items for a card whose prefetch failed', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder
      .mockImplementationOnce(() => throwError(() => new Error('boom')))
      .mockImplementationOnce(() => of({ id: 'a', code: 'A-1', customerName: 'Ana', items: [{ productName: 'Suco', quantity: 1, unitPrice: 10, totalPrice: 10 }], subtotal: 10, deliveryFee: 0, total: 10, createdAtUtc: '' }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.hasItemsError('a')).toBe(true);

    fixture.componentInstance.retryItems('a');
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('a')[0].productName).toBe('Suco');
    expect(fixture.componentInstance.hasItemsError('a')).toBe(false);
  });

  it('ignores a stale retry response when a newer retry supersedes it', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    const older = new Subject<any>();
    const newer = new Subject<any>();
    orderServiceMock.getStoreOrder
      .mockImplementationOnce(() => throwError(() => new Error('boom')))
      .mockImplementationOnce(() => older.asObservable())
      .mockImplementationOnce(() => newer.asObservable());

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.hasItemsError('a')).toBe(true);

    fixture.componentInstance.retryItems('a');
    fixture.componentInstance.retryItems('a');

    older.next({ id: 'a', code: 'A-1', customerName: 'Ana', items: [{ productName: 'Antigo', quantity: 1, unitPrice: 10, totalPrice: 10 }], subtotal: 10, deliveryFee: 0, total: 10, createdAtUtc: '' });
    older.complete();

    expect(fixture.componentInstance.orderItems('a').length).toBe(0);
    expect(fixture.componentInstance.isRetrying('a')).toBe(true);

    newer.next({ id: 'a', code: 'A-1', customerName: 'Ana', items: [{ productName: 'Suco', quantity: 1, unitPrice: 10, totalPrice: 10 }], subtotal: 10, deliveryFee: 0, total: 10, createdAtUtc: '' });
    newer.complete();

    expect(fixture.componentInstance.orderItems('a')[0].productName).toBe('Suco');
    expect(fixture.componentInstance.isRetrying('a')).toBe(false);
    expect(fixture.componentInstance.hasItemsError('a')).toBe(false);
  });

  it('marks an order as retrying while the request is in flight', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    const inFlight = new Subject<any>();
    orderServiceMock.getStoreOrder
      .mockImplementationOnce(() => throwError(() => new Error('boom')))
      .mockImplementationOnce(() => inFlight.asObservable());

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.hasItemsError('a')).toBe(true);

    fixture.componentInstance.retryItems('a');
    expect(fixture.componentInstance.isRetrying('a')).toBe(true);

    inFlight.next({ id: 'a', code: 'A-1', customerName: 'Ana', items: [{ productName: 'Suco', quantity: 1, unitPrice: 10, totalPrice: 10 }], subtotal: 10, deliveryFee: 0, total: 10, createdAtUtc: '' });
    inFlight.complete();

    expect(fixture.componentInstance.isRetrying('a')).toBe(false);
    expect(fixture.componentInstance.orderItems('a')[0].productName).toBe('Suco');
  });

  it('does not let a stale prefetch overwrite items retried for the same order', () => {
    const prefetch = new Subject<any>();
    const retryResp = new Subject<any>();
    let getStoreOrderCalls = 0;

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockImplementation((id: string) => {
      if (id !== 'a') return of({ id, code: '', items: [], total: 0, createdAtUtc: '' });
      getStoreOrderCalls += 1;
      return getStoreOrderCalls === 1 ? prefetch.asObservable() : retryResp.asObservable();
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(getStoreOrderCalls).toBe(1);

    fixture.componentInstance.retryItems('a');
    expect(getStoreOrderCalls).toBe(2);

    retryResp.next({
      id: 'a',
      code: 'A-1',
      customerName: 'Ana',
      items: [{ productName: 'FRESH', quantity: 1, unitPrice: 10, totalPrice: 10 }],
      subtotal: 10,
      deliveryFee: 0,
      total: 10,
      createdAtUtc: '',
    });
    retryResp.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('a')[0].productName).toBe('FRESH');

    prefetch.next({
      id: 'a',
      code: 'A-1',
      customerName: 'Ana',
      items: [{ productName: 'STALE', quantity: 1, unitPrice: 5, totalPrice: 5 }],
      subtotal: 5,
      deliveryFee: 0,
      total: 5,
      createdAtUtc: '',
    });
    prefetch.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.orderItems('a')[0].productName).toBe('FRESH');
  });

  describe('day board (period selector removed)', () => {
    const statusOrder = [
      OrderStatus.Received,
      OrderStatus.Preparing,
      OrderStatus.Ready,
      OrderStatus.OnDelivery,
      OrderStatus.Delivered,
    ];

    it('loads orders with Sao Paulo today boundaries by default', () => {
      jest.useFakeTimers();
      jest.setSystemTime(new Date('2026-07-29T12:00:00.000Z'));

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      const calls = orderServiceMock.getStoreOrders.mock.calls;
      expect(calls.map(([query]: any) => query.status)).toEqual(statusOrder);
      for (const [query] of calls) {
        expect(query.pageSize).toBe(50);
        expect(query.startDateUtc).toBe('2026-07-29T03:00:00.000Z');
        expect(query.endDateUtc).toBe('2026-07-29T12:00:00.000Z');
      }
    });

    it('defaults to the Hoje period', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance.selectedPeriod()).toBe('today');
    });

    it('keeps the page title on the day board', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      const title = fixture.nativeElement.querySelector('.page-title');
      expect(title.textContent).toContain('Pedidos do dia');
    });

    it('does not render the Hoje/Semana/Mês period selector and keeps refresh', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.segmented')).toBeNull();
      expect(fixture.nativeElement.querySelector('[aria-label="Filtrar pedidos por período"]')).toBeNull();

      const topButtons = Array.from(
        fixture.nativeElement.querySelectorAll<HTMLButtonElement>('.top-actions button'),
      ).map((button) => button.textContent?.trim() ?? '');

      expect(topButtons.some((label) => ['Hoje', 'Semana', 'Mês'].includes(label))).toBe(false);
      expect(topButtons).toContain('Atualizar');
    });
  });

  it('omits the delivery fee line for pickup orders in the card', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-1', fulfillmentType: FulfillmentType.PickUp, subtotal: 30, deliveryFee: 0, total: 30 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({ id: '1', code: 'URB-1', fulfillmentType: FulfillmentType.PickUp, items: [], subtotal: 30, deliveryFee: 0, total: 30, createdAtUtc: '' }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    expect(card.textContent).toContain('Subtotal');
    expect(card.textContent).toContain('Total');
    expect(card.textContent).not.toContain('Taxa de entrega');
  });

  it('omits the delivery fee line for pickup orders in the details dialog', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-1', fulfillmentType: FulfillmentType.PickUp, total: 30 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: '1',
      code: 'URB-1',
      fulfillmentType: FulfillmentType.PickUp,
      items: [{ productName: 'X-Burger', quantity: 1, unitPrice: 30, totalPrice: 30 }],
      subtotal: 30,
      deliveryFee: 0,
      total: 30,
      createdAtUtc: '',
    }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.openOrderDetails('1');
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('.order-details-dialog');
    expect(dialog.textContent).toContain('Subtotal');
    expect(dialog.textContent).toContain('Total');
    expect(dialog.textContent).not.toContain('Taxa de entrega');
  });

  it('keeps the delivery fee line for delivery orders', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-1', fulfillmentType: FulfillmentType.Delivery, subtotal: 70, deliveryFee: 5, total: 75 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(of({ id: '1', code: 'URB-1', fulfillmentType: FulfillmentType.Delivery, items: [], subtotal: 70, deliveryFee: 5, total: 75, createdAtUtc: '' }));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, '1');
    const text = card.textContent.replace(/\u00a0/g, ' ');
    expect(text).toContain('Taxa de entrega');
    expect(text).toContain('R$ 5,00');
  });

  it('shows a loading state while prefetching items for a card', () => {
    const prefetch = new Subject<any>();
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'prep-1', code: 'PREP-1', status: OrderStatus.Preparing, total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(prefetch.asObservable());

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'prep-1');
    expect(card.textContent).toContain('Carregando itens');

    prefetch.next({ id: 'prep-1', code: 'PREP-1', items: [{ productName: 'Suco', quantity: 1, unitPrice: 12, totalPrice: 12 }], subtotal: 40, deliveryFee: 0, total: 40, createdAtUtc: '' });
    prefetch.complete();
    fixture.detectChanges();

    expect(card.textContent).toContain('Suco');
    expect(card.textContent).not.toContain('Carregando itens');
  });

  it('clears the loading state and shows the retry state when prefetch fails', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: 'a', code: 'A-1', status: OrderStatus.Preparing, total: 10 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });
    orderServiceMock.getStoreOrder.mockReturnValue(throwError(() => new Error('boom')));

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const card = expandOrderCard(fixture, 'a');
    expect(card.textContent).not.toContain('Carregando itens');
    expect(card.querySelector('.items-retry')).not.toBeNull();
  });

  describe('detached orders window', () => {
    it('renders the split action in the top bar and opens the dedicated window from the click', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.orders-window-toggle') as HTMLButtonElement | null;
      expect(button).not.toBeNull();
      expect(button?.textContent).toContain('Separar tela pedidos');

      button?.click();

      expect(ordersWindowMock.openDetachedWindow).toHaveBeenCalledTimes(1);
      expect(ordersWindowMock.markDetachedRoute).not.toHaveBeenCalled();
    });

    it('reports a blocked popup and keeps the dashboard in normal mode', () => {
      ordersWindowMock.openDetachedWindow.mockReturnValue(false);

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      fixture.nativeElement.querySelector('.orders-window-toggle').click();

      expect(toastServiceMock.showError).toHaveBeenCalledWith(
        expect.stringContaining('janela separada'),
      );
      expect(ordersWindowMock.isDetached()).toBe(false);
    });

    it('registers the dedicated route and turns the action into Fixar ao Dashboard', () => {
      activatedRouteMock.snapshot.data = { detachedOrdersWindow: true };

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      expect(ordersWindowMock.markDetachedRoute).toHaveBeenCalledTimes(1);

      const button = fixture.nativeElement.querySelector('.orders-window-toggle') as HTMLButtonElement;
      expect(button.textContent).toContain('Fixar ao Dashboard');

      button.click();

      expect(ordersWindowMock.closeDetachedWindow).toHaveBeenCalledTimes(1);
      expect(ordersWindowMock.openDetachedWindow).not.toHaveBeenCalled();
    });

    it('keeps the live board rendered on the dedicated route', () => {
      activatedRouteMock.snapshot.data = { detachedOrdersWindow: true };
      orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
        if (status === OrderStatus.Received) {
          return of({ items: [buildOrder({ id: 'live-order', code: 'LIVE' })], totalItems: 1 });
        }
        if (status === OrderStatus.Preparing) {
          return of({ items: [buildOrder({ id: 'live-accepted', code: 'ACCEPTED', status: OrderStatus.Preparing })], totalItems: 1 });
        }
        return of({ items: [], totalItems: 0 });
      });

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      expect(ordersWindowMock.isDetachedHost()).toBe(false);
      const board = fixture.nativeElement.querySelector('.orders-board');
      expect(board.classList.contains('is-detached-host')).toBe(false);
      expect(fixture.nativeElement.querySelector('.order-card[data-order-id="live-order"]')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.status-col .order-card[data-order-id="live-accepted"]')).not.toBeNull();
    });

    it('grays out the host board without rendering cards and keeps the top controls accessible', () => {
      isWindowsPlatformMock.mockReturnValue(true);
      orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
        if (status === OrderStatus.Received) {
          return of({ items: [buildOrder({ id: 'new-order', code: 'NEW' })], totalItems: 1 });
        }
        return of({ items: [], totalItems: 0 });
      });

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.order-card[data-order-id="new-order"]')).not.toBeNull();

      ordersWindowMock.isDetached.set(true);
      fixture.detectChanges();

      const board = fixture.nativeElement.querySelector('.orders-board');
      expect(board).not.toBeNull();
      expect(board.classList.contains('is-detached-host')).toBe(true);
      expect(board.getAttribute('aria-disabled')).toBe('true');
      expect(board.textContent).toContain('Os pedidos estão abertos na janela separada.');
      expect(fixture.nativeElement.querySelector('.order-card')).toBeNull();

      expect(fixture.nativeElement.querySelector('.page-title').textContent).toContain('Pedidos do dia');
      const topButtons = Array.from(
        fixture.nativeElement.querySelectorAll<HTMLButtonElement>('.top-actions button'),
      ).map((button) => button.textContent?.trim() ?? '');
      expect(topButtons).toContain('Separar tela pedidos');
      expect(topButtons).toContain('Tela cheia');
      expect(topButtons).toContain('Atualizar');
    });

    it('skips new-order pulses while detached and reloads once after the host is restored', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      ordersWindowMock.isDetached.set(true);
      fixture.detectChanges();

      orderServiceMock.getStoreOrders.mockClear();
      shellMock.newOrderPulse.mockReturnValue({ id: 'detached-pulse' });
      fixture.componentInstance['effectScheduler']?.();
      fixture.detectChanges();

      expect(orderServiceMock.getStoreOrders).not.toHaveBeenCalled();

      ordersWindowMock.isDetached.set(false);
      fixture.detectChanges();

      expect(orderServiceMock.getStoreOrders).toHaveBeenCalled();
    });

    it('does not load orders when the host is opened already detached and loads once on restore', () => {
      ordersWindowMock.isDetached.set(true);

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      expect(orderServiceMock.getStoreOrders).not.toHaveBeenCalled();
      const board = fixture.nativeElement.querySelector('.orders-board');
      expect(board.classList.contains('is-detached-host')).toBe(true);

      ordersWindowMock.isDetached.set(false);
      fixture.detectChanges();

      expect(orderServiceMock.getStoreOrders).toHaveBeenCalled();
    });

    it('ignores realtime status events while detached and reconciles on restore', () => {
      orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
        if (status === OrderStatus.Preparing) {
          return of({ items: [buildOrder({ id: '2', code: 'DEF', status: OrderStatus.Preparing })], totalItems: 1 });
        }
        return of({ items: [], totalItems: 0 });
      });

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      const listener = signalRServiceMock.onSellerEvent.mock.calls.find(
        ([name]: [string]) => name === 'OrderStatusUpdated',
      )?.[1];
      expect(listener).toBeDefined();

      ordersWindowMock.isDetached.set(true);
      fixture.detectChanges();

      const before = orderServiceMock.getStoreOrders.mock.calls.length;
      listener({ orderId: '2', status: OrderStatus.Ready });
      expect(orderServiceMock.getStoreOrders.mock.calls.length).toBe(before);

      ordersWindowMock.isDetached.set(false);
      fixture.detectChanges();

      expect(orderServiceMock.getStoreOrders.mock.calls.length).toBeGreaterThan(before);
    });

    it('releases the detached route instead of disposing the service on destroy', () => {
      activatedRouteMock.snapshot.data = { detachedOrdersWindow: true };

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();
      expect(ordersWindowMock.markDetachedRoute).toHaveBeenCalledTimes(1);
      expect(ordersWindowMock.isDetachedRoute()).toBe(true);

      fixture.destroy();

      expect(ordersWindowMock.releaseDetachedRoute).toHaveBeenCalledTimes(1);
      expect(ordersWindowMock.dispose).not.toHaveBeenCalled();
      expect(ordersWindowMock.closeDetachedWindow).not.toHaveBeenCalled();
      expect(ordersWindowMock.isDetachedRoute()).toBe(false);
      expect(ordersWindowMock.isDetachedHost()).toBe(false);
    });

    it('does not release the detached route when the dashboard view is destroyed', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      fixture.destroy();

      expect(ordersWindowMock.releaseDetachedRoute).not.toHaveBeenCalled();
      expect(ordersWindowMock.dispose).not.toHaveBeenCalled();
    });
  });
});
