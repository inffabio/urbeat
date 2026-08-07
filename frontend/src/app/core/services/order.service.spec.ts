import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrderStatus } from '../../shared/enums/order-status.enum';
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
});
