import { Component, OnDestroy, OnInit, inject, signal, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';

import { OrderService } from '../../core/services/order.service';
import { CartService } from '../../core/services/cart.service';
import { SignalRService } from '../../core/services/signalr.service';
import { StoreContextService } from '../../core/services/store-context.service';
import { formatSaoPauloTime } from '../../core/utils/sao-paulo-date.helper';
import { OrderDetails } from '../../shared/models/order.model';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { BackToMenuLinkComponent } from '../../shared/components/back-to-menu-link/back-to-menu-link.component';

interface TimelineStep {
  status: OrderStatus;
  label: string;
  time?: string;
  state: 'past' | 'current' | 'future';
}

export interface OrderStatusUpdateEvent {
  orderId: string;
  orderCode: string;
  status: OrderStatus;
  changedAtUtc: string;
}

@Component({
  selector: 'app-tracking-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, BackToMenuLinkComponent],
  templateUrl: './tracking-page.component.html',
  styleUrl: './tracking-page.component.scss',
})
export class TrackingPageComponent implements OnInit, OnDestroy {
  private static readonly POLL_INTERVAL_MS = 30_000;

  private readonly orders = inject(OrderService);
  private readonly cart = inject(CartService);
  private readonly signalR = inject(SignalRService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly storeContext = inject(StoreContextService);

  readonly order = signal<OrderDetails | null>(null);
  readonly loading = signal(true);
  readonly confirmLoading = signal(false);
  private currentOrderId: string | null = null;
  private pollingTimer: ReturnType<typeof setInterval> | null = null;
  private trackingActive = true;
  private orderStatusListener?: (data: OrderStatusUpdateEvent) => void;

  readonly FulfillmentType = FulfillmentType;
  readonly PaymentMethod = PaymentMethod;

  readonly itemsCount = computed(() => {
    const o = this.order();
    if (!o) return 0;
    return o.items.reduce((sum, i) => sum + i.quantity, 0);
  });

  readonly orderCode = computed(() => {
    const o = this.order();
    return o ? `Pedido #${o.code}` : '';
  });

  readonly etaText = computed(() => {
    const o = this.order();
    if (!o) return '';
    const start = new Date(new Date(o.createdAtUtc).getTime() + 30 * 60_000);
    const end = new Date(new Date(o.createdAtUtc).getTime() + 60 * 60_000);
    const fmt = (d: Date) => formatSaoPauloTime(d);
    return `Hoje, entre ${fmt(start)} e ${fmt(end)}`;
  });

  readonly steps = computed<TimelineStep[]>(() => {
    const o = this.order();
    const defs: { status: OrderStatus; label: string }[] = [
      { status: OrderStatus.Received, label: 'Pedido recebido' },
      { status: OrderStatus.Preparing, label: 'Preparando seu pedido' },
      { status: OrderStatus.OnDelivery, label: 'Saiu para entrega' },
      { status: OrderStatus.Delivered, label: 'Entregue' },
    ];
    if (!o) return defs.map((d) => ({ ...d, state: 'future' }));
    return defs.map((d) => {
      const history = o.history.find((h) => h.newStatus === d.status);
      const time = history
        ? formatSaoPauloTime(history.createdAtUtc)
        : undefined;
      let state: 'past' | 'current' | 'future';
      if (o.status > d.status) state = 'past';
      else if (o.status === d.status) state = 'current';
      else state = 'future';
      return { ...d, time, state };
    });
  });

  readonly paymentLabel = computed(() => {
    const o = this.order();
    if (!o) return '';
    switch (o.paymentMethod) {
      case PaymentMethod.PixOnline: return 'Pago no app · Pix';
      case PaymentMethod.CardOnline: return 'Pago no app · Mercado Pago';
      case PaymentMethod.CashOnDelivery: return 'Pagar na entrega · Dinheiro';
      case PaymentMethod.CardOnDelivery: return 'Pagar na entrega · Cartão';
      default: return '—';
    }
  });

  readonly canConfirmDelivery = computed(() => {
    const o = this.order();
    return !!o
      && o.status === OrderStatus.Delivered
      && o.fulfillmentType === FulfillmentType.Delivery
      && !o.deliveryConfirmedAtUtc;
  });

  ngOnInit(): void {
    const orderId = this.route.snapshot.paramMap.get('orderId');
    if (!orderId) return;
    this.currentOrderId = orderId;

    this.load(orderId);
    this.setupSignalR();
  }

  ngOnDestroy(): void {
    this.trackingActive = false;
    this.clearPolling();
    if (this.orderStatusListener) {
      this.signalR.removeCustomerListener('OrderStatusUpdated', this.orderStatusListener);
      this.orderStatusListener = undefined;
    }
    this.signalR.stopCustomerHub();
  }

  private setupSignalR(): void {
    this.signalR.startCustomerHub().then(() => {
      if (!this.trackingActive || !this.currentOrderId) return;
      this.orderStatusListener = (data: OrderStatusUpdateEvent) => {
        if (data && data.orderId === this.currentOrderId) {
          this.load(this.currentOrderId!, true);
        }
      };
      this.signalR.onCustomerEvent('OrderStatusUpdated', this.orderStatusListener);
    }).catch(() => {
      console.warn('SignalR customer hub failed to start, falling back to polling.');
    });
  }

  private load(orderId: string, silent = false): void {
    if (!silent) this.loading.set(true);
    this.orders.getOrder(orderId).subscribe({
      next: (o) => {
        this.order.set(o);
        this.loading.set(false);
        this.syncTrackingState(o);
      },
      error: () => {
        this.loading.set(false);
        this.clearPolling();
      },
    });
  }

  private syncTrackingState(o: OrderDetails): void {
    const terminal = o.status === OrderStatus.Delivered || o.status === OrderStatus.Cancelled;
    const confirmationPending = o.status === OrderStatus.Delivered
      && o.fulfillmentType === FulfillmentType.Delivery
      && !o.deliveryConfirmedAtUtc;

    if (terminal && !confirmationPending) {
      this.trackingActive = false;
      this.clearPolling();
      if (this.orderStatusListener) {
        this.signalR.removeCustomerListener('OrderStatusUpdated', this.orderStatusListener);
        this.orderStatusListener = undefined;
      }
      return;
    }

    if (!this.pollingTimer) {
      this.pollingTimer = setInterval(() => {
        if (this.currentOrderId) this.load(this.currentOrderId, true);
      }, TrackingPageComponent.POLL_INTERVAL_MS);
    }
  }

  private clearPolling(): void {
    if (this.pollingTimer) {
      clearInterval(this.pollingTimer);
      this.pollingTimer = null;
    }
  }

  confirmDelivery(): void {
    const o = this.order();
    if (!o || !this.currentOrderId || this.confirmLoading()) return;
    this.confirmLoading.set(true);
    this.orders.confirmDelivery(o.id).subscribe({
      next: () => {
        this.confirmLoading.set(false);
        this.load(this.currentOrderId!, true);
      },
      error: () => this.confirmLoading.set(false),
    });
  }

  goToMenu(): void {
    const m = this.router.url.match(/^\/([^/]+)\//);
    this.router.navigate(['/', m?.[1] ?? 'burguer_do_rafa']);
  }

  refresh(): void {
    const orderId = this.route.snapshot.paramMap.get('orderId');
    if (orderId) this.load(orderId);
  }

  openHelp(): void {
    const phone = this.storeContext.phoneNumber();
    if (!phone) return;
    const digits = phone.replace(/\D/g, '');
    const name = this.storeContext.storeName() || 'a loja';
    const text = encodeURIComponent(`Olá, preciso de ajuda com meu pedido.`);
    window.open(`https://wa.me/55${digits}?text=${text}`, '_blank', 'noopener,noreferrer');
  }
}
