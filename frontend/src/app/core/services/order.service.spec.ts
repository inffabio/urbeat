import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import {
  SKIP_GLOBAL_ERROR_TOAST,
  SKIP_SERVER_ACCESS_OVERLAY,
} from '../interceptors/server-access.context';
import { ApiService } from './api.service';
import { OrderService } from './order.service';

describe('OrderService', () => {
  let service: OrderService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [OrderService, ApiService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(OrderService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should request store report', () => {
    service.getStoreReport('2026-07-29T00:00:00Z', '2026-07-30T00:00:00Z').subscribe((report) => {
      expect(report.totalOrders).toBe(2);
    });

    const req = httpMock.expectOne(
      '/api/orders/store/report?startDateUtc=2026-07-29T00%3A00%3A00Z&endDateUtc=2026-07-30T00%3A00%3A00Z',
    );
    expect(req.request.method).toBe('GET');
    req.flush({
      totalOrders: 2,
      totalRevenue: 100,
      startDateUtc: '2026-07-29T00:00:00Z',
      endDateUtc: '2026-07-30T00:00:00Z',
    });
  });

  it('should confirm delivery', () => {
    service.confirmDelivery('order1').subscribe((order) => {
      expect(order.deliveryConfirmedAtUtc).toBe('2026-07-29T11:00:00Z');
    });

    const req = httpMock.expectOne('/api/orders/order1/delivery-confirmation');
    expect(req.request.method).toBe('POST');
    req.flush({
      id: 'order1',
      code: '123',
      storeId: 'store1',
      fulfillmentType: 1,
      status: OrderStatus.Delivered,
      paymentMethod: 1,
      subtotal: 20,
      deliveryFee: 0,
      total: 20,
      createdAtUtc: '2026-07-29T10:00:00Z',
      deliveryConfirmedAtUtc: '2026-07-29T11:00:00Z',
      items: [],
      history: [],
    });
  });

  it('should update seller order status', () => {
    service.updateStoreOrderStatus('order1', OrderStatus.Preparing, 'Aceito').subscribe((order) => {
      expect(order.status).toBe(OrderStatus.Preparing);
    });

    const req = httpMock.expectOne('/api/orders/order1/status');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ newStatus: OrderStatus.Preparing, notes: 'Aceito' });
    req.flush({
      id: 'order1',
      code: '123',
      storeId: 'store1',
      fulfillmentType: 1,
      status: OrderStatus.Preparing,
      paymentMethod: 1,
      subtotal: 20,
      deliveryFee: 0,
      total: 20,
      createdAtUtc: '2026-07-29T10:00:00Z',
      items: [],
      history: [],
    });
  });

  it('should request seller customer aggregates with pagination and filters', () => {
    service.getStoreCustomers({ page: 2, pageSize: 7, search: 'Cliente', status: 'active', sort: 'totalSpentDesc' }).subscribe((customers) => {
      expect(customers.items[0].name).toBe('Cliente Teste');
      expect(customers.metrics.averageTicket).toBe(48.6);
    });

    const req = httpMock.expectOne('/api/orders/store/customers?page=2&pageSize=7&search=Cliente&status=active&sort=totalSpentDesc');
    expect(req.request.method).toBe('GET');
    req.flush({
      page: 2,
      pageSize: 7,
      totalItems: 8,
      totalPages: 2,
      metrics: {
        totalCustomers: 8,
        activeCustomers: 5,
        recurringCustomers: 3,
        newCustomersThisMonth: 2,
        averageTicket: 48.6,
      },
      items: [{ id: 'customer1', name: 'Cliente Teste', email: 'cliente@teste.com', phone: '11999999999', totalOrders: 2, totalSpent: 80, lastOrderAtUtc: '2026-07-29T10:00:00Z', isActive: true }],
    });
  });

  it('should request seller delivery aggregates', () => {
    service.getStoreDeliveries().subscribe((deliveries) => {
      expect(deliveries[0].addressSummary).toBe('Rua Teste, 10 - Centro');
    });

    const req = httpMock.expectOne('/api/orders/store/deliveries');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'order1', code: '123', customerName: 'Cliente', customerPhoneNumber: '11999999999', addressSummary: 'Rua Teste, 10 - Centro', status: OrderStatus.OnDelivery, total: 40, createdAtUtc: '2026-07-29T10:00:00Z' }]);
  });

  it('marks a background getOrder with the skip-overlay and silent-error contexts', () => {
    service.getOrder('order1', { background: true }).subscribe();

    const req = httpMock.expectOne('/api/orders/order1');
    expect(req.request.method).toBe('GET');
    expect(req.request.context.get(SKIP_SERVER_ACCESS_OVERLAY)).toBe(true);
    expect(req.request.context.get(SKIP_GLOBAL_ERROR_TOAST)).toBe(true);
    req.flush({});
  });

  it('does not set the background contexts for a foreground getOrder', () => {
    service.getOrder('order1').subscribe();

    const req = httpMock.expectOne('/api/orders/order1');
    expect(req.request.context.get(SKIP_SERVER_ACCESS_OVERLAY)).toBe(false);
    expect(req.request.context.get(SKIP_GLOBAL_ERROR_TOAST)).toBe(false);
    req.flush({});
  });

  it('should complete a seller order', () => {
    service.completeOrder('order1').subscribe((order) => {
      expect(order.sellerCompletedAtUtc).toBe('2026-07-29T12:00:00Z');
    });

    const req = httpMock.expectOne('/api/orders/order1/complete');
    expect(req.request.method).toBe('POST');
    req.flush({
      id: 'order1',
      code: '123',
      storeId: 'store1',
      fulfillmentType: 1,
      status: OrderStatus.Delivered,
      paymentMethod: 1,
      subtotal: 20,
      deliveryFee: 0,
      total: 20,
      createdAtUtc: '2026-07-29T10:00:00Z',
      sellerCompletedAtUtc: '2026-07-29T12:00:00Z',
      items: [],
      history: [],
    });
  });

  it('should request store orders with an exclusive end bound', () => {
    service.getStoreOrders({
      pageSize: 100,
      startDateUtc: '2026-10-29T03:00:00.000Z',
      endExclusiveUtc: '2026-10-30T03:00:00Z',
    }).subscribe();

    const req = httpMock.expectOne(
      '/api/orders/store?pageSize=100&startDateUtc=2026-10-29T03%3A00%3A00.000Z&endExclusiveUtc=2026-10-30T03%3A00%3A00Z',
    );
    expect(req.request.method).toBe('GET');
    req.flush({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] });
  });

  it('should request store analytics with period, anchor and week block', () => {
    service.getStoreAnalytics({ period: 'week', anchor: '2026-10-29', weekBlock: 5 }).subscribe((analytics) => {
      expect(analytics.kpis.deliveredOrders).toBe(12);
      expect(analytics.availableYears).toEqual([2026, 2025]);
    });

    const req = httpMock.expectOne('/api/orders/store/analytics?period=week&anchor=2026-10-29&weekBlock=5');
    expect(req.request.method).toBe('GET');
    req.flush({
      period: 'week',
      anchorDate: '2026-10-29',
      weekBlock: 5,
      rangeStartUtc: '2026-10-29T03:00:00Z',
      rangeEndUtcExclusive: '2026-11-01T03:00:00Z',
      timeZone: 'America/Sao_Paulo',
      chartYear: 2026,
      kpis: { deliveredOrders: 12, revenue: 1234.5, deliveryFees: 60, averageTicket: 102.88 },
      products: [],
      payments: [],
      months: [],
      availableYears: [2026, 2025],
    });
  });

  it('should omit the week block outside the week period', () => {
    service.getStoreAnalytics({ period: 'day', anchor: '2026-10-29' }).subscribe();

    const req = httpMock.expectOne('/api/orders/store/analytics?period=day&anchor=2026-10-29');
    req.flush({});
  });

  it('should request store sales with pagination', () => {
    service.getStoreSales({ period: 'month', anchor: '2026-10-01', page: 2, pageSize: 20 }).subscribe((sales) => {
      expect(sales.page).toBe(2);
      expect(sales.totals.salesTotal).toBe(100);
    });

    const req = httpMock.expectOne('/api/orders/store/sales?period=month&anchor=2026-10-01&page=2&pageSize=20');
    expect(req.request.method).toBe('GET');
    req.flush({
      period: 'month',
      anchorDate: '2026-10-01',
      weekBlock: null,
      rangeStartUtc: '2026-10-01T03:00:00Z',
      rangeEndUtcExclusive: '2026-11-01T03:00:00Z',
      timeZone: 'America/Sao_Paulo',
      page: 2,
      pageSize: 20,
      totalItems: 21,
      totalPages: 2,
      totals: { salesTotal: 100, deliveryFees: 5 },
      sales: [],
    });
  });

  it('should omit the week block from store sales outside the week period', () => {
    service.getStoreSales({ period: 'year', anchor: '2026-01-01', page: 1, pageSize: 20 }).subscribe();

    const req = httpMock.expectOne('/api/orders/store/sales?period=year&anchor=2026-01-01&page=1&pageSize=20');
    req.flush({});
  });
});
