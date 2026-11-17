import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { EMPTY, Observable, catchError, expand, finalize, firstValueFrom, forkJoin, map, of, toArray } from 'rxjs';
import { DashboardPeriod, formatSaoPauloDateTime, formatSaoPauloTime, saoPauloPeriodRange, saoPauloTodayEndExclusive } from '../../core/utils/sao-paulo-date.helper';
import { isWindowsPlatform } from '../../core/utils/platform.helper';
import { OrderService } from '../../core/services/order.service';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerOrdersWindowService } from '../../core/services/seller-orders-window.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { SellerDeliveryDriver } from '../../shared/models/delivery-driver.model';
import { DeliveryRouteStatus } from '../../shared/models/delivery-route.model';
import {
  OrderDetails,
  OrderItem,
  OrderSummary,
  PagedOrderSummary,
} from '../../shared/models/order.model';
import { AutomaticPrintQueueService } from '../seller-printing/automatic-print-queue.service';
import { OrderAcceptedForPrintingEvent } from '../seller-printing/seller-printing.models';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';

interface PendingAction {
  order: OrderSummary;
  nextStatus: OrderStatus;
  label: string;
  deliveryDriverId?: string;
}

interface OrderStatusUpdateEvent {
  orderId?: string;
  orderCode?: string;
  status?: OrderStatus;
  changedAtUtc?: string;
  storeId?: string;
  sequence?: number;
  acceptedAtUtc?: string;
}

type BoardColumnTone = 'preparing' | 'ready' | 'on-delivery' | 'delivered';

interface BoardColumn {
  title: string;
  tone: BoardColumnTone;
  orders: OrderSummary[];
}

type BoardAction =
  | { kind: 'advance'; label: string; nextStatus: OrderStatus; css: string }
  | { kind: 'complete'; label: string; css: string };

/** Minimum gap between full-board reloads triggered by an unknown-order status event. */
const UNKNOWN_ORDER_RECONCILE_MIN_INTERVAL_MS = 5_000;

@Component({
  selector: 'app-seller-orders-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './seller-orders-page.component.html',
  styleUrl: './seller-orders-page.component.scss',
})
export class SellerOrdersPageComponent implements OnInit, OnDestroy {
  private readonly orderService = inject(OrderService);
  private readonly deliveryDriverService = inject(DeliveryDriverService);
  private readonly toast = inject(ToastService);
  private readonly automaticPrint = inject(AutomaticPrintQueueService);
  private readonly printing = inject(SellerPrintingService);
  private readonly shell = inject(SellerShellFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly signalR = inject(SignalRService);
  private readonly ordersWindow = inject(SellerOrdersWindowService);
  private lastPulseId: string | null = null;
  private wasDetachedHost = false;
  private loadSequence = 0;
  private detailsSequence = 0;
  private readonly detailsVersion = new Map<string, number>();
  private readonly retryVersion = new Map<string, number>();
  private readonly manualRequestPending = new Map<string, number>();
  private readonly prefetchSequence = new Map<string, number>();
  /** Last `changedAtUtc` applied per order, so duplicated/older events are dropped. */
  private readonly orderEventVersions = new Map<string, number>();
  /** Orders with a point detail refresh in flight, keyed by the event version it serves. */
  private readonly eventRefreshes = new Map<string, number>();
  private readonly pendingEventRefreshes = new Set<string>();
  private reconcilingUnknownOrder = false;
  private lastUnknownOrderReconcileAt = 0;
  /** Board ids seen by the previous load; `null` until the board is loaded for the first time. */
  private boardOrderIds: ReadonlySet<string> | null = null;
  private retrySequence = 0;
  private selectedOrderTrigger: HTMLElement | null = null;
  private readonly targetOrderId = this.route.snapshot.queryParamMap.get('order');
  private orderStatusListener?: (payload: OrderStatusUpdateEvent) => void;
  private acceptedForPrintingListener?: (payload: OrderAcceptedForPrintingEvent) => void;
  private realtimeWasConnected = false;
  private keydownHandler?: (event: KeyboardEvent) => void;
  private fullscreenChangeHandler?: () => void;

  readonly isWindows = isWindowsPlatform();
  readonly automaticPrintStatus = this.automaticPrint.statusMessage;
  readonly automaticPrintTone = this.automaticPrint.statusTone;
  readonly isFullscreen = signal(false);
  readonly isDetachedRoute = this.ordersWindow.isDetachedRoute;
  readonly isDetachedHost = this.ordersWindow.isDetachedHost;
  readonly OrderStatus = OrderStatus;
  readonly FulfillmentType = FulfillmentType;
  readonly DeliveryRouteStatus = DeliveryRouteStatus;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly updatingOrderId = signal<string | null>(null);
  readonly completingOrderId = signal<string | null>(null);
  readonly orders = signal<OrderSummary[]>([]);
  readonly pendingAction = signal<PendingAction | null>(null);
  readonly orderDetails = signal<Map<string, OrderDetails>>(new Map());
  readonly itemsErrorOrderIds = signal<ReadonlySet<string>>(new Set());
  readonly retryingOrderIds = signal<ReadonlySet<string>>(new Set());
  readonly selectedOrderId = signal<string | null>(null);
  readonly selectedOrderLoading = signal(false);
  readonly selectedOrderError = signal(false);
  readonly selectedPeriod = signal<DashboardPeriod>('today');
  readonly prefetchingOrderIds = signal<ReadonlySet<string>>(new Set());
  readonly expandedOrderIds = signal<ReadonlySet<string>>(new Set());
  readonly deliveryDrivers = signal<SellerDeliveryDriver[]>([]);
  readonly assignmentSelection = signal<string>('');
  readonly assignmentSaving = signal(false);
  /** Order whose manual print is in flight; blocks a second click on the same card. */
  readonly printingOrderId = signal<string | null>(null);

  readonly activePeriodTitle = computed(() => {
    switch (this.selectedPeriod()) {
      case 'week': return 'Pedidos da semana';
      case 'month': return 'Pedidos do mês';
      default: return 'Pedidos do dia';
    }
  });

  readonly selectedOrder = computed(() => {
    const orderId = this.selectedOrderId();
    if (!orderId) return null;
    const details = this.orderDetails().get(orderId);
    const summary = this.orders().find((o) => o.id === orderId);
    if (!details && !summary) return null;

    const composedAddress = details ? this.composeAddress(details) : '';

    return {
      id: details?.id ?? summary?.id ?? '',
      code: details?.code ?? summary?.code ?? '',
      customerName: details?.customerName ?? summary?.customerName,
      customerPhoneNumber: details?.customerPhoneNumber ?? summary?.customerPhoneNumber,
      fulfillmentType: details?.fulfillmentType ?? summary?.fulfillmentType,
      status: details?.status ?? summary?.status,
      createdAtUtc: details?.createdAtUtc ?? summary?.createdAtUtc,
      notes: details?.notes,
      address: composedAddress || summary?.addressSummary || '',
      paymentMethod: details?.paymentMethod ?? summary?.paymentMethod,
      items: details?.items ?? [],
      subtotal: details?.subtotal,
      deliveryFee: details?.deliveryFee,
      total: details?.total ?? summary?.total ?? 0,
      cashWithoutChange: details?.cashWithoutChange ?? summary?.cashWithoutChange,
      cashChangeForAmount: details?.cashChangeForAmount ?? summary?.cashChangeForAmount,
      cashReceivedAmount: details?.cashReceivedAmount ?? summary?.cashReceivedAmount,
      cashChangeAmount: details?.cashChangeAmount ?? summary?.cashChangeAmount,
      paymentConfirmedAtUtc: details?.paymentConfirmedAtUtc ?? summary?.paymentConfirmedAtUtc,
      deliveryDriverId: details?.deliveryDriverId ?? summary?.deliveryDriverId,
      deliveryDriverName: details?.deliveryDriverName ?? summary?.deliveryDriverName,
      deliveryStartedAtUtc: details?.deliveryStartedAtUtc ?? summary?.deliveryStartedAtUtc,
      deliveryPinConfirmedAtUtc: details?.deliveryPinConfirmedAtUtc ?? summary?.deliveryPinConfirmedAtUtc,
      deliveryConfirmedAtUtc: details?.deliveryConfirmedAtUtc ?? summary?.deliveryConfirmedAtUtc,
      deliveryRouteId: details?.deliveryRouteId ?? summary?.deliveryRouteId,
      deliveryRouteStatus: details?.deliveryRouteStatus ?? summary?.deliveryRouteStatus,
      sellerCompletedAtUtc: details?.sellerCompletedAtUtc ?? summary?.sellerCompletedAtUtc,
    };
  });

  readonly assignableDrivers = computed(() =>
    this.deliveryDrivers().filter((driver) => driver.isActive),
  );

  readonly pendingRequiresDriver = computed(() => {
    const pending = this.pendingAction();
    return !!pending
      && pending.nextStatus === OrderStatus.OnDelivery
      && pending.order.fulfillmentType === FulfillmentType.Delivery;
  });

  readonly canConfirmPending = computed(() =>
    !this.pendingRequiresDriver() || !!this.pendingAction()?.deliveryDriverId,
  );

  private readonly allStatuses = [
    OrderStatus.Received,
    OrderStatus.Preparing,
    OrderStatus.Ready,
    OrderStatus.OnDelivery,
    OrderStatus.Delivered,
  ];

  readonly newOrders = computed(() => this.statusGroups().received);

  readonly boardColumns = computed<BoardColumn[]>(() => [
    { title: 'Preparando', tone: 'preparing', orders: this.statusGroups().preparing },
    { title: 'Pronto', tone: 'ready', orders: this.statusGroups().ready },
    { title: 'Em entrega', tone: 'on-delivery', orders: this.statusGroups().onDelivery },
    { title: 'Concluído', tone: 'delivered', orders: this.statusGroups().delivered },
  ]);

  readonly visibleOrders = computed(() =>
    this.orders().filter((o) => !o.sellerCompletedAtUtc || this.isConcludedPickup(o)),
  );

  readonly statusGroups = computed(() => {
    const all = this.visibleOrders();
    return {
      received: this.sortOrders(all.filter((o) => o.status === OrderStatus.Received)),
      preparing: this.sortOrders(all.filter((o) => o.status === OrderStatus.Preparing)),
      ready: this.sortOrders(all.filter((o) => o.status === OrderStatus.Ready && !this.isConcludedPickup(o))),
      onDelivery: this.sortOrders(all.filter((o) => o.status === OrderStatus.OnDelivery)),
      // Pickup keeps OrderStatus.Ready after the handover, so a concluded pickup belongs to the
      // terminal column next to the delivered orders.
      delivered: this.sortOrders(all.filter((o) =>
        o.status === OrderStatus.Delivered || this.isConcludedPickup(o))),
    };
  });

  constructor() {
    effect(() => {
      const pulse = this.shell.newOrderPulse();
      if (!pulse || pulse.id === this.lastPulseId) return;
      this.lastPulseId = pulse.id;
      // Consume the pulse without loading while the detached window owns the
      // live board; the restore effect performs one authoritative reload.
      if (this.ordersWindow.isDetachedHost()) return;
      this.load({ silent: true });
    });

    effect(() => {
      const targetOrderId = this.targetOrderId;
      const orders = this.orders();

      if (!targetOrderId || !orders.some((order) => order.id === targetOrderId)) return;

      this.focusTargetOrder(targetOrderId);
    });

    effect(() => {
      if (!this.selectedOrderId()) return;
      this.focusOrderDetails();
    });

    effect(() => {
      const connected = this.signalR.isSellerConnected();
      if (connected && !this.realtimeWasConnected && !this.ordersWindow.isDetachedHost()) {
        // Reconcile after a reconnect: events are at-least-once and may have been missed while
        // offline. The print queue deduplicates by the acceptance identity.
        this.load({ silent: true });
      }
      this.realtimeWasConnected = connected;
    });

    effect(() => {
      // Fixing/closing the secondary window restores the host board with one
      // authoritative load, recovering orders received while detached.
      const detachedHost = this.ordersWindow.isDetachedHost();
      if (this.wasDetachedHost && !detachedHost && !this.ordersWindow.isDetachedRoute()) {
        this.load({ silent: true });
      }
      this.wasDetachedHost = detachedHost;
    });
  }

  ngOnInit(): void {
    if (this.route.snapshot.data?.['detachedOrdersWindow'] === true) {
      this.ordersWindow.markDetachedRoute();
    }
    // A dashboard opened while the detached window owns the board does not
    // load orders until it is restored; the restore effect loads them then.
    if (!this.ordersWindow.isDetachedHost()) {
      this.load();
    }
    this.registerOrderStatusListener();
    this.registerFullscreenListeners();
    this.loadDeliveryDrivers();
  }

  ngOnDestroy(): void {
    if (this.orderStatusListener) {
      this.signalR.removeSellerListener('OrderStatusUpdated', this.orderStatusListener);
      this.orderStatusListener = undefined;
    }
    if (this.acceptedForPrintingListener) {
      this.signalR.removeSellerListener('OrderAcceptedForPrinting', this.acceptedForPrintingListener);
      this.acceptedForPrintingListener = undefined;
    }
    if (this.keydownHandler) {
      document.removeEventListener('keydown', this.keydownHandler);
      this.keydownHandler = undefined;
    }
    if (this.fullscreenChangeHandler) {
      document.removeEventListener('fullscreenchange', this.fullscreenChangeHandler);
      this.fullscreenChangeHandler = undefined;
    }
    if (this.ordersWindow.isDetachedRoute()) {
      // A route change destroys this view without the seller clicking
      // "Fixar ao Dashboard": tell the host to restore instead of leaving its
      // board grayed out. The shared service instance and the window survive.
      this.ordersWindow.releaseDetachedRoute();
    }
  }

  toggleOrdersWindow(): void {
    if (this.ordersWindow.isDetachedRoute()) {
      this.ordersWindow.closeDetachedWindow();
      return;
    }

    const opened = this.ordersWindow.openDetachedWindow();
    if (!opened) {
      void this.toast.showError('Não foi possível abrir a janela separada. Verifique o bloqueador de pop-ups.');
    }
  }

  async toggleFullscreen(): Promise<void> {
    if (!this.isWindows) return;

    try {
      if (document.fullscreenElement) {
        await document.exitFullscreen();
      } else if (document.documentElement.requestFullscreen) {
        await document.documentElement.requestFullscreen();
      }
    } catch {
      this.isFullscreen.set(!!document.fullscreenElement);
      return;
    }

    this.isFullscreen.set(!!document.fullscreenElement);
  }

  private registerFullscreenListeners(): void {
    if (!this.isWindows) return;

    this.keydownHandler = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && this.isFullscreen()) {
        void this.toggleFullscreen();
      }
    };
    this.fullscreenChangeHandler = () => {
      this.isFullscreen.set(!!document.fullscreenElement);
    };

    document.addEventListener('keydown', this.keydownHandler);
    document.addEventListener('fullscreenchange', this.fullscreenChangeHandler);
  }

  load(options?: { silent?: boolean }): void {
    if (!options?.silent) this.loading.set(true);
    if (!options?.silent) this.error.set(false);
    const sequence = ++this.loadSequence;
    const range = saoPauloPeriodRange(this.selectedPeriod());
    // The day board ends at the São Paulo civil-day boundary computed from the calendar, never at
    // the browser's current instant: a brand-new order must be visible on the realtime reload even
    // when the device clock is slightly behind the server that stamped CreatedAtUtc.
    const endExclusiveUtc = this.selectedPeriod() === 'today'
      ? saoPauloTodayEndExclusive()
      : undefined;
    const endDateUtc = endExclusiveUtc ? undefined : range.endDateUtc;

    forkJoin(this.allStatuses.map((status) =>
      this.loadStatusOrders(status, range.startDateUtc, endDateUtc, endExclusiveUtc).pipe(
        map((pages) => ({ status, pages })),
        // One column failing must not discard the whole board: the remaining statuses still
        // refresh, and the failed column keeps its previous contents. The failure stays visible
        // (console diagnostic + the global error toast from the interceptor) instead of aborting
        // the realtime reload silently.
        catchError((error: unknown) => {
          console.warn(`SellerOrders: could not refresh status ${OrderStatus[status]}.`, error);
          return of({ status, pages: null as PagedOrderSummary[] | null });
        }),
      ),
    )).pipe(
      // A request that completes without emitting (or whose payload cannot be processed)
      // must still release the board spinner. Only the newest request owns the flag, so a
      // superseded silent refresh cannot leave the initial loading state stuck.
      finalize(() => {
        if (sequence !== this.loadSequence) return;
        this.reconcilingUnknownOrder = false;
        this.loading.set(false);
      }),
    ).subscribe({
      next: (statusResults) => {
        if (sequence !== this.loadSequence) return;
        try {
          const previous = this.orders();
          const items = this.sortOrders(
            statusResults.flatMap(({ status, pages }) => pages
              ? pages.flatMap((page) => page.items)
              : previous.filter((order) => order.status === status)),
          );
          const everyStatusFailed = statusResults.every(({ pages }) => pages === null);
          if (everyStatusFailed && previous.length === 0) {
            if (!options?.silent) this.error.set(true);
            return;
          }

          this.orders.set(items);
          this.expandNewlyReceivedOrders(items);
          this.syncDetailsFromSummaries(items);
          this.loadOrderItems(items, sequence);
          void this.automaticPrint.reconcilePendingJobs();
        } catch {
          if (!options?.silent) this.error.set(true);
        }
      },
      error: () => {
        if (sequence !== this.loadSequence) return;
        if (!options?.silent) this.error.set(true);
      },
    });
  }

  /**
   * Loads every page of the given status. OnDelivery is fully paginated because the batch send
   * must never silently omit deliveries beyond the first page; the other columns keep the
   * existing single-page behavior to avoid pulling the whole history on every refresh.
   */
  private loadStatusOrders(
    status: OrderStatus,
    startDateUtc: string | undefined,
    endDateUtc: string | undefined,
    endExclusiveUtc: string | undefined,
  ): Observable<PagedOrderSummary[]> {
    const request = (page: number) =>
      this.orderService.getStoreOrders({
        page,
        pageSize: 50,
        status,
        startDateUtc,
        endDateUtc,
        endExclusiveUtc,
      });

    const firstPage = request(1);
    if (status !== OrderStatus.OnDelivery) {
      return firstPage.pipe(map((result) => [result]));
    }

    // Track the requested page locally: a malformed/echoed response can never make `expand`
    // request the same page forever, and `totalPages` bounds the loop.
    let requestedPage = 1;
    return firstPage.pipe(
      expand((result) => {
        const pageSize = result.pageSize > 0 ? result.pageSize : 50;
        const totalPages = result.totalPages
          ?? Math.ceil((result.totalItems ?? 0) / pageSize);
        if (!Number.isFinite(totalPages) || totalPages < 1) return EMPTY;
        if (requestedPage >= totalPages) return EMPTY;
        requestedPage += 1;
        return request(requestedPage);
      }),
      toArray(),
    );
  }

  private loadOrderItems(orders: OrderSummary[], sequence: number): void {
    const selectedOrderId = this.selectedOrderId();
    const prefetchTargets = orders.filter(
      (o) => o.id !== selectedOrderId && !this.manualRequestPending.has(o.id),
    );
    for (const order of prefetchTargets) {
      this.prefetchOrderItems(order.id, sequence);
    }
  }

  private prefetchOrderItems(orderId: string, sequence: number): void {
    const version = this.detailsVersion.get(orderId) ?? 0;
    this.prefetchSequence.set(orderId, sequence);
    this.prefetchingOrderIds.update((ids) => {
      const next = new Set(ids);
      next.add(orderId);
      return next;
    });

    this.orderService.getStoreOrder(orderId).pipe(
      catchError(() => {
        if (sequence === this.loadSequence && !this.orderDetails().has(orderId)) {
          this.itemsErrorOrderIds.update((ids) => {
            const next = new Set(ids);
            next.add(orderId);
            return next;
          });
        }
        return of(null);
      }),
    ).subscribe((detail) => {
      if (this.prefetchSequence.get(orderId) === sequence) {
        this.prefetchSequence.delete(orderId);
        this.prefetchingOrderIds.update((ids) => {
          if (!ids.has(orderId)) return ids;
          const next = new Set(ids);
          next.delete(orderId);
          return next;
        });
      }
      if (detail === null) return;
      if (sequence !== this.loadSequence) return;
      const currentSelected = this.selectedOrderId();
      if (detail.id === currentSelected) return;
      if (this.manualRequestPending.has(detail.id)) return;
      if ((this.detailsVersion.get(detail.id) ?? 0) !== version) return;
      const details = new Map(this.orderDetails());
      details.set(detail.id, detail);
      this.orderDetails.set(details);
      this.itemsErrorOrderIds.update((ids) => {
        if (!ids.has(detail.id)) return ids;
        const next = new Set(ids);
        next.delete(detail.id);
        return next;
      });
    });
  }

  hasItemsError(orderId: string): boolean {
    return this.itemsErrorOrderIds().has(orderId);
  }

  retryItems(orderId: string): void {
    const version = (this.retryVersion.get(orderId) ?? 0) + 1;
    this.retryVersion.set(orderId, version);

    this.detailsVersion.set(orderId, (this.detailsVersion.get(orderId) ?? 0) + 1);
    const manualSequence = ++this.retrySequence;
    this.manualRequestPending.set(orderId, manualSequence);

    this.retryingOrderIds.update((ids) => {
      const next = new Set(ids);
      next.add(orderId);
      return next;
    });
    this.itemsErrorOrderIds.update((ids) => {
      if (!ids.has(orderId)) return ids;
      const next = new Set(ids);
      next.delete(orderId);
      return next;
    });

    this.orderService.getStoreOrder(orderId).pipe(
      catchError(() => of(null)),
    ).subscribe((detail) => {
      if ((this.retryVersion.get(orderId) ?? 0) !== version) return;

      if (this.manualRequestPending.get(orderId) === manualSequence) {
        this.manualRequestPending.delete(orderId);
      }

      this.retryingOrderIds.update((ids) => {
        const next = new Set(ids);
        next.delete(orderId);
        return next;
      });

      if (detail === null) {
        this.itemsErrorOrderIds.update((ids) => {
          const next = new Set(ids);
          next.add(orderId);
          return next;
        });
        return;
      }

      const details = new Map(this.orderDetails());
      details.set(orderId, detail);
      this.orderDetails.set(details);
      this.itemsErrorOrderIds.update((ids) => {
        if (!ids.has(orderId)) return ids;
        const next = new Set(ids);
        next.delete(orderId);
        return next;
      });
    });
  }

  isRetrying(orderId: string): boolean {
    return this.retryingOrderIds().has(orderId);
  }

  isPrefetchingItems(orderId: string): boolean {
    return this.prefetchingOrderIds().has(orderId);
  }

  isOrderExpanded(orderId: string): boolean {
    return this.expandedOrderIds().has(orderId);
  }

  /**
   * A brand-new order opens on arrival so the seller sees its details immediately. The board
   * loaded when the dashboard was opened is the baseline and keeps the compact collapsed
   * layout; only orders in `Received` that were not on a previous load count as arrivals, and
   * orders already on the board keep whatever state the seller left them in.
   */
  private expandNewlyReceivedOrders(orders: OrderSummary[]): void {
    const previousIds = this.boardOrderIds;
    this.boardOrderIds = new Set(orders.map((order) => order.id));
    if (!previousIds) return;

    const arrivals = orders.filter(
      (order) => order.status === OrderStatus.Received && !previousIds.has(order.id),
    );
    if (arrivals.length === 0) return;

    this.expandedOrderIds.update((ids) => {
      const next = new Set(ids);
      for (const order of arrivals) next.add(order.id);
      return next;
    });
  }

  toggleOrderExpanded(orderId: string): void {
    this.expandedOrderIds.update((ids) => {
      const next = new Set(ids);
      if (next.has(orderId)) {
        next.delete(orderId);
      } else {
        next.add(orderId);
      }
      return next;
    });
  }

  orderBodyId(orderId: string): string {
    return `order-body-${orderId}`;
  }

  private syncDetailsFromSummaries(summaries: OrderSummary[]): void {
    const current = this.orderDetails();
    if (current.size === 0) return;

    const updated = new Map(current);
    let changed = false;
    for (const summary of summaries) {
      const detail = current.get(summary.id);
      if (detail && detail.status !== summary.status) {
        updated.set(summary.id, { ...detail, status: summary.status });
        changed = true;
      }
    }
    if (changed) this.orderDetails.set(updated);
  }

  private patchDetailStatus(orderId: string, status: OrderStatus): void {
    this.orderDetails.update((details) => {
      const detail = details.get(orderId);
      if (!detail || detail.status === status) return details;
      const updated = new Map(details);
      updated.set(orderId, { ...detail, status });
      return updated;
    });
  }

  orderItems(orderId: string): OrderItem[] {
    return this.orderDetails().get(orderId)?.items ?? [];
  }

  displayOrderCode(order: Pick<OrderSummary, 'code'>): string {
    return order.code;
  }

  itemOptionEntries(item: OrderItem): { name: string; price?: number }[] {
    const prices = item.optionPrices;
    if (prices && prices.length > 0) {
      return prices.map((option) => ({ name: option.name, price: option.price }));
    }

    const entries: { name: string; price?: number }[] = [];
    if (item.choiceOptionName) entries.push({ name: item.choiceOptionName });
    if (item.additionalNames) entries.push({ name: item.additionalNames });
    return entries;
  }

  openOrderDetails(orderId: string, event?: Event): void {
    this.selectedOrderId.set(orderId);
    this.selectedOrderError.set(false);
    const summary = this.orders().find((order) => order.id === orderId);
    this.assignmentSelection.set(
      summary?.deliveryDriverId ?? this.orderDetails().get(orderId)?.deliveryDriverId ?? '',
    );
    const trigger = event?.currentTarget;
    if (trigger instanceof HTMLElement) {
      this.selectedOrderTrigger = trigger;
    }

    if (this.orderDetails().has(orderId)) {
      this.selectedOrderLoading.set(false);
      return;
    }

    const sequence = ++this.detailsSequence;
    this.detailsVersion.set(orderId, (this.detailsVersion.get(orderId) ?? 0) + 1);
    this.manualRequestPending.set(orderId, sequence);
    this.selectedOrderLoading.set(true);

    this.orderService.getStoreOrder(orderId).subscribe({
      next: (detail) => {
        if (this.manualRequestPending.get(orderId) === sequence) {
          this.manualRequestPending.delete(orderId);
        }
        if (sequence !== this.detailsSequence) return;
        const details = new Map(this.orderDetails());
        details.set(orderId, detail);
        this.orderDetails.set(details);
        this.itemsErrorOrderIds.update((ids) => {
          if (!ids.has(orderId)) return ids;
          const next = new Set(ids);
          next.delete(orderId);
          return next;
        });
        if (this.selectedOrderId() === orderId) {
          this.selectedOrderLoading.set(false);
          this.selectedOrderError.set(false);
        }
      },
      error: () => {
        if (this.manualRequestPending.get(orderId) === sequence) {
          this.manualRequestPending.delete(orderId);
        }
        if (sequence !== this.detailsSequence) return;
        if (this.selectedOrderId() === orderId) {
          this.selectedOrderLoading.set(false);
          this.selectedOrderError.set(true);
        }
      },
    });
  }

  retryOrderDetails(): void {
    const orderId = this.selectedOrderId();
    if (!orderId) return;
    this.selectedOrderError.set(false);
    this.openOrderDetails(orderId);
  }

  closeOrderDetails(): void {
    this.detailsSequence += 1;
    this.selectedOrderId.set(null);
    this.selectedOrderLoading.set(false);
    this.selectedOrderError.set(false);
    this.restoreFocusToTrigger();
  }

  private restoreFocusToTrigger(): void {
    const trigger = this.selectedOrderTrigger;
    this.selectedOrderTrigger = null;
    if (!trigger) return;
    setTimeout(() => trigger.focus());
  }

  isTargetOrder(orderId: string): boolean {
    return this.targetOrderId === orderId;
  }

  confirmAdvance(order: OrderSummary, nextStatus: OrderStatus, label: string): void {
    if (this.updatingOrderId()) return;

    const requiresDriver = nextStatus === OrderStatus.OnDelivery
      && order.fulfillmentType === FulfillmentType.Delivery;
    const existingDriver = requiresDriver
      ? this.assignableDrivers().find((driver) => driver.deliveryDriverId === order.deliveryDriverId)
      : undefined;

    this.pendingAction.set({
      order,
      nextStatus,
      label,
      deliveryDriverId: existingDriver?.id,
    });
  }

  setPendingDriver(driverId: string): void {
    const pending = this.pendingAction();
    if (!pending) return;
    this.pendingAction.set({ ...pending, deliveryDriverId: driverId || undefined });
  }

  dismissConfirm(): void {
    this.pendingAction.set(null);
  }

  executeAdvance(): void {
    const pending = this.pendingAction();
    if (!pending || this.updatingOrderId()) return;

    const requiresDriver = pending.nextStatus === OrderStatus.OnDelivery
      && pending.order.fulfillmentType === FulfillmentType.Delivery;
    if (requiresDriver && !pending.deliveryDriverId) {
      void this.toast.showError('Selecione um motoboy ativo para iniciar a entrega.');
      return;
    }

    const orderId = pending.order.id;
    const driverId = requiresDriver ? pending.deliveryDriverId : undefined;
    this.updatingOrderId.set(orderId);
    this.pendingAction.set(null);

    this.orderService.updateStoreOrderStatus(
      orderId,
      pending.nextStatus,
      'Atualizado pelo painel do lojista',
      driverId,
    ).subscribe({
      next: (updated) => {
        this.updatingOrderId.set(null);
        this.orders.update((items) =>
          items.map((order) => (order.id === orderId ? {
            ...order,
            status: updated.status ?? pending.nextStatus,
            deliveryDriverId: updated.deliveryDriverId ?? order.deliveryDriverId,
            deliveryDriverName: updated.deliveryDriverName ?? order.deliveryDriverName,
            deliveryStartedAtUtc: updated.deliveryStartedAtUtc ?? order.deliveryStartedAtUtc,
            deliveryRouteId: updated.deliveryRouteId ?? order.deliveryRouteId,
            deliveryRouteStatus: updated.deliveryRouteStatus ?? order.deliveryRouteStatus,
          } : order)),
        );
        this.patchDetailStatus(orderId, pending.nextStatus);
        this.patchOrderFromDetails(updated);
        void this.toast.showSuccess('Pedido atualizado.');
        if (pending.order.status === OrderStatus.Received && pending.nextStatus === OrderStatus.Preparing) {
          this.triggerAcceptedOrderPrint(orderId);
        }
        this.shell.notifyOrderChanged(orderId);
        this.load({ silent: true });
      },
      error: (err) => {
        this.updatingOrderId.set(null);
        void this.toast.showError(err?.error?.error ?? 'Não foi possível atualizar o pedido.');
      },
    });
  }

  completeOrder(order: OrderSummary): void {
    if (this.completingOrderId()) return;
    const orderId = order.id;
    this.completingOrderId.set(orderId);

    this.orderService.completeOrder(orderId).subscribe({
      next: (completed) => {
        this.completingOrderId.set(null);
        if (this.isPickup(order)) {
          // A pickup never changes status (it stays Ready), so the seller confirmation is what
          // concludes it. The row is patched in place and moves to the Concluído column instead
          // of disappearing from the day board. The server timestamp is authoritative; the local
          // one only covers a response without the field and is reconciled on the next load.
          const sellerCompletedAtUtc = completed?.sellerCompletedAtUtc ?? new Date().toISOString();
          this.orders.update((items) => items.map((item) =>
            item.id === orderId ? { ...item, sellerCompletedAtUtc } : item,
          ));
        } else {
          this.orders.update((items) => items.filter((o) => o.id !== orderId));
        }
        void this.toast.showSuccess('Pedido concluído.');
      },
      error: () => {
        this.completingOrderId.set(null);
        void this.toast.showError('Não foi possível concluir o pedido.');
      },
    });
  }

  canComplete(order: OrderSummary): boolean {
    return (order.status === OrderStatus.Delivered
       || (this.isPickup(order) && order.status === OrderStatus.Ready))
      && !order.sellerCompletedAtUtc
      && (!!order.deliveryConfirmedAtUtc || this.isPickup(order));
  }

  /**
   * Manual print is available once a motoboy is assigned to a delivery order. It is independent
   * from the automatic acceptance print (`autoPrint`) and can be repeated.
   */
  canPrintOrder(order: OrderSummary): boolean {
    return order.fulfillmentType === FulfillmentType.Delivery
      && !!order.deliveryDriverId
      && !order.sellerCompletedAtUtc;
  }

  /**
   * Prints the order on the configured printer, repeating the operation whenever the seller asks.
   * The details are prefetched with the board; a missing snapshot is fetched on demand. Errors are
   * reported by toast and never marked as a printed acceptance.
   */
  async printOrder(order: OrderSummary): Promise<void> {
    if (this.printingOrderId()) return;
    const config = this.printing.config();
    // The browser fallback opens the system dialog and needs no configured printer name.
    if (config.connectionType !== 'browser-print' && !config.printerName.trim()) {
      void this.toast.showError('Configure a impressora da loja em Impressão antes de imprimir.');
      return;
    }

    this.printingOrderId.set(order.id);
    try {
      const details = this.orderDetails().get(order.id)
        ?? await firstValueFrom(this.orderService.getStoreOrder(order.id));
      // A fresh key per click keeps the print repeatable: the agent deduplicates by operation key
      // and would drop a manual reprint that reused the acceptance key.
      const operationKey = `manual:${order.id}:${Date.now()}`;
      const result = await this.printing.printOrderFromDetails(details, operationKey, {
        deliveryDriverName: order.deliveryDriverName,
      });

      if (result.ok) {
        void this.toast.showSuccess(`Pedido #${order.code} enviado para ${this.printing.config().printerName}.`);
      } else {
        void this.toast.showError(result.message || 'A impressora não confirmou a impressão.');
      }
    } catch {
      void this.toast.showError('Não foi possível imprimir o pedido.');
    } finally {
      this.printingOrderId.set(null);
    }
  }

  canAssignDeliveryDriver(order: {
    fulfillmentType?: FulfillmentType;
    status?: OrderStatus;
    deliveryStartedAtUtc?: string;
    sellerCompletedAtUtc?: string;
  }): boolean {
    return order.fulfillmentType === FulfillmentType.Delivery
      && (order.status === OrderStatus.Ready || order.status === OrderStatus.OnDelivery)
      && !order.deliveryStartedAtUtc
      && !order.sellerCompletedAtUtc;
  }

  assignDeliveryDriver(orderId: string): void {
    const driverId = this.assignmentSelection();
    if (!driverId || this.assignmentSaving()) return;

    this.assignmentSaving.set(true);
    this.deliveryDriverService.assignOrder(orderId, driverId).subscribe({
      next: (order) => {
        this.assignmentSaving.set(false);
        this.patchOrderFromDetails(order);
        void this.toast.showSuccess('Entregador atribuído.');
      },
      error: (err) => {
        this.assignmentSaving.set(false);
        void this.toast.showError(err?.error?.error ?? 'Não foi possível atribuir o entregador.');
      },
    });
  }

  removeDeliveryDriver(orderId: string): void {
    if (this.assignmentSaving()) return;

    this.assignmentSaving.set(true);
    this.deliveryDriverService.removeAssignment(orderId).subscribe({
      next: (order) => {
        this.assignmentSaving.set(false);
        this.assignmentSelection.set('');
        this.patchOrderFromDetails(order);
        void this.toast.showSuccess('Atribuição removida.');
      },
      error: (err) => {
        this.assignmentSaving.set(false);
        void this.toast.showError(err?.error?.error ?? 'Não foi possível remover a atribuição.');
      },
    });
  }

  routeStatusLabel(status?: number | null): string {
    switch (status) {
      case DeliveryRouteStatus.Active: return 'Rota ativa';
      case DeliveryRouteStatus.Completed: return 'Rota concluída';
      case DeliveryRouteStatus.Revoked: return 'Rota revogada';
      case DeliveryRouteStatus.Expired: return 'Rota expirada';
      default: return 'Sem rota';
    }
  }

  private loadDeliveryDrivers(): void {
    this.deliveryDriverService.listStoreDrivers().subscribe({
      next: (drivers) => this.deliveryDrivers.set(drivers),
      error: () => this.deliveryDrivers.set([]),
    });
  }

  private patchOrderFromDetails(order: OrderDetails): void {
    if (!order?.id) return;

    this.orders.update((items) =>
      items.map((item) => (item.id === order.id ? {
        ...item,
        status: order.status,
        deliveryDriverId: order.deliveryDriverId,
        deliveryDriverName: order.deliveryDriverName,
        deliveryStartedAtUtc: order.deliveryStartedAtUtc,
        deliveryPinConfirmedAtUtc: order.deliveryPinConfirmedAtUtc,
        deliveryRouteId: order.deliveryRouteId,
        deliveryRouteStatus: order.deliveryRouteStatus,
        deliveryConfirmedAtUtc: order.deliveryConfirmedAtUtc,
        sellerCompletedAtUtc: order.sellerCompletedAtUtc,
      } : item)),
    );
    this.orderDetails.update((details) => {
      const updated = new Map(details);
      updated.set(order.id, order);
      return updated;
    });
  }

  firstName(customerName?: string): string {
    if (!customerName) return '';
    return customerName.trim().split(/\s+/)[0] ?? '';
  }

  paymentLabel(method?: PaymentMethod): string {
    switch (method) {
      case PaymentMethod.PixOnline: return 'Pix já pago';
      case PaymentMethod.CardOnline: return 'Cartão online';
      case PaymentMethod.CashOnDelivery: return 'Dinheiro ao receber';
      case PaymentMethod.CardOnDelivery: return 'Cartão ao receber';
      case PaymentMethod.PixOnDelivery: return 'Pix na maquininha';
      case PaymentMethod.DebitOnDelivery: return 'Débito na maquininha';
      case PaymentMethod.CreditOnDelivery: return 'Crédito na maquininha';
      default: return '-';
    }
  }

  formatCurrency(value?: number | null): string {
    if (value == null || Number.isNaN(value)) return 'R$ 0,00';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  cashDetail(order: { paymentMethod?: PaymentMethod; cashWithoutChange?: boolean; cashChangeForAmount?: number; cashReceivedAmount?: number; cashChangeAmount?: number }): string {
    if (order.paymentMethod !== PaymentMethod.CashOnDelivery) return '';
    if (order.cashReceivedAmount != null) {
      return `Recebido ${this.formatCurrency(order.cashReceivedAmount)} · Troco ${this.formatCurrency(order.cashChangeAmount ?? 0)}`;
    }
    if (order.cashWithoutChange === true) return 'Sem troco';
    if (order.cashChangeForAmount != null) return `Troco para ${this.formatCurrency(order.cashChangeForAmount)}`;
    return '';
  }

  paymentConfirmationLabel(order: { paymentConfirmedAtUtc?: string }): string {
    return order.paymentConfirmedAtUtc
      ? `Confirmado em ${formatSaoPauloDateTime(order.paymentConfirmedAtUtc)}`
      : '';
  }

  statusLabel(status?: OrderStatus, fulfillmentType?: FulfillmentType, sellerCompletedAtUtc?: string | null): string {
    switch (status) {
      case OrderStatus.Created: return 'Criado';
      case OrderStatus.PendingPayment: return 'Aguardando pagamento';
      case OrderStatus.Received: return 'Recebido';
      case OrderStatus.Preparing: return 'Preparando';
      case OrderStatus.Ready:
        // Pickup keeps the technical Ready status after the handover; the seller confirmation is
        // the concluded marker, so only the copy changes to "Retirado".
        if (fulfillmentType === FulfillmentType.PickUp) {
          return sellerCompletedAtUtc ? 'Retirado' : 'Pronto para retirada';
        }
        return 'Pronto';
      case OrderStatus.OnDelivery: return 'Saiu para entrega';
      case OrderStatus.Delivered: return 'Entregue';
      case OrderStatus.Cancelled: return 'Cancelado';
      default: return '';
    }
  }

  fulfillmentLabel(type?: FulfillmentType): string {
    if (type === undefined || type === null) return '';
    return type === FulfillmentType.PickUp ? 'Retirada pessoalmente' : 'Entrega';
  }

  isPickup(order: OrderSummary): boolean {
    return order.fulfillmentType === FulfillmentType.PickUp;
  }

  isPickupType(type?: FulfillmentType): boolean {
    return type === FulfillmentType.PickUp;
  }

  /**
   * Pickup stays in `OrderStatus.Ready` for its whole lifetime (the state machine never enters
   * the delivery states), so the seller confirmation is the concluded marker on the day board.
   */
  isConcludedPickup(order: OrderSummary): boolean {
    return order.fulfillmentType === FulfillmentType.PickUp && !!order.sellerCompletedAtUtc;
  }

  cardAction(order: OrderSummary): BoardAction | null {
    switch (order.status) {
      case OrderStatus.Received:
        return { kind: 'advance', label: 'Aceitar pedido', nextStatus: OrderStatus.Preparing, css: 'action-orange' };
      case OrderStatus.Preparing:
        return { kind: 'advance', label: 'Marcar como pronto', nextStatus: OrderStatus.Ready, css: 'action-orange' };
      case OrderStatus.Ready:
        return this.isPickup(order)
          ? (this.canComplete(order)
            ? { kind: 'complete', label: 'Concluir retirada', css: 'action-green' }
            : null)
          : { kind: 'advance', label: 'Saiu para entrega', nextStatus: OrderStatus.OnDelivery, css: 'action-blue' };
      case OrderStatus.OnDelivery:
        return { kind: 'advance', label: 'Marcar como entregue', nextStatus: OrderStatus.Delivered, css: 'action-purple' };
      case OrderStatus.Delivered:
        return this.canComplete(order)
          ? { kind: 'complete', label: this.isPickup(order) ? 'Concluir retirada' : 'Concluído', css: 'action-green' }
          : null;
      default:
        return null;
    }
  }

  formatOrderTime(value?: string): string {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return formatSaoPauloTime(value);
  }

  formatOrderDate(value?: string): string {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return formatSaoPauloDateTime(value);
  }

  onDialogKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const dialog = event.currentTarget as HTMLElement | null;
    if (!dialog) return;

    const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    )).filter((el) => !el.hasAttribute('disabled'));

    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;
    const index = active ? focusable.indexOf(active) : -1;

    if (event.shiftKey) {
      if (index <= 0) {
        event.preventDefault();
        last.focus();
      }
    } else {
      if (index === -1 || index === focusable.length - 1) {
        event.preventDefault();
        first.focus();
      }
    }
  }

  private composeAddress(details: OrderDetails): string {
    const street = [details.addressStreet, details.addressNumber].filter(Boolean).join(' ');
    const cityState = [details.addressCity, details.addressState].filter(Boolean).join(' - ');
    return [
      details.addressCep ? `CEP ${details.addressCep}` : '',
      street,
      details.addressNeighborhood,
      cityState,
      details.addressComplement,
      details.addressReference,
    ].filter(Boolean).join(', ');
  }

  private registerOrderStatusListener(): void {
    this.orderStatusListener = (payload: OrderStatusUpdateEvent) => {
      if (!payload?.orderId) return;
      // The detached window owns the live board; the host reloads once on
      // restore instead of processing status events against a hidden board.
      if (this.ordersWindow.isDetachedHost()) return;
      this.handleOrderStatusEvent(payload);
    };
    this.signalR.onSellerEvent('OrderStatusUpdated', this.orderStatusListener);

    this.acceptedForPrintingListener = (payload: OrderAcceptedForPrintingEvent) => {
      if (!payload?.orderId) return;
      void this.automaticPrint.enqueueAcceptedOrder(payload);
    };
    this.signalR.onSellerEvent('OrderAcceptedForPrinting', this.acceptedForPrintingListener);
  }

  /**
   * A status notification only carries the new status, so the affected row is patched in
   * place and a single detail read fills the fields the payload does not carry. The full
   * board is only reloaded when the order is not on it, because no safe patch exists then.
   */
  private handleOrderStatusEvent(payload: OrderStatusUpdateEvent): void {
    const orderId = payload.orderId;
    if (!orderId) return;

    const order = this.orders().find((item) => item.id === orderId);
    if (!order) {
      this.reconcileUnknownOrder();
      return;
    }

    const version = this.utcTimestamp(payload.changedAtUtc);
    const lastVersion = this.orderEventVersions.get(orderId) ?? 0;
    // Duplicated or out-of-order notifications never regress the board and never cost a read.
    if (version > 0 && version <= lastVersion) return;
    if (version > 0) this.orderEventVersions.set(orderId, version);

    const nextStatus = payload.status;
    if (nextStatus !== undefined && nextStatus !== order.status) {
      this.orders.update((items) => items.map((item) =>
        item.id === orderId ? { ...item, status: nextStatus } : item,
      ));
      this.patchDetailStatus(orderId, nextStatus);
    }

    // The payload omits driver, route and confirmation timestamps; one point read keeps
    // the card actions correct without reloading every column.
    this.refreshOrderFromEvent(orderId);
  }

  /** Coalesces a missed event for an order that is not on the board into one silent reload. */
  private reconcileUnknownOrder(): void {
    if (this.loading() || this.reconcilingUnknownOrder) return;
    const now = Date.now();
    if (now - this.lastUnknownOrderReconcileAt < UNKNOWN_ORDER_RECONCILE_MIN_INTERVAL_MS) return;
    // A batch of events for orders outside the board must not turn into a request storm.
    this.lastUnknownOrderReconcileAt = now;
    this.reconcilingUnknownOrder = true;
    this.load({ silent: true });
  }

  private refreshOrderFromEvent(orderId: string): void {
    if (this.eventRefreshes.has(orderId)) {
      // The in-flight read may predate this event; queue exactly one follow-up read.
      this.pendingEventRefreshes.add(orderId);
      return;
    }

    const version = this.orderEventVersions.get(orderId) ?? 0;
    this.eventRefreshes.set(orderId, version);
    this.orderService.getStoreOrder(orderId).pipe(
      catchError(() => of(null)),
    ).subscribe((detail) => {
      if (this.eventRefreshes.get(orderId) === version) {
        this.eventRefreshes.delete(orderId);
      }
      const queued = this.pendingEventRefreshes.delete(orderId);
      const stillOnBoard = this.orders().some((item) => item.id === orderId);

      if (detail && detail.id === orderId && stillOnBoard) {
        if ((this.orderEventVersions.get(orderId) ?? 0) > version) {
          // A newer event won the race; the queued read below brings the latest state.
        } else if (this.manualRequestPending.has(orderId)) {
          // A manual details read is in flight and applies the newest state itself.
        } else if (detail.sellerCompletedAtUtc && detail.fulfillmentType !== FulfillmentType.PickUp) {
          // Completed delivery orders leave the day board. A pickup stays on it in the
          // terminal column because its status remains Ready after the handover.
          this.orders.update((items) => items.filter((item) => item.id !== orderId));
        } else {
          this.patchOrderFromDetails(detail);
          this.itemsErrorOrderIds.update((ids) => {
            if (!ids.has(orderId)) return ids;
            const next = new Set(ids);
            next.delete(orderId);
            return next;
          });
        }
      }

      if (queued && this.orders().some((item) => item.id === orderId)) {
        this.refreshOrderFromEvent(orderId);
      }
    });
  }

  private utcTimestamp(value?: string): number {
    if (!value) return 0;
    const timestamp = Date.parse(value);
    return Number.isNaN(timestamp) ? 0 : timestamp;
  }

  private sortOrders(orders: OrderSummary[]): OrderSummary[] {
    return [...orders].sort((a, b) => this.orderTimestamp(b) - this.orderTimestamp(a));
  }

  private orderTimestamp(order: OrderSummary): number {
    const timestamp = Date.parse(order.createdAtUtc);
    return Number.isNaN(timestamp) ? 0 : timestamp;
  }

  private triggerAcceptedOrderPrint(orderId: string): void {
    // The queue enforces AutoPrint and the platform-specific automatic path. Printing failures are
    // reported in the dashboard and never roll back the accepted status.
    void this.automaticPrint.enqueueAcceptedOrder({ orderId }).catch(() => undefined);
  }

  private focusTargetOrder(orderId: string): void {
    setTimeout(() => {
      const target = document.querySelector<HTMLElement>(
        `.order-card[data-order-id="${orderId}"]`,
      );

      if (!target) return;

      target.scrollIntoView({ behavior: 'smooth', block: 'center' });
      target.focus();
    });
  }

  private focusOrderDetails(): void {
    setTimeout(() => {
      const dialog = document.querySelector<HTMLElement>('.order-details-dialog');
      dialog?.focus();
    });
  }
}
