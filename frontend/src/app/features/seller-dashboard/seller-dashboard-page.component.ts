import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { addIcons } from 'ionicons';
import { printOutline } from 'ionicons/icons';
import { IonIcon } from '@ionic/angular/standalone';
import { RouterModule } from '@angular/router';
import { OrderService } from '../../core/services/order.service';
import { SubscriptionService } from '../../core/services/subscription.service';
import {
  analyticsMaxWeekBlock,
  analyticsWeekBlockEndIso,
  analyticsWeekBlockForDay,
  analyticsWeekBlockStartIso,
  formatSaoPauloDate,
  formatSaoPauloIsoDate,
  formatSaoPauloTime,
  saoPauloTodayIso,
} from '../../core/utils/sao-paulo-date.helper';
import {
  OrderSummary,
  PagedStoreSales,
  StoreAnalytics,
  StoreAnalyticsPeriod,
  StoreAnalyticsProduct,
  StoreAnalyticsQuery,
} from '../../shared/models/order.model';
import {
  SellerSubscriptionBillingStatus,
  SellerSubscriptionMyResponse,
} from '../../shared/models/subscription.model';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { DashboardPeriodFilterComponent } from './components/dashboard-period-filter/dashboard-period-filter.component';
import { DashboardSalesGridComponent } from './components/dashboard-sales-grid/dashboard-sales-grid.component';
import {
  AnalyticsDonutChartComponent,
  DonutSlice,
} from '../../shared/components/analytics-donut-chart/analytics-donut-chart.component';
import {
  AnalyticsBarChartComponent,
  BarDatum,
} from '../../shared/components/analytics-bar-chart/analytics-bar-chart.component';

addIcons({
  'print-outline': printOutline,
});

const IN_PROGRESS_STATUSES: OrderStatus[] = [
  OrderStatus.Received,
  OrderStatus.Preparing,
  OrderStatus.Ready,
  OrderStatus.OnDelivery,
];

type DashboardSubscriptionStatus = 'unknown' | 'none' | 'ok' | 'due-soon' | 'overdue' | 'cancelled';

interface SubscriptionNotice {
  tone: 'ok' | 'warning' | 'danger' | 'neutral';
  iconClass: string;
  message: string;
}

const MONTH_NAMES = [
  'Janeiro',
  'Fevereiro',
  'Março',
  'Abril',
  'Maio',
  'Junho',
  'Julho',
  'Agosto',
  'Setembro',
  'Outubro',
  'Novembro',
  'Dezembro',
];

const MONTH_LABELS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

const PAGE_SIZE = 20;

interface PaymentRow {
  paymentMethod: PaymentMethod;
  label: string;
  deliveredOrders: number;
  revenue: number;
  percent: number;
}

@Component({
  selector: 'app-seller-dashboard-page',
  standalone: true,
  imports: [
    IonIcon,
    RouterModule,
    DashboardPeriodFilterComponent,
    DashboardSalesGridComponent,
    AnalyticsDonutChartComponent,
    AnalyticsBarChartComponent,
  ],
  templateUrl: './seller-dashboard-page.component.html',
  styleUrl: './seller-dashboard-page.component.scss',
})
export class SellerDashboardPageComponent implements OnInit {
  private readonly orders = inject(OrderService);
  private readonly subscriptionService = inject(SubscriptionService);
  readonly shell = inject(SellerShellFacade);
  readonly printing = inject(SellerPrintingService);

  readonly todayIso = saoPauloTodayIso();

  readonly period = signal<StoreAnalyticsPeriod>('day');
  readonly anchorDate = signal<string>(this.todayIso);
  readonly weekBlock = signal<number>(this.initialWeekBlock(this.todayIso));

  readonly analytics = signal<StoreAnalytics | null>(null);
  readonly sales = signal<PagedStoreSales | null>(null);
  readonly salesPage = signal(1);
  readonly salesLoading = signal(false);
  readonly salesError = signal(false);
  readonly expandedOrders = signal<ReadonlySet<string>>(new Set<string>());

  readonly inProgressOrders = signal<OrderSummary[]>([]);
  readonly ordersTotal = signal<number | null>(null);

  readonly loading = signal(true);
  readonly refreshing = signal(false);
  readonly error = signal(false);
  readonly ordersError = signal(false);
  readonly staleWarning = signal(false);
  readonly lastUpdatedAt = signal<Date | null>(null);

  readonly subscriptionStatus = signal<DashboardSubscriptionStatus>('unknown');
  readonly subscriptionDueDate = signal('');
  readonly subscriptionLoadFailed = signal(false);
  readonly subscriptionLoaded = signal(false);
  readonly subscriptionRegularizationMessage = signal('');

  private filterSequence = 0;
  private salesSequence = 0;
  private requestedSalesPage = 1;
  private lastActivityPulseId: string | null = null;

  readonly currentYear = computed(() => Number(this.todayIso.slice(0, 4)) || new Date().getFullYear());

  readonly yearOptions = computed<readonly number[]>(() => {
    const values = new Set<number>(this.analytics()?.availableYears ?? []);
    values.add(this.currentYear());
    const anchorYear = Number(this.anchorDate().slice(0, 4));
    if (anchorYear > 0) values.add(anchorYear);
    return Array.from(values).sort((left, right) => right - left);
  });

  readonly rangeLabel = computed(() => {
    const period = this.period();
    const anchor = this.anchorDate();
    const year = Number(anchor.slice(0, 4));
    const month = Number(anchor.slice(5, 7));

    if (period === 'day') {
      return formatSaoPauloIsoDate(anchor);
    }

    if (period === 'week') {
      const start = formatSaoPauloIsoDate(anchor);
      const end = formatSaoPauloIsoDate(analyticsWeekBlockEndIso(anchor, this.weekBlock()));
      return `${start.slice(0, 5)} a ${end}`;
    }

    if (period === 'month') {
      return `${MONTH_NAMES[month - 1] ?? ''} de ${year}`;
    }

    return String(year);
  });

  readonly paymentRows = computed<readonly PaymentRow[]>(() => {
    const payments = this.analytics()?.payments ?? [];
    const total = payments.reduce((sum, payment) => sum + payment.deliveredOrders, 0);
    if (total === 0) return [];

    const raw = payments.map((payment) => (payment.deliveredOrders * 100) / total);
    const percents = raw.map((value) => Math.floor(value));
    let remaining = 100 - percents.reduce((sum, value) => sum + value, 0);

    const order = raw
      .map((value, index) => ({ index, fraction: value - Math.floor(value) }))
      .sort((left, right) => {
        if (right.fraction !== left.fraction) return right.fraction - left.fraction;
        const countDiff = payments[right.index].deliveredOrders - payments[left.index].deliveredOrders;
        if (countDiff !== 0) return countDiff;
        return payments[left.index].paymentMethod - payments[right.index].paymentMethod;
      });

    for (const entry of order) {
      if (remaining <= 0) break;
      percents[entry.index] += 1;
      remaining -= 1;
    }

    return payments.map((payment, index) => ({
      paymentMethod: payment.paymentMethod,
      label: this.paymentLabel(payment.paymentMethod),
      deliveredOrders: payment.deliveredOrders,
      revenue: payment.revenue,
      percent: percents[index] ?? 0,
    }));
  });

  readonly donutSlices = computed<readonly DonutSlice[]>(() => {
    const products = this.analytics()?.products ?? [];
    const top = products.slice(0, 5).map((product) => ({
      label: this.productLabel(product),
      value: product.quantity,
    }));
    const rest = products.slice(5);
    const restTotal = rest.reduce((sum, product) => sum + product.quantity, 0);
    return restTotal > 0 ? [...top, { label: 'Outros', value: restTotal }] : top;
  });

  readonly totalUnits = computed(() =>
    (this.analytics()?.products ?? []).reduce((sum, product) => sum + product.quantity, 0),
  );

  readonly products = computed<readonly StoreAnalyticsProduct[]>(() => this.analytics()?.products ?? []);

  readonly monthlyBars = computed<readonly BarDatum[]>(() => {
    const months = this.analytics()?.months ?? [];
    const anchorMonth = Number(this.anchorDate().slice(5, 7));
    const highlight = this.period() !== 'year';
    return months.map((month) => ({
      label: MONTH_LABELS[month.month - 1] ?? String(month.month),
      value: month.revenue,
      highlighted: highlight && month.month === anchorMonth,
    }));
  });

  readonly yearTotal = computed(() =>
    (this.analytics()?.months ?? []).reduce((sum, month) => sum + month.revenue, 0),
  );

  readonly bestMonth = computed(() => {
    const months = this.analytics()?.months ?? [];
    if (months.length === 0) return null;
    const best = months.reduce((max, month) => (month.revenue > max.revenue ? month : max), months[0]);
    if (best.revenue <= 0) return null;
    return `${MONTH_NAMES[best.month - 1] ?? ''} de ${best.year}`;
  });

  readonly chartYear = computed(() => this.analytics()?.chartYear ?? this.currentYear());

  readonly monthlyCaption = computed(() => `Faturamento por mês em ${this.chartYear()}`);

  readonly barAriaLabel = computed(
    () => `Faturamento por mês em ${this.chartYear()}. Total ${this.formatCurrency(this.yearTotal())}`,
  );

  readonly donutAriaLabel = computed(() => `Produtos mais pedidos em ${this.rangeLabel()}`);

  readonly lastUpdatedLabel = computed(() => {
    const updatedAt = this.lastUpdatedAt();
    return updatedAt ? `Atualizado às ${formatSaoPauloTime(updatedAt)}` : '';
  });

  readonly subscriptionNotice = computed<SubscriptionNotice>(() => {
    const dueDate = this.subscriptionDueDate();
    const dueText = dueDate ? ` Próximo vencimento ${dueDate}.` : '';

    switch (this.subscriptionStatus()) {
      case 'ok':
        return {
          tone: 'ok',
          iconClass: 'bi-check-circle',
          message: `Sua mensalidade está em dia!${dueText}`,
        };
      case 'due-soon':
        return {
          tone: 'warning',
          iconClass: 'bi-exclamation-circle',
          message: `Sua mensalidade vence em breve.${dueText}`,
        };
      case 'overdue':
        return {
          tone: 'danger',
          iconClass: 'bi-exclamation-triangle',
          message: this.subscriptionRegularizationMessage()
            || 'Sua mensalidade está pendente. Regularize para continuar recebendo pedidos.',
        };
      case 'cancelled':
        return {
          tone: 'danger',
          iconClass: 'bi-x-circle',
          message: 'Sua assinatura está cancelada.',
        };
      case 'none':
        return {
          tone: 'neutral',
          iconClass: 'bi-info-circle',
          message: 'Nenhuma assinatura ativa.',
        };
      default:
        return {
          tone: 'neutral',
          iconClass: 'bi-info-circle',
          message: this.subscriptionLoadFailed()
            ? 'Não foi possível confirmar o status da mensalidade.'
            : this.subscriptionLoaded()
              ? 'Status da mensalidade indisponível.'
              : 'Carregando informações da mensalidade.',
        };
    }
  });

  readonly printerWarning = computed(() => {    const config = this.printing.config();
    if (!config.autoPrint) return null;
    const state = this.printing.bluetoothState();
    if (config.connectionType === 'android-bluetooth' && state.status !== 'connected') {
      return 'Impressão automática ativada, mas a impressora não está conectada.';
    }
    return null;
  });

  readonly hasInProgress = computed(() => this.inProgressOrders().length > 0);
  readonly inProgressLimitNote = computed(() => (this.ordersTotal() ?? 0) >= 100);

  constructor() {
    effect(() => {
      const pulse = this.shell.orderActivityPulse();
      if (!pulse || pulse.id === this.lastActivityPulseId) return;
      this.lastActivityPulseId = pulse.id;
      this.load({ silent: true, resetPage: false });
    });
  }

  ngOnInit(): void {
    this.loadSubscription();
    this.load();
  }

  private loadSubscription(): void {
    this.subscriptionService.getMySubscription().subscribe({
      next: (subscription) => {
        this.subscriptionDueDate.set(
          subscription.nextDueDateUtc ? formatSaoPauloDate(subscription.nextDueDateUtc) : '',
        );
        this.subscriptionRegularizationMessage.set(subscription.regularizationMessage?.trim() ?? '');
        this.subscriptionStatus.set(this.resolveSubscriptionStatus(subscription));
        this.subscriptionLoaded.set(true);
        this.subscriptionLoadFailed.set(false);
      },
      error: () => {
        this.subscriptionStatus.set('unknown');
        this.subscriptionLoaded.set(false);
        this.subscriptionLoadFailed.set(true);
      },
    });
  }

  private resolveSubscriptionStatus(subscription: SellerSubscriptionMyResponse): DashboardSubscriptionStatus {
    if (!subscription.hasSubscription) return 'none';
    if (subscription.storeBlocked
      || subscription.billingStatus === SellerSubscriptionBillingStatus.Overdue
      || subscription.billingStatus === SellerSubscriptionBillingStatus.Blocked) {
      return 'overdue';
    }
    if (subscription.billingStatus === SellerSubscriptionBillingStatus.Cancelled) return 'cancelled';
    if (subscription.billingStatus === SellerSubscriptionBillingStatus.DueSoon) return 'due-soon';
    if (subscription.billingStatus === SellerSubscriptionBillingStatus.Active) return 'ok';
    return 'unknown';
  }

  load(options?: { silent?: boolean; resetPage?: boolean }): void {
    const silent = options?.silent ?? false;
    const resetPage = options?.resetPage ?? true;
    const sequence = ++this.filterSequence;

    // Every load invalidates sales requests from the previous cycle, including a silent
    // refresh that keeps the current rows visible while it runs.
    this.salesSequence++;

    if (silent) {
      this.refreshing.set(true);
    } else {
      this.loading.set(true);
      this.error.set(false);
    }

    if (resetPage) {
      this.analytics.set(null);
      this.lastUpdatedAt.set(null);
      this.staleWarning.set(false);
      this.salesPage.set(1);
      this.requestedSalesPage = 1;
      this.expandedOrders.set(new Set<string>());
      this.sales.set(null);
      this.salesError.set(false);
      this.salesLoading.set(true);
      this.inProgressOrders.set([]);
    }

    this.orders.getStoreAnalytics(this.filterQuery()).subscribe({
      next: (analytics) => {
        if (sequence !== this.filterSequence) return;

        this.analytics.set(analytics);
        this.lastUpdatedAt.set(new Date());
        this.error.set(false);
        this.staleWarning.set(false);

        this.loadSales({
          page: resetPage ? 1 : this.requestedSalesPage,
          resetExpansion: resetPage
        });
        this.loadInProgress(analytics, sequence);

        this.loading.set(false);
        this.refreshing.set(false);
      },
      error: () => {
        if (sequence !== this.filterSequence) return;

        this.loading.set(false);
        this.refreshing.set(false);
        this.salesLoading.set(false);
        this.salesError.set(true);
        if (this.analytics()) {
          this.staleWarning.set(true);
        } else {
          this.error.set(true);
        }
      },
    });
  }

  loadSales(options: { page: number; resetExpansion: boolean }, allowClamp = true): void {
    const sequence = ++this.salesSequence;
    this.requestedSalesPage = options.page;
    this.salesLoading.set(true);
    this.salesError.set(false);

    if (options.resetExpansion) {
      this.expandedOrders.set(new Set<string>());
    }

    this.orders.getStoreSales({ ...this.filterQuery(), page: options.page, pageSize: PAGE_SIZE }).subscribe({
      next: (result) => {
        if (sequence !== this.salesSequence) return;

        if (allowClamp && result.totalPages > 0 && result.page > result.totalPages) {
          this.loadSales({ page: result.totalPages, resetExpansion: true }, false);
          return;
        }

        this.sales.set(result);
        this.salesPage.set(result.page);
        const present = new Set(result.sales.map((sale) => sale.orderId));
        this.expandedOrders.set(
          new Set(Array.from(this.expandedOrders()).filter((orderId) => present.has(orderId))),
        );
        this.salesLoading.set(false);
      },
      error: () => {
        if (sequence !== this.salesSequence) return;
        this.salesError.set(true);
        this.salesLoading.set(false);
      },
    });
  }

  private loadInProgress(analytics: StoreAnalytics, sequence: number): void {
    const startDateUtc = new Date(analytics.rangeStartUtc).toISOString();

    // The operational listing keeps its inclusive contract for existing callers. Sending the
    // additive exclusive bound avoids the old -1ms conversion, which dropped the final
    // sub-millisecond ticks of the period.
    this.orders.getStoreOrders({
      pageSize: 100,
      startDateUtc,
      endExclusiveUtc: analytics.rangeEndUtcExclusive
    }).subscribe({
      next: (result) => {
        if (sequence !== this.filterSequence) return;
        this.inProgressOrders.set(
          result.items.filter((order) => IN_PROGRESS_STATUSES.includes(order.status)),
        );
        this.ordersTotal.set(result.totalItems);
        this.ordersError.set(false);
      },
      error: () => {
        if (sequence !== this.filterSequence) return;
        this.ordersError.set(true);
      },
    });
  }

  onPeriodChange(next: StoreAnalyticsPeriod): void {
    if (next === this.period()) return;

    const current = this.period();
    const anchor = this.anchorDate();
    const year = Number(anchor.slice(0, 4));
    const month = Number(anchor.slice(5, 7));
    let nextAnchor = anchor;
    let nextBlock = this.weekBlock();

    if (next === 'year') {
      nextAnchor = `${year}-01-01`;
    } else if (current === 'year') {
      nextAnchor = `${year}-01-01`;
      if (next === 'week') nextBlock = 1;
    } else if (next === 'day' || next === 'month') {
      nextAnchor = `${year}-${this.pad(month)}-01`;
    } else {
      if (current === 'day') {
        nextBlock = analyticsWeekBlockForDay(year, month, Number(anchor.slice(8, 10)));
      } else {
        nextBlock = Math.min(this.weekBlock(), analyticsMaxWeekBlock(year, month));
      }
      nextAnchor = analyticsWeekBlockStartIso(`${year}-${this.pad(month)}-01`, nextBlock);
    }

    this.period.set(next);
    this.anchorDate.set(nextAnchor);
    this.weekBlock.set(nextBlock);
    this.applyFilterChange();
  }

  onAnchorChange(anchor: string): void {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(anchor)) return;

    const year = Number(anchor.slice(0, 4));
    const month = Number(anchor.slice(5, 7));
    const day = Number(anchor.slice(8, 10));
    let nextAnchor = anchor;
    let nextBlock = this.weekBlock();

    if (this.period() === 'month') {
      nextAnchor = `${year}-${this.pad(month)}-01`;
    } else if (this.period() === 'year') {
      nextAnchor = `${year}-01-01`;
    } else if (this.period() === 'week') {
      nextBlock = day === 1
        ? Math.min(this.weekBlock(), analyticsMaxWeekBlock(year, month))
        : analyticsWeekBlockForDay(year, month, day);
      nextAnchor = analyticsWeekBlockStartIso(`${year}-${this.pad(month)}-01`, nextBlock);
    }

    this.anchorDate.set(nextAnchor);
    this.weekBlock.set(nextBlock);
    this.applyFilterChange();
  }

  onWeekBlockChange(block: number): void {
    const year = Number(this.anchorDate().slice(0, 4));
    const month = Number(this.anchorDate().slice(5, 7));
    const maxBlock = analyticsMaxWeekBlock(year, month);
    const nextBlock = Math.min(Math.max(block, 1), maxBlock);

    this.weekBlock.set(nextBlock);
    this.anchorDate.set(analyticsWeekBlockStartIso(`${year}-${this.pad(month)}-01`, nextBlock));
    this.applyFilterChange();
  }

  onPageChange(page: number): void {
    if (page < 1 || page === this.sales()?.page) return;
    this.loadSales({ page, resetExpansion: true });
  }

  toggleItems(orderId: string): void {
    const next = new Set(this.expandedOrders());
    if (next.has(orderId)) {
      next.delete(orderId);
    } else {
      next.add(orderId);
    }
    this.expandedOrders.set(next);
  }

  retry(): void {
    this.load();
  }

  retryRefresh(): void {
    this.load({ silent: true, resetPage: false });
  }

  retrySales(): void {
    if (!this.analytics() || this.staleWarning() || this.error()) {
      this.load({ silent: !!this.analytics(), resetPage: !this.analytics() });
      return;
    }

    this.loadSales({ page: this.requestedSalesPage, resetExpansion: false });
  }

  retryOrders(): void {
    const analytics = this.analytics();
    if (analytics) {
      this.loadInProgress(analytics, this.filterSequence);
    }
  }

  productLabel(product: StoreAnalyticsProduct): string {
    // Match the backend's PostgreSQL-compatible semantic: trim ASCII spaces only.
    const name = product.productName.replace(/^ +| +$/g, '');
    return name.length > 0 ? name : 'Item sem nome';
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
      .format(value)
      .replace(/\u00a0/g, ' ');
  }

  formatNumber(value: number | undefined | null): string {
    return String(value ?? 0);
  }

  formatPercent(value: number): string {
    return `${value}%`;
  }

  paymentLabel(method?: PaymentMethod): string {
    switch (method) {
      case PaymentMethod.PixOnline: return 'Pix';
      case PaymentMethod.CardOnline: return 'Cartão online';
      case PaymentMethod.CashOnDelivery: return 'Dinheiro';
      case PaymentMethod.CardOnDelivery: return 'Cartão na entrega';
      default: return '-';
    }
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

  private applyFilterChange(): void {
    this.load({ resetPage: true });
  }

  private filterQuery(): StoreAnalyticsQuery {
    const period = this.period();
    const anchor = this.anchorDate();
    return period === 'week'
      ? { period, anchor, weekBlock: this.weekBlock() }
      : { period, anchor };
  }

  private initialWeekBlock(anchor: string): number {
    const year = Number(anchor.slice(0, 4));
    const month = Number(anchor.slice(5, 7));
    const day = Number(anchor.slice(8, 10));
    if (!year || !month || !day) {
      return 1;
    }
    return analyticsWeekBlockForDay(year, month, day);
  }

  private pad(value: number): string {
    return String(value).padStart(2, '0');
  }
}
