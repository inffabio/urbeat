import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  SKIP_GLOBAL_ERROR_TOAST,
  SKIP_SERVER_ACCESS_OVERLAY,
} from '../interceptors/server-access.context';
import { ApiService } from './api.service';
import { PaymentService } from './payment.service';

describe('PaymentService', () => {
  let service: PaymentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PaymentService, ApiService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(PaymentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('marks an automatic background confirmation with the silent-error context', () => {
    service.getPayment('order1', { background: true }).subscribe();

    const req = httpMock.expectOne('/api/payments/order/order1');
    expect(req.request.method).toBe('GET');
    expect(req.request.context.get(SKIP_SERVER_ACCESS_OVERLAY)).toBe(true);
    expect(req.request.context.get(SKIP_GLOBAL_ERROR_TOAST)).toBe(true);
    req.flush({});
  });

  it('keeps the foreground payment load visible to the global error handler', () => {
    service.getPayment('order1').subscribe();

    const req = httpMock.expectOne('/api/payments/order/order1');
    expect(req.request.context.get(SKIP_SERVER_ACCESS_OVERLAY)).toBe(false);
    expect(req.request.context.get(SKIP_GLOBAL_ERROR_TOAST)).toBe(false);
    req.flush({});
  });

  it('does not tag payment creation as a background poll', () => {
    service.createPayment('order1').subscribe();

    const req = httpMock.expectOne('/api/payments/order');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ orderId: 'order1' });
    expect(req.request.context.get(SKIP_GLOBAL_ERROR_TOAST)).toBe(false);
    req.flush({});
  });
});
