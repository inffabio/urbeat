import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { addIcons } from 'ionicons';
import { arrowDown, arrowUp, bagCheckOutline, calendarOutline, carOutline, chevronDownOutline, journalOutline, locationOutline, notificationsOutline, peopleOutline, printOutline, receiptOutline, refreshOutline, settingsOutline, ticketOutline, timeOutline, walletOutline } from 'ionicons/icons';
import { IonIcon } from '@ionic/angular/standalone';
import { OrderService } from '../../core/services/order.service';
import { SubscriptionService } from '../../core/services/subscription.service';
import { RouterModule } from '@angular/router';
import { DashboardPeriod, formatSaoPauloDate, saoPauloPeriodRange } from '../../core/utils/sao-paulo-date.helper';
import { SubscriptionBannerStatus } from '../../shared/components/subscription-banner/subscription-banner.component';
import { OrderSummary, StoreOrdersReport } from '../../shared/models/order.model';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';

addIcons({
  'arrow-up': arrowUp,
  'arrow-down': arrowDown,
  'bag-check-outline': bagCheckOutline,
  'calendar-outline': calendarOutline,
  'car-outline': carOutline,
  'chevron-down-outline': chevronDownOutline,
  'journal-outline': journalOutline,
  'location-outline': locationOutline,
  'notifications-outline': notificationsOutline,
  'people-outline': peopleOutline,
  'print-outline': printOutline,
  'receipt-outline': receiptOutline,
  'refresh-outline': refreshOutline,
  'settings-outline': settingsOutline,
  'ticket-outline': ticketOutline,
  'time-outline': timeOutline,
  'wallet-outline': walletOutline,
});

const IN_PROGRESS_STATUSES: OrderStatus[] = [
  OrderStatus.Preparing,
  OrderStatus.Ready,
  OrderStatus.OnDelivery,
];

@Component({
  selector: 'app-seller-dashboard-page',
  standalone: true,
  imports: [
    CommonModule,
    IonIcon,
    RouterModule,
  ],
  templateUrl: './seller-dashboard-page.component.html',
  styleUrl: './seller-dashboard-page.component.scss',
})
export class SellerDashboardPageComponent implements OnInit {
  private readonly orders = inject(OrderService);
  private readonly subscriptionService = inject(SubscriptionService);
  readonly shell = inject(SellerShellFacade);
  readonly printing = inject(SellerPrintingService);

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly report = signal<StoreOrdersReport | null>(null);
  readonly recentOrders = signal<OrderSummary[]>([]);
  readonly selectedPeriod = signal<DashboardPeriod>('today');
  readonly subscriptionStatus = signal<SubscriptionBannerStatus>('ok');
  readonly subscriptionDueDate = signal('');
  private lastActivityPulseId: string | null = null;

  readonly periods: { label: string; value: DashboardPeriod }[] = [
    { label: 'Hoje', value: 'today' },
    { label: 'Semana', value: 'week' },
    { label: 'Mês', value: 'month' },
  ];

  readonly averageTicket = computed(() => {
    const r = this.report();
    if (!r || r.totalOrders === 0) return 0;
    return r.totalRevenue / r.totalOrders;
  });

  readonly inProgressOrders = computed(() =>
    this.recentOrders().filter((o) => IN_PROGRESS_STATUSES.includes(o.status)),
  );

  readonly deliverySummary = computed(() => {
    const deliveryOrders = this.recentOrders().filter(
      (o) => o.fulfillmentType === FulfillmentType.Delivery,
    );
    return {
      count: deliveryOrders.length,
      total: deliveryOrders.reduce((sum, o) => sum + o.total, 0),
    };
  });

  readonly pickupSummary = computed(() => {
    const pickupOrders = this.recentOrders().filter(
      (o) => o.fulfillmentType === FulfillmentType.PickUp,
    );
    return {
      count: pickupOrders.length,
      total: pickupOrders.reduce((sum, o) => sum + o.total, 0),
    };
  });

  readonly paymentMethodBreakdown = computed(() => {
    const map = new Map<string, { count: number; total: number }>();
    for (const o of this.recentOrders()) {
      const key = this.paymentLabel(o.paymentMethod);
      const entry = map.get(key) || { count: 0, total: 0 };
      entry.count += 1;
      entry.total += o.total;
      map.set(key, entry);
    }
    return Array.from(map.entries())
      .map(([label, data]) => ({ label, ...data }))
      .sort((a, b) => b.total - a.total);
  });

  readonly printerWarning = computed(() => {
    const cfg = this.printing.config();
    if (!cfg.autoPrint) return null;
    const state = this.printing.bluetoothState();
    if (cfg.connectionType === 'android-bluetooth' && state.status !== 'connected') {
      return 'Impressao automatica ativada, mas a impressora nao esta conectada.';
    }
    return null;
  });

  readonly currentDateLabel = computed(() => {
    const formatted = new Intl.DateTimeFormat('pt-BR', {
      day: '2-digit',
      month: 'long',
      timeZone: 'America/Sao_Paulo',
    }).format(new Date());

    return `Hoje, ${formatted}`;
  });

  readonly activePeriodLabel = computed(() =>
    this.periods.find((period) => period.value === this.selectedPeriod())?.label ?? 'Hoje',
  );

  constructor() {
    effect(() => {
      const pulse = this.shell.orderActivityPulse();
      if (!pulse || pulse.id === this.lastActivityPulseId) return;
      this.lastActivityPulseId = pulse.id;
      this.load({ silent: true });
    });
  }

  ngOnInit(): void {
    this.loadSubscription();
    this.load();
  }

  private loadSubscription(): void {
    this.subscriptionService.getMySubscription().subscribe({
      next: (sub) => {
        if (sub.nextDueDateUtc) {
          this.subscriptionDueDate.set(formatSaoPauloDate(sub.nextDueDateUtc));
        }
        if (sub.storeBlocked) {
          this.subscriptionStatus.set('overdue');
        } else if (sub.billingStatus === 2) {
          this.subscriptionStatus.set('due-soon');
        }
      },
    });
  }

  load(options?: { silent?: boolean }): void {
    if (!options?.silent) this.loading.set(true);
    if (!options?.silent) this.error.set(false);
    const range = saoPauloPeriodRange(this.selectedPeriod());
    this.orders.getStoreReport(range.startDateUtc, range.endDateUtc).subscribe({
      next: (report) => {
        this.report.set(report);
        this.orders.getStoreOrders({ pageSize: 100 }).subscribe({
          next: (orders) => {
            this.recentOrders.set(orders.items);
            this.loading.set(false);
          },
          error: () => {
            this.error.set(true);
            this.loading.set(false);
          },
        });
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  formatNumber(value: number | undefined): string {
    return String(value ?? 0);
  }

  selectPeriod(period: DashboardPeriod): void {
    if (this.selectedPeriod() === period) return;
    this.selectedPeriod.set(period);
    this.load();
  }

  statusLabel(status: OrderStatus): string {
    switch (status) {
      case OrderStatus.Created: return 'Criado';
      case OrderStatus.PendingPayment: return 'Aguardando pagamento';
      case OrderStatus.Received: return 'Recebido';
      case OrderStatus.Preparing: return 'Preparando';
      case OrderStatus.Ready: return 'Pronto';
      case OrderStatus.OnDelivery: return 'Saiu para entrega';
      case OrderStatus.Delivered: return 'Entregue';
      case OrderStatus.Cancelled: return 'Cancelado';
      default: return 'Desconhecido';
    }
  }

  paymentLabel(method?: PaymentMethod): string {
    switch (method) {
      case PaymentMethod.PixOnline: return 'Pix';
      case PaymentMethod.CardOnline: return 'Cartao Online';
      case PaymentMethod.CashOnDelivery: return 'Dinheiro';
      case PaymentMethod.CardOnDelivery: return 'Cartao na entrega';
      default: return '-';
    }
  }

  statusBadgeClass(status: OrderStatus): string {
    switch (status) {
      case OrderStatus.Created:
      case OrderStatus.PendingPayment:
      case OrderStatus.Received:
        return 'badge-orange';
      case OrderStatus.Preparing:
        return 'badge-blue';
      case OrderStatus.Ready:
        return 'badge-yellow';
      case OrderStatus.OnDelivery:
        return 'badge-purple';
      case OrderStatus.Delivered:
        return 'badge-green';
      case OrderStatus.Cancelled:
        return 'badge-red';
      default:
        return 'badge-gray';
    }
  }

  paymentBadgeClass(method?: PaymentMethod): string {
    switch (method) {
      case PaymentMethod.PixOnline:
        return 'badge-money';
      case PaymentMethod.CardOnline:
        return 'badge-blue';
      case PaymentMethod.CashOnDelivery:
        return 'badge-gray';
      case PaymentMethod.CardOnDelivery:
        return 'badge-debit';
      default:
        return 'badge-gray';
    }
  }
}
