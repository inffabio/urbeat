import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { forkJoin } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
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

@Component({
  selector: 'app-seller-orders-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-orders-page.component.html',
  styleUrl: './seller-orders-page.component.scss',
})
export class SellerOrdersPageComponent implements OnInit {
  private readonly orderService = inject(OrderService);
  private readonly toast = inject(ToastService);
  private readonly printing = inject(SellerPrintingService);
  private readonly shell = inject(SellerShellFacade);
  private readonly route = inject(ActivatedRoute);
  private lastPulseId: string | null = null;
  private readonly targetOrderId = this.route.snapshot.queryParamMap.get('order');

  readonly OrderStatus = OrderStatus;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly updatingOrderId = signal<string | null>(null);
  readonly orders = signal<OrderSummary[]>([]);
  readonly pendingAction = signal<PendingAction | null>(null);
  readonly orderDetails = signal<Map<string, OrderItem[]>>(new Map());

  private readonly allStatuses = [
    OrderStatus.Received,
    OrderStatus.Preparing,
    OrderStatus.Ready,
    OrderStatus.OnDelivery,
    OrderStatus.Delivered,
  ];

  readonly newOrders = computed(() => this.statusGroups().received);

  readonly statusGroups = computed(() => {
    const all = this.orders();
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
  }

  ngOnInit(): void {
    this.load();
  }

  load(options?: { silent?: boolean }): void {
    if (!options?.silent) this.loading.set(true);
    if (!options?.silent) this.error.set(false);

    forkJoin(this.allStatuses.map((status) =>
      this.orderService.getStoreOrders({ pageSize: 50, status }),
    )).subscribe({
      next: (results) => {
        const items = this.sortOrders(results.flatMap((r) => r.items));
        this.orders.set(items);
        this.loading.set(false);
        this.loadOrderItems(items.filter((o) => o.status === OrderStatus.Received));
      },
      error: () => {
        if (!options?.silent) this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  private loadOrderItems(newOrders: OrderSummary[]): void {
    if (newOrders.length === 0) return;
    const details = new Map(this.orderDetails());

    forkJoin(newOrders.map((o) => this.orderService.getStoreOrder(o.id))).subscribe({
      next: (results) => {
        for (const detail of results) {
          details.set(detail.id, detail.items ?? []);
        }
        this.orderDetails.set(details);
      },
      error: () => { /* items are optional */ },
    });
  }

  orderItems(orderId: string): OrderItem[] {
    return this.orderDetails().get(orderId) ?? [];
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
        void this.toast.showSuccess('Pedido atualizado.');
        if (pending.order.status === OrderStatus.Received && pending.nextStatus === OrderStatus.Preparing) {
          this.triggerAcceptedOrderPrint(orderId);
        }
        this.shell.notifyOrderChanged(orderId);
        this.load({ silent: true });
      },
      error: () => {
        this.updatingOrderId.set(null);
        void this.toast.showError('Nao foi possivel atualizar o pedido.');
      },
    });
  }

  paymentLabel(method?: PaymentMethod): string {
    switch (method) {
      case PaymentMethod.PixOnline: return 'Pix ja pago';
      case PaymentMethod.CardOnline: return 'Cartao online';
      case PaymentMethod.CashOnDelivery: return 'Dinheiro ao receber';
      case PaymentMethod.CardOnDelivery: return 'Cartao ao receber';
      default: return '-';
    }
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
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
        `.order-card[data-order-id="${orderId}"], .status-card[data-order-id="${orderId}"]`,
      );

      if (!target) return;

      target.scrollIntoView({ behavior: 'smooth', block: 'center' });
      target.focus();
    });
  }
}
