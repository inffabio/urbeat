import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { DeliveryRouteService } from './delivery-route.service';
import { authInterceptor } from '../interceptors/auth.interceptor';

describe('DeliveryRouteService', () => {
  let service: DeliveryRouteService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DeliveryRouteService, ApiService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(DeliveryRouteService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('shares an individual delivery without creating a route', () => {
    service.shareIndividualDelivery('order-1').subscribe((result) => {
      expect(result.shareUrl).toContain('/entrega/token');
    });

    const request = httpMock.expectOne('/api/orders/store/order-1/delivery-link');
    expect(request.request.method).toBe('POST');
    request.flush({ shareUrl: 'https://urbeat.test/entrega/token' });
  });

  it('sends a batch of orders and returns one share link per motoboy', () => {
    service.sendDeliveryRoutes(['order-1', 'order-2'], true).subscribe((result) => {
      expect(result.groups).toHaveLength(1);
      expect(result.groups[0].shareUrl).toContain('/rota/token');
    });

    const request = httpMock.expectOne('/api/delivery-routes/store/send');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      orderIds: ['order-1', 'order-2'],
      physicalConfirmation: true,
    });
    request.flush({
      groups: [{
        shareUrl: 'https://urbeat.test/rota/token',
        driverName: 'Ana',
        orderCount: 2,
        totalStops: 2,
      }],
    });
  });

  it('loads and confirms an individual public delivery link', () => {
    service.getPublicIndividualDelivery('token/1').subscribe();
    const load = httpMock.expectOne('/api/public/deliveries/token%2F1');
    expect(load.request.method).toBe('GET');
    load.flush({ storeName: 'Loja', driverName: 'Ana', stops: [] });

    service.confirmPublicIndividualDelivery('token/1', '1234', true, 30).subscribe();
    const confirm = httpMock.expectOne('/api/public/deliveries/token%2F1/deliver');
    expect(confirm.request.method).toBe('POST');
    expect(confirm.request.body).toEqual({ pin: '1234', handoverConfirmed: true, cashReceivedAmount: 30 });
    confirm.flush({ succeeded: true });
  });
});

describe('DeliveryRouteService public links', () => {
  let service: DeliveryRouteService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DeliveryRouteService,
        ApiService,
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { getToken: jest.fn().mockReturnValue('seller-token-in-this-tab') },
        },
      ],
    });

    service = TestBed.inject(DeliveryRouteService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('never adds a bearer token to public route or delivery links', () => {
    service.getPublicRoute('route-token').subscribe();
    const route = httpMock.expectOne('/api/public/delivery-routes/route-token');
    expect(route.request.headers.has('Authorization')).toBe(false);
    route.flush({ storeName: 'Loja', driverName: 'Ana', stops: [] });

    service.getPublicIndividualDelivery('delivery-token').subscribe();
    const delivery = httpMock.expectOne('/api/public/deliveries/delivery-token');
    expect(delivery.request.headers.has('Authorization')).toBe(false);
    delivery.flush({ storeName: 'Loja', driverName: 'Ana', stops: [] });

    service.confirmPublicDelivery('route-token', 1, '1234', true, 20).subscribe();
    const confirm = httpMock.expectOne('/api/public/delivery-routes/route-token/stops/1/deliver');
    expect(confirm.request.headers.has('Authorization')).toBe(false);
    confirm.flush({ succeeded: true });

    service.confirmPublicIndividualDelivery('delivery-token', '1234', true, 20).subscribe();
    const individualConfirm = httpMock.expectOne('/api/public/deliveries/delivery-token/deliver');
    expect(individualConfirm.request.headers.has('Authorization')).toBe(false);
    individualConfirm.flush({ succeeded: true });
  });

  it('keeps adding a bearer token to seller delivery management requests', () => {
    service.listStoreRoutes().subscribe();
    const list = httpMock.expectOne('/api/delivery-routes/store');
    expect(list.request.headers.get('Authorization')).toBe('Bearer seller-token-in-this-tab');
    list.flush([]);
  });
});
