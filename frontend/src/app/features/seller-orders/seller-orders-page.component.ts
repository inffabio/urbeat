import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { DashboardPeriod, formatSaoPauloDateTime, formatSaoPauloTime, saoPauloPeriodRange } from '../../core/utils/sao-paulo-date.helper';
import { isWindowsPlatform } from '../../core/utils/platform.helper';
import { OrderService } from '../../core/services/order.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ToastService } from '../../core/services/toast.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { OrderDetails, OrderItem, OrderSummary } from '../../shared/models/order.model';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';

interface PendingAction {
  order: OrderSummary;
  nextStatus: OrderStatus;
  label: string;
}

interface OrderStatusUpdateEvent {
  orderId?: string;
  orderCode?: string;
  status?: OrderStatus;
  changedAtUtc?: string;
}

interface BoardColumn {
  title: string;
  orders: OrderSummary[];
}

type BoardAction =
  | { kind: 'advance'; label: string; nextStatus: OrderStatus; css: string }
  | { kind: 'complete'; label: string; css: string };

@Component({
  selector: 'app-seller-orders-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-orders-page.component.html',
  styleUrl: './seller-orders-page.component.scss',
})
export class SellerOrdersPageComponent implements OnInit, OnDestroy {
  private readonly orderService = inject(OrderService);
  private readonly toast = inject(ToastService);
  private readonly printing = inject(SellerPrintingService);
  private readonly shell = inject(SellerShellFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly signalR = inject(SignalRService);
  private lastPulseId: string | null = null;
  private loadSequence = 0;
  private detailsSequence = 0;
  private readonly detailsVersion = new Map<string, number>();
  private readonly retryVersion = new Map<string, number>();
  private readonly manualRequestPending = new Map<string, number>();
  private readonly prefetchSequence = new Map<string, number>();
  private retrySequence = 0;
  private selectedOrderTrigger: HTMLElement | null = null;
  private readonly targetOrderId = this.route.snapshot.queryParamMap.get('order');
  private orderStatusListener?: (payload: OrderStatusUpdateEvent) => void;
  private keydownHandler?: (event: KeyboardEvent) => void;
  private fullscreenChangeHandler?: () => void;

  readonly isWindows = isWindowsPlatform();
  readonly isFullscreen = signal(false);
  readonly OrderStatus = OrderStatus;
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
    };
  });

  private readonly allStatuses = [
    OrderStatus.Received,
    OrderStatus.Preparing,
    OrderStatus.Ready,
    OrderStatus.OnDelivery,
    OrderStatus.Delivered,
  ];

  readonly newOrders = computed(() => this.statusGroups().received);

  readonly boardColumns = computed<BoardColumn[]>(() => [
    { title: 'Em preparação', orders: this.statusGroups().preparing },
    { title: 'Pronto para retirada', orders: this.statusGroups().ready },
    { title: 'Em entrega', orders: this.statusGroups().onDelivery },
    { title: 'Concluído', orders: this.statusGroups().delivered },
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
  }

  ngOnInit(): void {
    this.load();
    this.registerOrderStatusListener();
    this.registerFullscreenListeners();
  }

  ngOnDestroy(): void {
    if (this.orderStatusListener) {
      this.signalR.removeSellerListener('OrderStatusUpdated', this.orderStatusListener);
      this.orderStatusListener = undefined;
    }
    if (this.keydownHandler) {
      document.removeEventListener('keydown', this.keydownHandler);
      this.keydownHandler = undefined;
    }
    if (this.fullscreenChangeHandler) {
      document.removeEventListener('fullscreenchange', this.fullscreenChangeHandler);
      this.fullscreenChangeHandler = undefined;
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
      this.orderService.getStoreOrders({
        pageSize: 50,
        status,
        startDateUtc: range.startDateUtc,
        endDateUtc: range.endDateUtc,
      }),
    )).subscribe({
      next: (results) => {
        if (sequence !== this.loadSequence) return;
        const items = this.sortOrders(results.flatMap((r) => r.items));
        this.orders.set(items);
        this.syncDetailsFromSummaries(items);
        this.loading.set(false);
        this.loadOrderItems(items, sequence);
      },
      error: () => {
        if (sequence !== this.loadSequence) return;
        if (!options?.silent) this.error.set(true);
        this.loading.set(false);
      },
    });
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
    this.pendingAction.set({ order, nextStatus, label });
  }

  dismissConfirm(): void {
    this.pendingAction.set(null);
  }

  executeAdvance(): void {
    const pending = this.pendingAction();
    if (!pending || this.updatingOrderId()) return;

    const orderId = pending.order.id;
    this.updatingOrderId.set(orderId);
    this.pendingAction.set(null);

    this.orderService.updateStoreOrderStatus(orderId, pending.nextStatus, 'Atualizado pelo painel do lojista').subscribe({
      next: () => {
        this.updatingOrderId.set(null);
        this.orders.update((items) =>
          items.map((order) => (order.id === orderId ? { ...order, status: pending.nextStatus } : order)),
        );
        this.patchDetailStatus(orderId, pending.nextStatus);
        void this.toast.showSuccess('Pedido atualizado.');
        if (pending.order.status === OrderStatus.Received && pending.nextStatus === OrderStatus.Preparing) {
          this.triggerAcceptedOrderPrint(orderId);
        }
        this.shell.notifyOrderChanged(orderId);
        this.load({ silent: true });
      },
      error: () => {
        this.updatingOrderId.set(null);
        void this.toast.showError('Não foi possível atualizar o pedido.');
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
      if (!this.orders().some((order) => order.id === payload.orderId)) return;
      this.load({ silent: true });
    };
    this.signalR.onSellerEvent('OrderStatusUpdated', this.orderStatusListener);
  }

  private sortOrders(orders: OrderSummary[]): OrderSummary[] {
    return [...orders].sort((a, b) => this.orderTimestamp(b) - this.orderTimestamp(a));
  }

  private orderTimestamp(order: OrderSummary): number {
    const timestamp = Date.parse(order.createdAtUtc);
    return Number.isNaN(timestamp) ? 0 : timestamp;
  }

  private triggerAcceptedOrderPrint(orderId: string): void {
    try {
      void this.printing.printAcceptedOrder(orderId).catch(() => undefined);
    } catch {
      // best-effort: printing cannot block the status transition
    }
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
