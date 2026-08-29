import { Component, OnDestroy, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';

import { OrderService } from '../../core/services/order.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { StoreContextService } from '../../core/services/store-context.service';
import { formatSaoPauloTime } from '../../core/utils/sao-paulo-date.helper';
import { OrderDetails, OrderItem } from '../../shared/models/order.model';
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

@Component({
  selector: 'app-tracking-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, BackToMenuLinkComponent],
  templateUrl: './tracking-page.component.html',
  styleUrl: './tracking-page.component.scss',
})
export class TrackingPageComponent implements OnInit, OnDestroy {
  private readonly orders = inject(OrderService);
  private readonly tracking = inject(CustomerOrderTrackingService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly storeContext = inject(StoreContextService);

  readonly confirmLoading = signal(false);
  private readonly currentOrderId = signal<string | null>(null);
  private routeParamSub?: Subscription;

  readonly order = computed<OrderDetails | null>(() => {
    const id = this.currentOrderId();
    if (!id) return null;
    const found = this.tracking.trackedOrders().find((o) => o.id === id) ?? null;
    if (!found) return null;
    if (found.storeId !== this.storeContext.storeId()) return null;
    return found;
  });

  readonly invalidOrder = computed(() => {
    const id = this.currentOrderId();
    if (!id) return false;
    const found = this.tracking.trackedOrders().find((o) => o.id === id);
    return !!found && found.storeId !== this.storeContext.storeId();
  });

  readonly unavailable = computed(() => {
    const id = this.currentOrderId();
    if (!id) return false;
    return this.tracking.isOrderUnavailable(id);
  });

  readonly cancelled = computed(() => this.order()?.status === OrderStatus.Cancelled);

  readonly loading = computed(() => this.order() === null && !this.invalidOrder() && !this.unavailable());

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

  readonly isPickup = computed(() => this.order()?.fulfillmentType === FulfillmentType.PickUp);

  readonly etaLabel = computed(() => (this.isPickup() ? 'Retirada no local' : 'Previsão de entrega'));

  readonly etaIcon = computed(() => (this.isPickup() ? 'storefront-outline' : 'time-outline'));

  readonly etaText = computed(() => {
    const o = this.order();
    if (!o) return '';
    if (this.isPickup()) return 'A loja avisará quando estiver pronto';
    const start = new Date(new Date(o.createdAtUtc).getTime() + 30 * 60_000);
    const end = new Date(new Date(o.createdAtUtc).getTime() + 60 * 60_000);
    const fmt = (d: Date) => formatSaoPauloTime(d);
    return `Hoje, entre ${fmt(start)} e ${fmt(end)}`;
  });

  readonly steps = computed<TimelineStep[]>(() => {
    const o = this.order();
    const isPickup = o?.fulfillmentType === FulfillmentType.PickUp;
    const cancelled = o?.status === OrderStatus.Cancelled;
    const defs: { status: OrderStatus; label: string }[] = [
      { status: OrderStatus.Received, label: 'Recebido' },
      { status: OrderStatus.Preparing, label: 'Preparando' },
      { status: OrderStatus.Ready, label: 'Pronto' },
      ...(isPickup ? [] : [{ status: OrderStatus.OnDelivery, label: 'Saiu para entregar' }]),
      { status: OrderStatus.Delivered, label: 'Entregue' },
    ];
    if (!o) return defs.map((d) => ({ ...d, state: 'future' as const }));
    return defs.map((d) => {
      const history = o.history.find((h) => h.newStatus === d.status);
      const time = history && !cancelled
        ? formatSaoPauloTime(history.createdAtUtc)
        : undefined;
      let state: 'past' | 'current' | 'future';
      if (cancelled) state = 'future';
      else if (o.status > d.status) state = 'past';
      else if (o.status === d.status) state = 'current';
      else state = 'future';
      return { ...d, time, state };
    });
  });

  itemOptionEntries(item: OrderItem): { name: string; price?: number }[] {
    if (item.optionPrices && item.optionPrices.length > 0) {
      return item.optionPrices.map((option) => ({ name: option.name, price: option.price }));
    }

    const entries: { name: string; price?: number }[] = [];
    if (item.choiceOptionName) entries.push({ name: item.choiceOptionName });
    if (item.additionalNames) entries.push({ name: item.additionalNames });
    return entries;
  }

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
    this.routeParamSub = this.route.paramMap.subscribe((params) => {
      const orderId = params.get('orderId');
      this.currentOrderId.set(orderId);
      if (!orderId) return;

      // The store shell owns the shared service lifecycle. This page only
      // tracks the order of interest; it never starts or stops the hub.
      this.tracking.trackOrder(orderId);
    });
  }

  ngOnDestroy(): void {
    this.routeParamSub?.unsubscribe();
    this.currentOrderId.set(null);
  }

  confirmDelivery(): void {
    const o = this.order();
    const orderId = this.currentOrderId();
    if (!o || !orderId || this.confirmLoading()) return;
    this.confirmLoading.set(true);
    this.orders.confirmDelivery(o.id).subscribe({
      next: () => {
        this.confirmLoading.set(false);
        if (this.currentOrderId() === orderId) this.tracking.refresh(orderId);
      },
      error: () => this.confirmLoading.set(false),
    });
  }

  goToMenu(): void {
    const m = this.router.url.match(/^\/([^/]+)\//);
    this.router.navigate(['/', m?.[1] ?? 'burguer_do_rafa']);
  }

  refresh(): void {
    const orderId = this.currentOrderId();
    if (orderId) this.tracking.refresh(orderId);
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
