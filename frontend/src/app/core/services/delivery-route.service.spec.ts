import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';
import { DeliveryRouteService } from './delivery-route.service';

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

  it('loads and confirms an individual public delivery link', () => {
    service.getPublicIndividualDelivery('token/1').subscribe();
    const load = httpMock.expectOne('/api/public/deliveries/token%2F1');
    expect(load.request.method).toBe('GET');
    load.flush({ storeName: 'Loja', driverName: 'Ana', stops: [] });

    service.confirmPublicIndividualDelivery('token/1', '1234').subscribe();
    const confirm = httpMock.expectOne('/api/public/deliveries/token%2F1/deliver');
    expect(confirm.request.method).toBe('POST');
    expect(confirm.request.body).toEqual({ pin: '1234' });
    confirm.flush({ succeeded: true });
  });
});
