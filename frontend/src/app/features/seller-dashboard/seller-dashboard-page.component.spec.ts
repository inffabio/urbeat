import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { of, Subject, throwError } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { SubscriptionService } from '../../core/services/subscription.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { SellerSubscriptionBillingStatus } from '../../shared/models/subscription.model';
import { PagedStoreSales, StoreAnalytics } from '../../shared/models/order.model';
import { SellerDashboardPageComponent } from './seller-dashboard-page.component';

function analyticsFixture(overrides: Partial<StoreAnalytics> = {}): StoreAnalytics {
  return {
    period: 'day',
    anchorDate: '2026-10-29',
    weekBlock: null,
    rangeStartUtc: '2026-10-29T03:00:00Z',
    rangeEndUtcExclusive: '2026-10-30T03:00:00Z',
    timeZone: 'America/Sao_Paulo',
    chartYear: 2026,
    kpis: { deliveredOrders: 12, revenue: 1234.5, deliveryFees: 60, averageTicket: 102.88 },
    products: [
      { productName: 'X-Burger', quantity: 30, revenue: 900 },
      { productName: 'Batata grande', quantity: 18, revenue: 334.5 },
    ],
    payments: [
      { paymentMethod: PaymentMethod.PixOnline, deliveredOrders: 7, revenue: 800 },
      { paymentMethod: PaymentMethod.CashOnDelivery, deliveredOrders: 5, revenue: 434.5 },
    ],
    months: Array.from({ length: 12 }, (_, index) => ({
      year: 2026,
      month: index + 1,
      deliveredOrders: index === 9 ? 12 : 0,
      revenue: index === 9 ? 1234.5 : 0,
    })),
    availableYears: [2026, 2025],
    ...overrides,
  };
}

function salesFixture(overrides: Partial<PagedStoreSales> = {}): PagedStoreSales {
  return {
    period: 'day',
    anchorDate: '2026-10-29',
    weekBlock: null,
    rangeStartUtc: '2026-10-29T03:00:00Z',
    rangeEndUtcExclusive: '2026-10-30T03:00:00Z',
    timeZone: 'America/Sao_Paulo',
    page: 1,
    pageSize: 20,
    totalItems: 2,
    totalPages: 1,
    totals: { salesTotal: 12345.67, deliveryFees: 456.78 },
    sales: [
      {
        orderId: 'order-1',
        code: 'A1B2C3D4',
        customerName: 'Maria Silva',
        createdAtUtc: '2026-10-29T16:42:00Z',
        subtotal: 80,
        deliveryFee: 5,
        total: 85,
        items: [{ productName: 'X-Burger', variationName: null, quantity: 2, unitPrice: 35, totalPrice: 70 }],
      },
    ],
    ...overrides,
  };
}

describe('SellerDashboardPageComponent', () => {
  let orderServiceMock: {
    getStoreAnalytics: jest.Mock;
    getStoreSales: jest.Mock;
    getStoreOrders: jest.Mock;
  };
  let shellFacadeMock: any;

  beforeEach(() => {
    jest.useFakeTimers().setSystemTime(new Date('2026-10-29T12:00:00Z'));
    orderServiceMock = {
      getStoreAnalytics: jest.fn(),
      getStoreSales: jest.fn(),
      getStoreOrders: jest.fn(),
    };
    shellFacadeMock = {
      unreadCount: signal(3),
      ordersCount: signal(0),
      store: jest.fn(() => ({ isSubscriptionBlocked: false })),
      newOrderPulse: signal(null),
      orderActivityPulse: signal(null),
    };

    TestBed.configureTestingModule({
      imports: [SellerDashboardPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: OrderService, useValue: orderServiceMock },
        {
          provide: SubscriptionService,
          useValue: {
            getMySubscription: jest.fn().mockReturnValue(of({
              hasSubscription: true,
              billingStatus: SellerSubscriptionBillingStatus.Active,
              storeBlocked: false,
              nextDueDateUtc: null,
            })),
          },
        },
        { provide: SellerShellFacade, useValue: shellFacadeMock },
        {
          provide: SellerPrintingService,
          useValue: {
            config: jest.fn(() => ({ autoPrint: false, connectionType: 'browser-print' })),
            bluetoothState: jest.fn(() => ({ status: 'disconnected' })),
          },
        },
      ],
    });
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  function arrangeHappyPath() {
    orderServiceMock.getStoreAnalytics.mockReturnValue(of(analyticsFixture()));
    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture()));
    orderServiceMock.getStoreOrders.mockReturnValue(of({
      page: 1,
      pageSize: 100,
      totalItems: 5,
      totalPages: 1,
      items: [
        { id: 'rc', code: 'RC', status: OrderStatus.Received, total: 10, createdAtUtc: '2026-10-29T09:30:00Z' },
        { id: 'p', code: 'P', status: OrderStatus.Preparing, total: 10, createdAtUtc: '2026-10-29T10:00:00Z' },
        { id: 'r', code: 'R', status: OrderStatus.Ready, total: 10, createdAtUtc: '2026-10-29T11:00:00Z' },
        { id: 'd', code: 'D', status: OrderStatus.OnDelivery, total: 10, createdAtUtc: '2026-10-29T12:00:00Z' },
        { id: 'x', code: 'X', status: OrderStatus.Delivered, total: 10, createdAtUtc: '2026-10-29T13:00:00Z' },
      ],
    }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads analytics, sales and in-progress orders for the current local day', () => {
    arrangeHappyPath();

    expect(orderServiceMock.getStoreAnalytics).toHaveBeenCalledWith({ period: 'day', anchor: '2026-10-29' });
    expect(orderServiceMock.getStoreSales).toHaveBeenCalledWith({
      period: 'day',
      anchor: '2026-10-29',
      page: 1,
      pageSize: 20,
    });
    expect(orderServiceMock.getStoreOrders).toHaveBeenCalledWith({
      pageSize: 100,
      startDateUtc: '2026-10-29T03:00:00.000Z',
      endExclusiveUtc: '2026-10-30T03:00:00Z',
    });
  });

  it('renders the three KPIs with BRL formatting', () => {
    const fixture = arrangeHappyPath();
    const kpis = fixture.nativeElement.querySelectorAll('.analytics-kpi');

    expect(kpis).toHaveLength(3);
    expect(fixture.nativeElement.textContent).toContain('Pedidos entregues');
    expect(fixture.nativeElement.textContent).toContain('Faturamento');
    expect(fixture.nativeElement.textContent).toContain('Ticket médio');
    expect(fixture.nativeElement.textContent).toContain('12');
    expect(fixture.nativeElement.textContent).toContain('R$ 1.234,50');
    expect(fixture.nativeElement.textContent).toContain('R$ 102,88');
  });

  it('renders zero KPIs without NaN on an empty period', () => {
    orderServiceMock.getStoreAnalytics.mockReturnValue(of(analyticsFixture({
      kpis: { deliveredOrders: 0, revenue: 0, deliveryFees: 0, averageTicket: 0 },
      products: [],
      payments: [],
      months: Array.from({ length: 12 }, (_, index) => ({ year: 2026, month: index + 1, deliveredOrders: 0, revenue: 0 })),
    })));
    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture({
      totalItems: 0,
      totalPages: 0,
      totals: { salesTotal: 0, deliveryFees: 0 },
      sales: [],
    })));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('R$ 0,00');
    expect(fixture.nativeElement.textContent).not.toContain('NaN');
  });

  it('normalizes the anchor when the period changes', () => {
    const fixture = arrangeHappyPath();

    fixture.nativeElement.querySelector('[data-period="week"]').click();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreAnalytics).toHaveBeenLastCalledWith({
      period: 'week',
      anchor: '2026-10-29',
      weekBlock: 5,
    });

    fixture.nativeElement.querySelector('[data-period="month"]').click();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreAnalytics).toHaveBeenLastCalledWith({ period: 'month', anchor: '2026-10-01' });

    fixture.nativeElement.querySelector('[data-period="year"]').click();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreAnalytics).toHaveBeenLastCalledWith({ period: 'year', anchor: '2026-01-01' });
  });

  it('keeps a valid week block or falls back to the last block when the month changes', () => {
    const fixture = arrangeHappyPath();
    fixture.nativeElement.querySelector('[data-period="week"]').click();
    fixture.detectChanges();

    const monthSelect = fixture.nativeElement.querySelector('#dashboard-month') as HTMLSelectElement;
    monthSelect.value = '9';
    monthSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(orderServiceMock.getStoreAnalytics).toHaveBeenLastCalledWith({
      period: 'week',
      anchor: '2026-09-29',
      weekBlock: 5,
    });

    monthSelect.value = '2';
    monthSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(orderServiceMock.getStoreAnalytics).toHaveBeenLastCalledWith({
      period: 'week',
      anchor: '2026-02-22',
      weekBlock: 4,
    });
    expect(fixture.nativeElement.querySelector('[data-week-block="5"]')).toBeNull();
  });

  it('labels snapshot products with the same space-only normalization as the backend', () => {
    const fixture = arrangeHappyPath();
    const component = fixture.componentInstance;

    expect(component.productLabel({ productName: ' X-Burger ', quantity: 1, revenue: 1 })).toBe('X-Burger');
    expect(component.productLabel({ productName: '\tX-Burger', quantity: 1, revenue: 1 })).toBe('\tX-Burger');
    expect(component.productLabel({ productName: '   ', quantity: 1, revenue: 1 })).toBe('Item sem nome');
  });

  it('renders the product donut, note and accessible full ranking', () => {
    const products = [
      { productName: 'A', quantity: 10, revenue: 100 },
      { productName: 'B', quantity: 9, revenue: 90 },
      { productName: 'C', quantity: 8, revenue: 80 },
      { productName: 'D', quantity: 7, revenue: 70 },
      { productName: 'E', quantity: 6, revenue: 60 },
      { productName: 'F', quantity: 5, revenue: 50 },
    ];
    orderServiceMock.getStoreAnalytics.mockReturnValue(of(analyticsFixture({ products })));
    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture()));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    const legendItems = fixture.nativeElement.querySelectorAll('.donut-legend li');
    expect(legendItems).toHaveLength(6);
    expect(legendItems[5].textContent).toContain('Outros');
    expect(fixture.nativeElement.querySelector('.donut-center').textContent).toContain('45');
    expect(fixture.nativeElement.textContent).toContain('Nomes conforme registrados no pedido.');

    const ranking = fixture.nativeElement.querySelector('.products-ranking');
    expect(ranking.getAttribute('tabindex')).toBe('0');
    expect(ranking.getAttribute('role')).toBe('region');
    expect(ranking.querySelector('caption').textContent).toContain('Ranking de produtos do período');
    expect(ranking.querySelectorAll('tbody tr')).toHaveLength(6);
  });

  it('renders twelve month bars, highlight, details and support text', () => {
    const fixture = arrangeHappyPath();

    expect(fixture.nativeElement.querySelectorAll('.bar')).toHaveLength(12);
    expect(fixture.nativeElement.querySelectorAll('.bar')[9].classList.contains('is-highlighted')).toBe(true);
    expect(fixture.nativeElement.querySelectorAll('.bar-details tbody tr')).toHaveLength(12);
    expect(fixture.nativeElement.textContent).toContain('Total do ano');
    expect(fixture.nativeElement.textContent).toContain('Melhor mês');
  });

  it('renders payment percentages that sum to 100', () => {
    const fixture = arrangeHappyPath();
    const percentages = Array.from(fixture.nativeElement.querySelectorAll('.payment-percent'))
      .map((cell: any) => Number(cell.textContent.replace('%', '').trim()));

    expect(percentages.reduce((sum, value) => sum + value, 0)).toBe(100);
    expect(fixture.nativeElement.querySelectorAll('.payment-row')).toHaveLength(2);
    expect(fixture.nativeElement.textContent).toContain('R$ 800,00');
  });

  it('produces the documented range labels', () => {
    const fixture = arrangeHappyPath();
    const component = fixture.componentInstance;

    expect(component.rangeLabel()).toBe('29/10/2026');

    component.onPeriodChange('week');
    fixture.detectChanges();
    expect(component.rangeLabel()).toBe('29/10 a 31/10/2026');

    component.onPeriodChange('month');
    fixture.detectChanges();
    expect(component.rangeLabel()).toBe('Outubro de 2026');

    component.onPeriodChange('year');
    fixture.detectChanges();
    expect(component.rangeLabel()).toBe('2026');
  });

  it('includes Received in the in-progress list and does not overwrite the shell badge', () => {
    shellFacadeMock.ordersCount.set(7);
    const fixture = arrangeHappyPath();

    expect(shellFacadeMock.ordersCount()).toBe(7);
    expect(fixture.componentInstance.inProgressOrders().map((order) => order.id)).toEqual(['rc', 'p', 'r', 'd']);
  });

  it('keeps the shell badge and the rest of the dashboard when in-progress orders fail', () => {
    shellFacadeMock.ordersCount.set(7);
    const fixture = arrangeHappyPath();
    orderServiceMock.getStoreOrders.mockReturnValue(throwError(() => new Error('orders down')));

    shellFacadeMock.orderActivityPulse.set({ id: 'pulse-1', orderId: 'x', source: 'manual-status-change' });
    fixture.detectChanges();

    expect(shellFacadeMock.ordersCount()).toBe(7);
    expect(fixture.componentInstance.ordersError()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar os pedidos.');
    expect(fixture.nativeElement.textContent).toContain('Pedidos entregues');
  });

  it('only reloads sales when the page changes', () => {
    const fixture = arrangeHappyPath();
    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture({ page: 2, totalPages: 3, totalItems: 57 })));

    fixture.componentInstance.onPageChange(2);
    fixture.detectChanges();

    expect(orderServiceMock.getStoreAnalytics).toHaveBeenCalledTimes(1);
    expect(orderServiceMock.getStoreOrders).toHaveBeenCalledTimes(1);
    expect(orderServiceMock.getStoreSales).toHaveBeenCalledTimes(2);
    expect(orderServiceMock.getStoreSales).toHaveBeenLastCalledWith({
      period: 'day',
      anchor: '2026-10-29',
      page: 2,
      pageSize: 20,
    });
  });

  it('drops stale analytics and sales responses', () => {
    const analyticsSubjects: Subject<StoreAnalytics>[] = [];
    const salesSubjects: Subject<PagedStoreSales>[] = [];
    orderServiceMock.getStoreAnalytics.mockImplementation(() => {
      const subject = new Subject<StoreAnalytics>();
      analyticsSubjects.push(subject);
      return subject.asObservable();
    });
    orderServiceMock.getStoreSales.mockImplementation(() => {
      const subject = new Subject<PagedStoreSales>();
      salesSubjects.push(subject);
      return subject.asObservable();
    });
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.onPeriodChange('week');
    fixture.detectChanges();
    expect(analyticsSubjects.length).toBe(2);

    analyticsSubjects[1].next(analyticsFixture({ period: 'week', anchorDate: '2026-10-29', weekBlock: 5 }));
    analyticsSubjects[1].complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.analytics()?.period).toBe('week');

    analyticsSubjects[0].next(analyticsFixture({ period: 'day', anchorDate: '2026-10-29' }));
    analyticsSubjects[0].complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.analytics()?.period).toBe('week');

    expect(salesSubjects.length).toBe(1);
    salesSubjects[0].next(salesFixture({ page: 1 }));
    salesSubjects[0].complete();
    fixture.detectChanges();

    fixture.componentInstance.onPageChange(2);
    fixture.detectChanges();
    expect(salesSubjects.length).toBe(2);

    salesSubjects[1].next(salesFixture({ page: 2, totalItems: 2, totalPages: 2 }));
    salesSubjects[1].complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.sales()?.page).toBe(2);

    salesSubjects[0].next(salesFixture({ page: 1, totalItems: 1, totalPages: 1 }));
    salesSubjects[0].complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.sales()?.page).toBe(2);
  });

  it('shows the full error state when the first analytics load fails', () => {
    orderServiceMock.getStoreAnalytics.mockReturnValue(throwError(() => new Error('down')));
    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture()));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar o dashboard');
    expect(fixture.nativeElement.textContent).toContain('Tentar novamente');
    expect(fixture.componentInstance.error()).toBe(true);
    expect(orderServiceMock.getStoreSales).not.toHaveBeenCalled();
  });

  it('keeps the previous data and warns when a refresh fails', () => {
    const fixture = arrangeHappyPath();
    orderServiceMock.getStoreAnalytics.mockReturnValue(throwError(() => new Error('down')));

    shellFacadeMock.orderActivityPulse.set({ id: 'pulse-2', orderId: 'x', source: 'manual-status-change' });
    fixture.detectChanges();

    expect(fixture.componentInstance.analytics()).not.toBeNull();
    expect(fixture.componentInstance.staleWarning()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Não foi possível atualizar os números. Exibindo os últimos dados carregados.');
  });

  it('keeps KPIs and charts when only the sales grid fails', () => {
    orderServiceMock.getStoreAnalytics.mockReturnValue(of(analyticsFixture()));
    orderServiceMock.getStoreSales.mockReturnValue(throwError(() => new Error('sales down')));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.salesError()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Faturamento');
    expect(fixture.nativeElement.querySelector('app-analytics-donut-chart')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar as vendas.');
  });

  it('clears the previous analytics while a new filter loads', () => {
    const fixture = arrangeHappyPath();
    const pending = new Subject<StoreAnalytics>();
    orderServiceMock.getStoreAnalytics.mockReturnValueOnce(pending.asObservable());

    fixture.componentInstance.onPeriodChange('week');
    fixture.detectChanges();

    expect(fixture.componentInstance.analytics()).toBeNull();
    expect(fixture.componentInstance.inProgressOrders()).toEqual([]);
    expect(fixture.componentInstance.salesLoading()).toBe(true);
    expect(fixture.nativeElement.querySelector('.dashboard-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('R$ 1.234,50');

    pending.next(analyticsFixture({ period: 'week', anchorDate: '2026-10-29', weekBlock: 5 }));
    pending.complete();
    fixture.detectChanges();
  });

  it('clears old analytics and shows the full error when a filter change fails', () => {
    const fixture = arrangeHappyPath();
    orderServiceMock.getStoreAnalytics.mockReturnValueOnce(throwError(() => new Error('down')));

    fixture.componentInstance.onPeriodChange('week');
    fixture.detectChanges();

    expect(fixture.componentInstance.analytics()).toBeNull();
    expect(fixture.componentInstance.error()).toBe(true);
    expect(fixture.componentInstance.salesError()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar o dashboard');
    expect(fixture.nativeElement.textContent).not.toContain('R$ 1.234,50');
  });

  it('marks the grid as failed and retries analytics when a silent refresh fails', () => {
    const fixture = arrangeHappyPath();
    orderServiceMock.getStoreAnalytics.mockReturnValue(throwError(() => new Error('down')));

    shellFacadeMock.orderActivityPulse.set({ id: 'pulse-3', orderId: 'x', source: 'manual-status-change' });
    fixture.detectChanges();

    expect(fixture.componentInstance.staleWarning()).toBe(true);
    expect(fixture.componentInstance.salesError()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar as vendas.');

    orderServiceMock.getStoreAnalytics.mockReturnValue(of(analyticsFixture()));
    fixture.componentInstance.retrySales();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreAnalytics).toHaveBeenCalledTimes(3);
    expect(fixture.componentInstance.salesError()).toBe(false);
    expect(fixture.componentInstance.staleWarning()).toBe(false);
  });

  it('retries the page requested before a failure', () => {
    const fixture = arrangeHappyPath();
    orderServiceMock.getStoreSales.mockReturnValueOnce(throwError(() => new Error('sales down')));

    fixture.componentInstance.onPageChange(2);
    fixture.detectChanges();

    expect(fixture.componentInstance.salesPage()).toBe(1);
    expect(fixture.componentInstance.salesError()).toBe(true);

    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture({ page: 2, totalItems: 57, totalPages: 3 })));
    fixture.componentInstance.retrySales();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreSales).toHaveBeenLastCalledWith({
      period: 'day',
      anchor: '2026-10-29',
      page: 2,
      pageSize: 20,
    });
    expect(fixture.componentInstance.salesPage()).toBe(2);
    expect(fixture.componentInstance.salesError()).toBe(false);
  });

  it('invalidates in-flight sales on every refresh, including silent refresh', () => {
    const fixture = arrangeHappyPath();
    const pageTwoSales = new Subject<PagedStoreSales>();
    const refreshAnalytics = new Subject<StoreAnalytics>();
    orderServiceMock.getStoreSales.mockReturnValueOnce(pageTwoSales.asObservable());
    orderServiceMock.getStoreAnalytics.mockReturnValueOnce(refreshAnalytics.asObservable());

    fixture.componentInstance.onPageChange(2);
    fixture.detectChanges();
    fixture.componentInstance.load({ silent: true, resetPage: false });
    fixture.detectChanges();

    pageTwoSales.next(salesFixture({ page: 2, totalItems: 57, totalPages: 3 }));
    pageTwoSales.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.sales()?.page).toBe(1);

    const refreshedSales = new Subject<PagedStoreSales>();
    orderServiceMock.getStoreSales.mockReturnValueOnce(refreshedSales.asObservable());
    refreshAnalytics.next(analyticsFixture());
    refreshAnalytics.complete();
    fixture.detectChanges();
    refreshedSales.next(salesFixture({ page: 1 }));
    refreshedSales.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.sales()?.page).toBe(1);
    expect(fixture.componentInstance.salesError()).toBe(false);
  });

  it('resets page and expansion when the filter changes', () => {
    const fixture = arrangeHappyPath();
    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture({ page: 2, totalPages: 3, totalItems: 57 })));
    fixture.componentInstance.onPageChange(2);
    fixture.detectChanges();
    fixture.componentInstance.toggleItems('order-1');
    fixture.detectChanges();
    expect(fixture.componentInstance.expandedOrders().has('order-1')).toBe(true);

    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture({ page: 1 })));
    fixture.componentInstance.onAnchorChange('2026-10-20');
    fixture.detectChanges();

    expect(fixture.componentInstance.salesPage()).toBe(1);
    expect(fixture.componentInstance.expandedOrders().size).toBe(0);
    expect(orderServiceMock.getStoreSales).toHaveBeenLastCalledWith({
      period: 'day',
      anchor: '2026-10-20',
      page: 1,
      pageSize: 20,
    });
  });

  it('requests the last valid page once when the current page is beyond the end', () => {
    orderServiceMock.getStoreAnalytics.mockReturnValue(of(analyticsFixture()));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    orderServiceMock.getStoreSales
      .mockReturnValueOnce(of(salesFixture({ page: 5, totalPages: 3, totalItems: 57, sales: [] })))
      .mockReturnValueOnce(of(salesFixture({ page: 3, totalPages: 3, totalItems: 57, sales: [] })));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(orderServiceMock.getStoreSales).toHaveBeenCalledTimes(2);
    expect(orderServiceMock.getStoreSales).toHaveBeenLastCalledWith({
      period: 'day',
      anchor: '2026-10-29',
      page: 3,
      pageSize: 20,
    });
  });

  it('removes shortcuts, service summary, last orders and the date pill', () => {
    const fixture = arrangeHappyPath();
    const text = fixture.nativeElement.textContent;

    expect(text).not.toContain('Atalhos rápidos');
    expect(text).not.toContain('Resumo por serviço');
    expect(text).not.toContain('Últimos pedidos');
    expect(fixture.nativeElement.querySelector('.date-pill')).toBeNull();
  });

  function arrangeWithSubscription(payload: Record<string, unknown>) {
    TestBed.overrideProvider(SubscriptionService, {
      useValue: { getMySubscription: jest.fn().mockReturnValue(of(payload)) },
    });
    return arrangeHappyPath();
  }

  it('reports an active subscription with the real due date', () => {
    const fixture = arrangeWithSubscription({
      hasSubscription: true,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      storeBlocked: false,
      nextDueDateUtc: '2026-11-10T12:00:00.000Z',
    });

    expect(fixture.nativeElement.textContent).toContain('Sua mensalidade está em dia!');
    expect(fixture.nativeElement.textContent).toContain('10/11/2026');
  });

  it('does not claim the subscription is current when it is due soon', () => {
    const fixture = arrangeWithSubscription({
      hasSubscription: true,
      billingStatus: SellerSubscriptionBillingStatus.DueSoon,
      storeBlocked: false,
      nextDueDateUtc: '2026-11-10T12:00:00.000Z',
    });

    expect(fixture.nativeElement.textContent).toContain('Sua mensalidade vence em breve.');
    expect(fixture.nativeElement.textContent).not.toContain('em dia');
  });

  it('shows the blocked/overdue message instead of the current-subscription claim', () => {
    const fixture = arrangeWithSubscription({
      hasSubscription: true,
      billingStatus: SellerSubscriptionBillingStatus.Blocked,
      storeBlocked: true,
      regularizationMessage: 'Regularize sua mensalidade para voltar a receber pedidos.',
    });

    expect(fixture.nativeElement.textContent).toContain('Regularize sua mensalidade');
    expect(fixture.nativeElement.textContent).not.toContain('em dia');
  });

  it('shows a cancelled subscription without claiming it is current', () => {
    const fixture = arrangeWithSubscription({
      hasSubscription: true,
      billingStatus: SellerSubscriptionBillingStatus.Cancelled,
      storeBlocked: false,
    });

    expect(fixture.nativeElement.textContent).toContain('Sua assinatura está cancelada.');
    expect(fixture.nativeElement.textContent).not.toContain('em dia');
  });

  it('does not claim a subscription exists when the seller has none', () => {
    const fixture = arrangeWithSubscription({ hasSubscription: false, storeBlocked: false });

    expect(fixture.nativeElement.textContent).toContain('Nenhuma assinatura ativa.');
    expect(fixture.nativeElement.textContent).not.toContain('em dia');
  });

  it('does not claim the subscription is current when its status cannot be loaded', () => {
    TestBed.overrideProvider(SubscriptionService, {
      useValue: { getMySubscription: jest.fn().mockReturnValue(throwError(() => new Error('down'))) },
    });
    const fixture = arrangeHappyPath();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível confirmar o status da mensalidade.');
    expect(fixture.nativeElement.textContent).not.toContain('em dia');
  });

  it('does not promise a status when the backend omits it', () => {
    const fixture = arrangeWithSubscription({
      hasSubscription: true,
      billingStatus: null,
      storeBlocked: false,
    });

    expect(fixture.nativeElement.textContent).toContain('Status da mensalidade indisponível.');
    expect(fixture.nativeElement.textContent).not.toContain('em dia');
  });

  it('keeps the printer area and in-progress card', () => {
    const fixture = arrangeHappyPath();

    expect(fixture.nativeElement.querySelector('.in-progress-card')).not.toBeNull();
  });

  it('marks the dashboard busy while loading and announces updates', () => {
    const initial = new Subject<StoreAnalytics>();
    const refresh = new Subject<StoreAnalytics>();
    orderServiceMock.getStoreAnalytics
      .mockReturnValueOnce(initial.asObservable())
      .mockReturnValueOnce(refresh.asObservable());
    orderServiceMock.getStoreSales.mockReturnValue(of(salesFixture()));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-busy="true"]')).not.toBeNull();

    initial.next(analyticsFixture());
    initial.complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.loading()).toBe(false);

    fixture.componentInstance.load({ silent: true, resetPage: false });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Atualizando');

    refresh.next(analyticsFixture());
    refresh.complete();
    fixture.detectChanges();
  });
});
