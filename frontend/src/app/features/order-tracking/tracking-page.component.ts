import { Component, ElementRef, OnDestroy, OnInit, effect, inject, signal, computed, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';

import { OrderService } from '../../core/services/order.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { StoreContextService } from '../../core/services/store-context.service';
import { formatSaoPauloTime } from '../../core/utils/sao-paulo-date.helper';
import { OrderDetails, OrderItem, PickupStoreAddress } from '../../shared/models/order.model';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';

interface TimelineStep {
  status: OrderStatus;
  label: string;
  time?: string;
  state: 'past' | 'current' | 'future';
}

@Component({
  selector: 'app-tracking-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe],
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
  readonly deliveredCelebrationOpen = signal(false);
  private readonly currentOrderId = signal<string | null>(null);
  private routeParamSub?: Subscription;

  /**
   * Status last rendered for the order currently on screen. It separates the
   * live transition into `Delivered` (worth celebrating) from an order that was
   * already delivered when it was opened from the history list or after a
   * reload. The record is reset when the route switches orders, because a
   * status change that happened while another order was open is a first
   * observation when the customer comes back — never a transition to celebrate.
   */
  private renderedOrderStatus: { orderId: string; status: OrderStatus } | null = null;

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

  /**
   * Authoritative terminal state used by the timeline decoration. Only an
   * actual `Delivered` status stops the accordion sweep; intermediate states
   * can share the same numeric ratio, so ratio is not a valid signal here.
   */
  readonly delivered = computed(() => {
    const order = this.order();
    return order?.status === OrderStatus.Delivered && order.fulfillmentType === FulfillmentType.Delivery;
  });

  readonly loading = computed(() => this.order() === null && !this.invalidOrder() && !this.unavailable());

  readonly FulfillmentType = FulfillmentType;
  readonly OrderStatus = OrderStatus;
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

  readonly pickupStoreAddress = computed<PickupStoreAddress | null>(() => {
    const order = this.order();
    const address = order?.pickupStoreAddress;
    if (order?.fulfillmentType !== FulfillmentType.PickUp || !address) return null;
    const required = [address.zipCode, address.street, address.number, address.neighborhood, address.city, address.state];
    return required.every((value) => value.trim().length > 0) ? address : null;
  });

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
      { status: OrderStatus.Ready, label: isPickup ? 'Pronto para retirada' : 'Pronto' },
      ...(isPickup ? [] : [
        { status: OrderStatus.OnDelivery, label: 'Saiu para entregar' },
        { status: OrderStatus.Delivered, label: 'Entregue' },
      ]),
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

  /**
   * Track geometry derived from the authoritative steps only. The inset keeps
   * the line between the first and last dot centers; `ratio` is the normalized
   * position (0..1) of the current step, shared by the static fill and the
   * decorative accordion sweep. No future step is ever filled.
   */
  readonly timelineGeometry = computed(() => {
    const steps = this.steps();
    const count = Math.max(steps.length, 1);
    const currentIndex = steps.findIndex((step) => step.state === 'current');
    return {
      inset: `${50 / count}%`,
      ratio: count > 1 && currentIndex > 0 ? `${currentIndex / (count - 1)}` : '0',
    };
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
      case PaymentMethod.PixOnDelivery: return 'Pagar na entrega · Pix na maquininha';
      case PaymentMethod.DebitOnDelivery: return 'Pagar na entrega · Débito';
      case PaymentMethod.CreditOnDelivery: return 'Pagar na entrega · Crédito';
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

  private readonly celebrationAction = viewChild<ElementRef<HTMLButtonElement>>('celebrationAction');

  constructor() {
    // Celebrate only the transition into delivery observed on this screen.
    // SignalR reconnections, detail refreshes and history re-opens keep the
    // same order id, and the first snapshot of an already delivered order is
    // not a transition, so the modal never covers a finished order.
    effect(() => {
      const order = this.order();
      if (!order) return;

      const previous = this.renderedOrderStatus?.orderId === order.id
        ? this.renderedOrderStatus.status
        : undefined;
      if (previous === order.status) return;
      this.renderedOrderStatus = { orderId: order.id, status: order.status };

      if (order.status !== OrderStatus.Delivered) return;
      // `undefined` means the order arrived already delivered (history reopen).
      if (previous === undefined || previous === OrderStatus.Delivered) return;

      this.deliveredCelebrationOpen.set(true);
    });

    effect(() => {
      if (!this.deliveredCelebrationOpen()) return;
      this.celebrationAction()?.nativeElement.focus();
    });
  }

  ngOnInit(): void {
    this.routeParamSub = this.route.paramMap.subscribe((params) => {
      const orderId = params.get('orderId');
      if (this.currentOrderId() !== orderId) {
        // A celebration belongs to the order that was on screen when it was
        // delivered; it must never cover a different order opened afterwards.
        // The rendered status is per on-screen order as well: a transition that
        // happened while another order was open is a first observation when the
        // customer comes back, never a transition to celebrate.
        this.deliveredCelebrationOpen.set(false);
        this.renderedOrderStatus = null;
        this.currentOrderId.set(orderId);
      }
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

  /**
   * Closes the delivered celebration and returns to the menu. Only navigation
   * happens here: the tracked orders of this tab (including the finished one),
   * the cart and the checkout hint are preserved, so the order stays in
   * "Pedidos" for history consultation, delivery confirmation and re-opening.
   * Logout still clears everything through the auth token subscription.
   */
  finishDeliveredOrder(): void {
    this.deliveredCelebrationOpen.set(false);
    this.goToMenu();
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
