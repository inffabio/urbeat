import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { EMPTY, Observable, catchError, expand, forkJoin, map, of, toArray } from 'rxjs';
import { DashboardPeriod, formatSaoPauloDateTime, formatSaoPauloTime, saoPauloPeriodRange } from '../../core/utils/sao-paulo-date.helper';
import { isWindowsPlatform } from '../../core/utils/platform.helper';
import { OrderService } from '../../core/services/order.service';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { DeliveryRouteService } from '../../core/services/delivery-route.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerOrdersWindowService } from '../../core/services/seller-orders-window.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { SellerDeliveryDriver } from '../../shared/models/delivery-driver.model';
import {
  DeliveryRouteStatus,
  SendDeliveryRouteGroup,
  SendDeliveryRoutesResult,
} from '../../shared/models/delivery-route.model';
import {
  OrderDetails,
  OrderItem,
  OrderSummary,
  PagedOrderSummary,
} from '../../shared/models/order.model';
import { formatBrazilianPhone } from '../../shared/utils/brazilian-phone.utils';
import { AutomaticPrintQueueService } from '../seller-printing/automatic-print-queue.service';
import { OrderAcceptedForPrintingEvent } from '../seller-printing/seller-printing.models';
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

interface SendDeliveriesGroup {
  deliveryDriverId: string;
  driverName: string;
  orders: OrderSummary[];
  pendingOrders: OrderSummary[];
  alreadySentOrders: OrderSummary[];
}

interface SendDeliveriesSelection extends SendDeliveriesGroup {
  selectedOrders: OrderSummary[];
}

/** Printable manifest row. Only the fields the seller needs for a physical hand-off. */
interface SendDeliveriesManifestOrder {
  orderId: string;
  code: string;
  customerName: string;
  addressSummary: string;
  checked: boolean;
}

/** One manifest per motoboy; printing several starts each driver on a new page. */
interface SendDeliveriesManifest {
  key: string;
  driverName: string;
  driverPhone: string;
  orders: SendDeliveriesManifestOrder[];
}

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
  private readonly deliveryRouteService = inject(DeliveryRouteService);
  private readonly toast = inject(ToastService);
  private readonly automaticPrint = inject(AutomaticPrintQueueService);
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
  private retrySequence = 0;
  private selectedOrderTrigger: HTMLElement | null = null;
  private sendDeliveriesTrigger: HTMLElement | null = null;
  private readonly targetOrderId = this.route.snapshot.queryParamMap.get('order');
  private orderStatusListener?: (payload: OrderStatusUpdateEvent) => void;
  private acceptedForPrintingListener?: (payload: OrderAcceptedForPrintingEvent) => void;
  private realtimeWasConnected = false;
  private keydownHandler?: (event: KeyboardEvent) => void;
  private fullscreenChangeHandler?: () => void;
  private printCleanupTimer?: ReturnType<typeof setTimeout>;
  private readonly onAfterPrint = () => this.removePrintPortal();

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
  readonly sharingIndividualLink = signal(false);
  readonly sendDeliveriesDialogOpen = signal(false);
  readonly sendingDeliveries = signal(false);
  readonly includeAlreadySent = signal(false);
  readonly sendDeliveriesResult = signal<SendDeliveryRoutesResult | null>(null);
  /** Orders the seller physically checked for the current send batch. */
  readonly checkedSendOrderIds = signal<ReadonlySet<string>>(new Set());
  readonly sendDeliveriesConfirmed = signal(false);

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

  readonly sendDeliveriesGroups = computed<SendDeliveriesGroup[]>(() => {
    const groups = new Map<string, SendDeliveriesGroup>();

    for (const order of this.orders()) {
      if (!this.isSendableDelivery(order)) continue;
      const deliveryDriverId = order.deliveryDriverId;
      if (!deliveryDriverId) continue;

      const driver = this.deliveryDrivers().find(
        (item) => item.deliveryDriverId === deliveryDriverId,
      );
      let group = groups.get(deliveryDriverId);
      if (!group) {
        group = {
          deliveryDriverId,
          driverName: order.deliveryDriverName || driver?.name || 'Motoboy',
          orders: [],
          pendingOrders: [],
          alreadySentOrders: [],
        };
        groups.set(deliveryDriverId, group);
      }

      group.orders.push(order);
      if (this.isAlreadySent(order)) group.alreadySentOrders.push(order);
      else group.pendingOrders.push(order);
    }

    return Array.from(groups.values());
  });

  readonly pendingSendOrderCount = computed(() =>
    this.sendDeliveriesGroups().reduce(
      (total, group) => total + group.pendingOrders.length,
      0,
    ),
  );

  readonly alreadySentOrderCount = computed(() =>
    this.sendDeliveriesGroups().reduce(
      (total, group) => total + group.alreadySentOrders.length,
      0,
    ),
  );

  readonly unassignedOnDeliveryOrders = computed(() =>
    this.orders().filter((order) =>
      order.status === OrderStatus.OnDelivery
      && order.fulfillmentType === FulfillmentType.Delivery
      && !order.deliveryDriverId
      && !order.sellerCompletedAtUtc
      && !order.deliveryConfirmedAtUtc,
    ),
  );

  readonly includedSendGroups = computed<SendDeliveriesSelection[]>(() =>
    this.sendDeliveriesGroups()
      .map((group) => ({
        ...group,
        selectedOrders: this.includeAlreadySent()
          ? group.orders
          : group.pendingOrders,
      }))
      .filter((group) => group.selectedOrders.length > 0),
  );

  readonly sendDeliveriesOrderIds = computed(() =>
    this.includedSendGroups().flatMap((group) =>
      group.selectedOrders.map((order) => order.id),
    ),
  );

  readonly sendDeliveriesCheckedCount = computed(() => {
    const checked = this.checkedSendOrderIds();
    return this.sendDeliveriesOrderIds().filter((orderId) => checked.has(orderId)).length;
  });

  readonly allSendDeliveriesOrdersChecked = computed(() => {
    const orderIds = this.sendDeliveriesOrderIds();
    if (orderIds.length === 0) return false;
    const checked = this.checkedSendOrderIds();
    return orderIds.every((orderId) => checked.has(orderId));
  });

  /** Accessible status announced while the seller checks the physical volumes. */
  readonly sendDeliveriesConferenceStatus = computed(() => {
    const total = this.sendDeliveriesOrderIds().length;
    if (total === 0) return 'Nenhum pedido selecionado para envio.';

    const checked = this.sendDeliveriesCheckedCount();
    if (checked < total) {
      return `${checked} de ${total} pedido(s) conferido(s). Marque cada pedido separado antes de preparar os links.`;
    }
    if (!this.sendDeliveriesConfirmed()) {
      return 'Todos os pedidos foram conferidos. Marque a confirmação física para liberar o envio.';
    }
    return 'Conferência física completa. Você já pode preparar os links e abrir o WhatsApp.';
  });

  readonly canSendDeliveries = computed(() =>
    this.sendDeliveriesOrderIds().length > 0
    && this.allSendDeliveriesOrdersChecked()
    && this.sendDeliveriesConfirmed()
    && !this.sendingDeliveries(),
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
    this.orders().filter((o) => !o.sellerCompletedAtUtc),
  );

  readonly statusGroups = computed(() => {
    const all = this.visibleOrders();
    return {
      received: this.sortOrders(all.filter((o) => o.status === OrderStatus.Received)),
      preparing: this.sortOrders(all.filter((o) => o.status === OrderStatus.Preparing)),
      ready: this.sortOrders(all.filter((o) => o.status === OrderStatus.Ready)),
      onDelivery: this.sortOrders(all.filter((o) => o.status === OrderStatus.OnDelivery)),
      delivered: this.sortOrders(all.filter((o) => o.status === OrderStatus.Delivered)),
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
    this.removePrintPortal();
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

    forkJoin(this.allStatuses.map((status) =>
      this.loadStatusOrders(status, range.startDateUtc, range.endDateUtc),
    )).subscribe({
      next: (pagesByStatus) => {
        if (sequence !== this.loadSequence) return;
        const items = this.sortOrders(
          pagesByStatus.flatMap((pages) => pages.flatMap((page) => page.items)),
        );
        this.orders.set(items);
        this.syncDetailsFromSummaries(items);
        this.loading.set(false);
        this.loadOrderItems(items, sequence);
        void this.automaticPrint.reconcilePendingJobs();
      },
      error: () => {
        if (sequence !== this.loadSequence) return;
        if (!options?.silent) this.error.set(true);
        this.loading.set(false);
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
  ): Observable<PagedOrderSummary[]> {
    const request = (page: number) =>
      this.orderService.getStoreOrders({
        page,
        pageSize: 50,
        status,
        startDateUtc,
        endDateUtc,
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
      next: () => {
        this.completingOrderId.set(null);
        this.orders.update((items) => items.filter((o) => o.id !== orderId));
        void this.toast.showSuccess('Pedido concluído.');
      },
      error: () => {
        this.completingOrderId.set(null);
        void this.toast.showError('Não foi possível concluir o pedido.');
      },
    });
  }

  canComplete(order: OrderSummary): boolean {
    return order.status === OrderStatus.Delivered
      && !order.sellerCompletedAtUtc
      && (!!order.deliveryConfirmedAtUtc || this.isPickup(order));
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

  orderDriver(deliveryDriverId?: string | null): SellerDeliveryDriver | null {
    if (!deliveryDriverId) return null;
    return this.deliveryDrivers().find((driver) => driver.deliveryDriverId === deliveryDriverId) ?? null;
  }

  driverWhatsAppUrl(deliveryDriverId?: string | null): string | null {
    const driver = this.orderDriver(deliveryDriverId);
    if (!driver) return null;
    const phone = (driver.whatsAppPhoneNumber || driver.phoneNumber || '').replace(/\D/g, '');
    if (!phone) return null;
    return `https://wa.me/${phone.startsWith('55') ? phone : `55${phone}`}`;
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

  shareOrderRoute(): void {
    const order = this.selectedOrder();
    if (!order?.deliveryRouteId) return;

    this.deliveryRouteService.shareRoute(order.deliveryRouteId).subscribe({
      next: (result) => {
        if (!result.shareUrl) {
          void this.toast.showError('Não foi possível gerar o link da rota.');
          return;
        }

        const driver = this.deliveryDrivers().find(
          (item) => item.id === result.route?.deliveryDriverId,
        );
        const phone = (driver?.whatsAppPhoneNumber || driver?.phoneNumber || '').replace(/\D/g, '');
        if (!phone) {
          void this.toast.showError('Cadastre o WhatsApp do motoboy para compartilhar a rota.');
          return;
        }

        const withCountry = phone.startsWith('55') ? phone : `55${phone}`;
        const message = encodeURIComponent(
          `Rota de entregas: ${result.shareUrl}`,
        );
        window.open(`https://wa.me/${withCountry}?text=${message}`, '_blank', 'noopener,noreferrer');
        this.patchRouteInfoFromResult(result.route);
      },
      error: (err) => {
        void this.toast.showError(err?.error?.error ?? 'Não foi possível compartilhar a rota.');
      },
    });
  }

  private patchRouteInfoFromResult(route?: { id: string; status: DeliveryRouteStatus } | null): void {
    if (!route) return;
    const order = this.selectedOrder();
    if (!order) return;
    this.orders.update((items) => items.map((item) =>
      item.id === order.id
        ? { ...item, deliveryRouteId: route.id, deliveryRouteStatus: route.status }
        : item,
    ));
  }

  canShareIndividualDelivery(order: {
    status?: OrderStatus;
    deliveryStartedAtUtc?: string;
    deliveryDriverId?: string;
    deliveryConfirmedAtUtc?: string;
    sellerCompletedAtUtc?: string;
  }): boolean {
    return order.status === OrderStatus.OnDelivery
      && !!order.deliveryStartedAtUtc
      && !!order.deliveryDriverId
      && !order.deliveryConfirmedAtUtc
      && !order.sellerCompletedAtUtc;
  }

  shareIndividualDelivery(): void {
    const order = this.selectedOrder();
    if (!order || this.sharingIndividualLink()) return;

    this.sharingIndividualLink.set(true);
    this.deliveryRouteService.shareIndividualDelivery(order.id).subscribe({
      next: (result) => {
        this.sharingIndividualLink.set(false);
        if (!result.shareUrl) {
          void this.toast.showError(result.error ?? 'Não foi possível gerar o link da entrega.');
          return;
        }

        const driver = this.orderDriver(order.deliveryDriverId);
        const phone = (driver?.whatsAppPhoneNumber || driver?.phoneNumber || '').replace(/\D/g, '');
        if (!phone) {
          void this.toast.showError('Cadastre o WhatsApp do motoboy antes de compartilhar.');
          return;
        }

        const withCountry = phone.startsWith('55') ? phone : `55${phone}`;
        const message = encodeURIComponent(`Entrega do pedido #${order.code}: ${result.shareUrl}`);
        window.open(`https://wa.me/${withCountry}?text=${message}`, '_blank', 'noopener,noreferrer');
        void this.toast.showSuccess('Link individual da entrega gerado.');
      },
      error: (err) => {
        this.sharingIndividualLink.set(false);
        void this.toast.showError(err?.error?.error ?? 'Não foi possível compartilhar a entrega.');
      },
    });
  }

  private loadDeliveryDrivers(): void {
    this.deliveryDriverService.listStoreDrivers().subscribe({
      next: (drivers) => this.deliveryDrivers.set(drivers),
      error: () => this.deliveryDrivers.set([]),
    });
  }

  private isSendableDelivery(order: OrderSummary): boolean {
    return order.fulfillmentType === FulfillmentType.Delivery
      && order.status === OrderStatus.OnDelivery
      && !order.sellerCompletedAtUtc
      && !order.deliveryConfirmedAtUtc;
  }

  private isAlreadySent(order: OrderSummary): boolean {
    return order.deliveryRouteStatus === DeliveryRouteStatus.Active;
  }

  openSendDeliveries(event?: Event): void {
    if (this.pendingSendOrderCount() === 0) {
      void this.toast.showError('Nenhum pedido em entrega aguardando envio.');
      return;
    }

    const trigger = event?.currentTarget;
    this.sendDeliveriesTrigger = trigger instanceof HTMLElement ? trigger : null;
    this.includeAlreadySent.set(false);
    this.resetSendDeliveriesChecklist();
    this.sendDeliveriesDialogOpen.set(true);
    this.focusDialogElement('.send-deliveries-dialog');
  }

  toggleIncludeAlreadySent(value: boolean): void {
    if (this.includeAlreadySent() === value) return;

    this.includeAlreadySent.set(value);
    // A resend changes the physical batch: every order needs a fresh check and a fresh
    // global confirmation, so previously shared orders are never auto-marked as checked.
    this.sendDeliveriesConfirmed.set(false);
    if (value) {
      const alreadySentIds = new Set(
        this.sendDeliveriesGroups().flatMap((group) =>
          group.alreadySentOrders.map((order) => order.id),
        ),
      );
      this.checkedSendOrderIds.update((ids) =>
        new Set(Array.from(ids).filter((id) => !alreadySentIds.has(id))),
      );
    }
  }

  closeSendDeliveries(): void {
    this.sendDeliveriesDialogOpen.set(false);
    this.resetSendDeliveriesChecklist();
    this.restoreSendDeliveriesFocus();
  }

  isSendOrderChecked(orderId: string): boolean {
    return this.checkedSendOrderIds().has(orderId);
  }

  setSendOrderChecked(orderId: string, checked: boolean): void {
    this.checkedSendOrderIds.update((ids) => {
      const next = new Set(ids);
      if (checked) next.add(orderId);
      else next.delete(orderId);
      return next;
    });
    // Removing an order invalidates the global "all checked" statement.
    if (!checked) this.sendDeliveriesConfirmed.set(false);
  }

  setSendDeliveriesConfirmed(confirmed: boolean): void {
    if (confirmed && !this.allSendDeliveriesOrdersChecked()) {
      this.sendDeliveriesConfirmed.set(false);
      return;
    }
    this.sendDeliveriesConfirmed.set(confirmed);
  }

  private resetSendDeliveriesChecklist(): void {
    this.checkedSendOrderIds.set(new Set());
    this.sendDeliveriesConfirmed.set(false);
  }

  /** WhatsApp/phone label for a confirmation group, formatted for reading aloud. */
  sendGroupPhone(group: Pick<SendDeliveriesGroup, 'deliveryDriverId'>): string {
    const driver = this.orderDriver(group.deliveryDriverId);
    const phone = driver?.whatsAppPhoneNumber || driver?.phoneNumber || '';
    return phone ? formatBrazilianPhone(phone) : '';
  }

  resultGroupPhone(group: SendDeliveryRouteGroup): string {
    return formatBrazilianPhone(group.whatsAppPhoneNumber);
  }

  sendOrderCustomer(order: OrderSummary): string {
    return order.customerName?.trim() || 'Cliente não identificado';
  }

  sendOrderAddress(order: OrderSummary): string {
    const details = this.orderDetails().get(order.id);
    const composed = details ? this.composeAddress(details) : '';
    return composed || order.addressSummary?.trim() || 'Endereço no detalhe do pedido';
  }

  confirmSendDeliveries(): void {
    if (!this.canSendDeliveries()) return;

    const orderIds = this.sendDeliveriesOrderIds();
    this.sendingDeliveries.set(true);
    this.deliveryRouteService
      .sendDeliveryRoutes(orderIds, this.sendDeliveriesConfirmed())
      .subscribe({
        next: (result) => {
          this.sendingDeliveries.set(false);
          this.sendDeliveriesDialogOpen.set(false);
          this.resetSendDeliveriesChecklist();
          this.sendDeliveriesResult.set(result);
          this.applySendResult(result);
          this.focusDialogElement('.send-result-dialog');

          if (!result.groups.length) {
            void this.toast.showError(result.error ?? 'Nenhum link de entrega foi preparado.');
            return;
          }

          const label = result.groups.length === 1 ? 'link preparado' : 'links preparados';
          void this.toast.showSuccess(`${result.groups.length} ${label}, um por motoboy.`);
        },
        error: (err) => {
          this.sendingDeliveries.set(false);
          void this.toast.showError(
            err?.error?.error ?? 'Não foi possível preparar os links das entregas.',
          );
        },
      });
  }

  closeSendDeliveriesResult(): void {
    this.sendDeliveriesResult.set(null);
    this.restoreSendDeliveriesFocus();
  }

  openWhatsAppForGroup(group: SendDeliveryRouteGroup): void {
    if (!group.whatsAppUrl) {
      void this.toast.showError('Cadastre o WhatsApp do motoboy antes de enviar.');
      return;
    }

    const opened = window.open(group.whatsAppUrl, '_blank', 'noopener,noreferrer');
    if (!opened) {
      void this.toast.showError(
        'O navegador bloqueou a abertura do WhatsApp. Libere pop-ups para este site e tente novamente.',
      );
    }
  }

  copyDeliveryRouteLink(group: SendDeliveryRouteGroup): void {
    if (!navigator.clipboard?.writeText) {
      void this.toast.showError('Não foi possível copiar o link neste navegador.');
      return;
    }

    navigator.clipboard.writeText(group.shareUrl).then(
      () => void this.toast.showSuccess('Link da rota copiado.'),
      () => void this.toast.showError('Não foi possível copiar o link da rota.'),
    );
  }

  printSendDeliveriesManifest(group: SendDeliveriesSelection): void {
    this.printManifests([this.buildSelectionManifest(group)]);
  }

  printAllSendDeliveriesManifests(): void {
    const manifests = this.includedSendGroups().map((group) =>
      this.buildSelectionManifest(group),
    );
    this.printManifests(manifests);
  }

  printSendResultManifest(group: SendDeliveryRouteGroup): void {
    this.printManifests([this.buildResultManifest(group)]);
  }

  printAllSendResultManifests(): void {
    const manifests = (this.sendDeliveriesResult()?.groups ?? []).map((group) =>
      this.buildResultManifest(group),
    );
    this.printManifests(manifests);
  }

  private buildSelectionManifest(group: SendDeliveriesSelection): SendDeliveriesManifest {
    return {
      key: group.deliveryDriverId,
      driverName: group.driverName,
      driverPhone: this.sendGroupPhone(group),
      orders: group.selectedOrders.map((order) => ({
        orderId: order.id,
        code: order.code,
        customerName: this.sendOrderCustomer(order),
        addressSummary: this.sendOrderAddress(order),
        checked: this.isSendOrderChecked(order.id),
      })),
    };
  }

  private buildResultManifest(group: SendDeliveryRouteGroup): SendDeliveriesManifest {
    const stops = [...(group.route?.orders ?? [])].sort((a, b) => a.sequence - b.sequence);
    return {
      key: group.storeDeliveryDriverId,
      driverName: group.driverName,
      driverPhone: this.resultGroupPhone(group),
      orders: stops.map((stop) => ({
        orderId: stop.orderId,
        code: stop.code,
        customerName: stop.customerName?.trim() || 'Cliente não identificado',
        addressSummary: stop.addressSummary?.trim() || 'Endereço no detalhe do pedido',
        // The printed result manifest is used at dispatch, so the box stays blank.
        checked: false,
      })),
    };
  }

  /**
   * Builds a print-only portal on `document.body` so page breaks are not clipped by the
   * modal/overlay and no navigation or button is printed. Values are inserted with
   * `textContent`, never HTML, and the portal is discarded right after printing.
   */
  private printManifests(manifests: SendDeliveriesManifest[]): void {
    if (manifests.length === 0) return;

    this.removePrintPortal();
    const portal = document.createElement('div');
    portal.className = 'send-manifests-print-portal';
    portal.setAttribute('aria-hidden', 'true');
    for (const manifest of manifests) {
      portal.appendChild(this.createManifestElement(manifest));
    }

    document.body.appendChild(portal);
    document.body.classList.add('printing-send-manifests');
    window.addEventListener('afterprint', this.onAfterPrint);

    try {
      window.print();
    } catch {
      this.removePrintPortal();
      return;
    }

    // Embedded webviews may not fire `afterprint`; drop the temporary DOM anyway.
    this.printCleanupTimer = setTimeout(() => this.removePrintPortal(), 60_000);
  }

  private createManifestElement(manifest: SendDeliveriesManifest): HTMLElement {
    const article = document.createElement('article');
    article.className = 'send-manifest';

    const header = document.createElement('header');
    header.className = 'send-manifest-header';
    header.appendChild(this.createPrintText('h1', 'send-manifest-title', 'Manifesto de entrega'));
    header.appendChild(this.createPrintText('p', 'send-manifest-driver', manifest.driverName));
    header.appendChild(this.createPrintText(
      'p',
      'send-manifest-phone',
      manifest.driverPhone ? `WhatsApp: ${manifest.driverPhone}` : 'WhatsApp não cadastrado',
    ));
    header.appendChild(this.createPrintText(
      'p',
      'send-manifest-total',
      `${manifest.orders.length} pedido(s) · ${manifest.orders.length} volume(s)`,
    ));
    article.appendChild(header);

    const list = document.createElement('ol');
    list.className = 'send-manifest-orders';
    for (const order of manifest.orders) {
      const item = document.createElement('li');
      item.className = 'send-manifest-order';

      const head = document.createElement('div');
      head.className = 'send-manifest-order-head';
      head.appendChild(this.createPrintText('span', 'send-manifest-code', `#${order.code}`));
      const check = document.createElement('span');
      check.className = order.checked ? 'send-manifest-check is-checked' : 'send-manifest-check';
      check.textContent = 'Conferido';
      head.appendChild(check);

      item.appendChild(head);
      item.appendChild(this.createPrintText('p', 'send-manifest-customer', order.customerName));
      item.appendChild(this.createPrintText('p', 'send-manifest-address', order.addressSummary));
      list.appendChild(item);
    }
    article.appendChild(list);

    const footer = document.createElement('footer');
    footer.className = 'send-manifest-footer';
    footer.appendChild(this.createPrintText(
      'p',
      'send-manifest-confirmation',
      `Conferi fisicamente os ${manifest.orders.length} pedido(s) e confirmei que estão separados para ${manifest.driverName}.`,
    ));
    footer.appendChild(this.createPrintText(
      'p',
      'send-manifest-signature',
      'Assinatura: ________________________________   Data: ____/____/________',
    ));
    article.appendChild(footer);

    return article;
  }

  private createPrintText(tag: string, className: string, text: string): HTMLElement {
    const element = document.createElement(tag);
    element.className = className;
    element.textContent = text;
    return element;
  }

  private removePrintPortal(): void {
    window.removeEventListener('afterprint', this.onAfterPrint);
    if (this.printCleanupTimer !== undefined) {
      clearTimeout(this.printCleanupTimer);
      this.printCleanupTimer = undefined;
    }
    document.body.classList.remove('printing-send-manifests');
    document.querySelectorAll('.send-manifests-print-portal').forEach((node) => node.remove());
  }

  private applySendResult(result: SendDeliveryRoutesResult): void {
    const routesByOrder = new Map<string, { routeId: string; status: DeliveryRouteStatus }>();
    for (const group of result.groups) {
      for (const stop of group.route?.orders ?? []) {
        routesByOrder.set(stop.orderId, {
          routeId: group.route.id,
          status: group.route.status,
        });
      }
    }

    if (!routesByOrder.size) return;

    this.orders.update((items) => items.map((order) => {
      const route = routesByOrder.get(order.id);
      return route
        ? { ...order, deliveryRouteId: route.routeId, deliveryRouteStatus: route.status }
        : order;
    }));

    this.orderDetails.update((details) => {
      let changed = false;
      const updated = new Map(details);
      for (const [orderId, detail] of updated) {
        const route = routesByOrder.get(orderId);
        if (!route) continue;
        updated.set(orderId, {
          ...detail,
          deliveryRouteId: route.routeId,
          deliveryRouteStatus: route.status,
        });
        changed = true;
      }
      return changed ? updated : details;
    });
  }

  private focusDialogElement(selector: string): void {
    setTimeout(() => {
      document.querySelector<HTMLElement>(selector)?.focus();
    });
  }

  private restoreSendDeliveriesFocus(): void {
    const trigger = this.sendDeliveriesTrigger;
    this.sendDeliveriesTrigger = null;
    if (!trigger) return;
    setTimeout(() => {
      if (trigger.isConnected) trigger.focus();
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
      default: return '-';
    }
  }

  formatCurrency(value?: number | null): string {
    if (value == null || Number.isNaN(value)) return 'R$ 0,00';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  statusLabel(status?: OrderStatus): string {
    switch (status) {
      case OrderStatus.Created: return 'Criado';
      case OrderStatus.PendingPayment: return 'Aguardando pagamento';
      case OrderStatus.Received: return 'Recebido';
      case OrderStatus.Preparing: return 'Preparando';
      case OrderStatus.Ready: return 'Pronto';
      case OrderStatus.OnDelivery: return 'Saiu para entrega';
      case OrderStatus.Delivered: return 'Entregue';
      case OrderStatus.Cancelled: return 'Cancelado';
      default: return '';
    }
  }

  fulfillmentLabel(type?: FulfillmentType): string {
    if (type === undefined || type === null) return '';
    return type === FulfillmentType.PickUp ? 'Retirada' : 'Entrega';
  }

  isPickup(order: OrderSummary): boolean {
    return order.fulfillmentType === FulfillmentType.PickUp;
  }

  isPickupType(type?: FulfillmentType): boolean {
    return type === FulfillmentType.PickUp;
  }

  cardAction(order: OrderSummary): BoardAction | null {
    switch (order.status) {
      case OrderStatus.Received:
        return { kind: 'advance', label: 'Aceitar pedido', nextStatus: OrderStatus.Preparing, css: 'action-orange' };
      case OrderStatus.Preparing:
        return { kind: 'advance', label: 'Marcar como pronto', nextStatus: OrderStatus.Ready, css: 'action-orange' };
      case OrderStatus.Ready:
        return this.isPickup(order)
          ? { kind: 'advance', label: 'Concluir retirada', nextStatus: OrderStatus.Delivered, css: 'action-blue' }
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
      if (!this.orders().some((order) => order.id === payload.orderId)) return;
      this.load({ silent: true });
    };
    this.signalR.onSellerEvent('OrderStatusUpdated', this.orderStatusListener);

    this.acceptedForPrintingListener = (payload: OrderAcceptedForPrintingEvent) => {
      if (!payload?.orderId) return;
      void this.automaticPrint.enqueueAcceptedOrder(payload);
    };
    this.signalR.onSellerEvent('OrderAcceptedForPrinting', this.acceptedForPrintingListener);
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
