import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SellerSubscriptionBillingStatus } from '../../shared/models/subscription.model';
import { ApiService } from './api.service';
import { SubscriptionService } from './subscription.service';

describe('SubscriptionService', () => {
  let service: SubscriptionService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SubscriptionService, ApiService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(SubscriptionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the current seller subscription', () => {
    service.getMySubscription().subscribe((res) => {
      expect(res.planName).toBe('Plano Pro');
      expect(res.billingStatus).toBe(SellerSubscriptionBillingStatus.Active);
    });

    const req = httpMock.expectOne('/api/subscriptions/my');
    expect(req.request.method).toBe('GET');
    req.flush({ hasSubscription: true, planName: 'Plano Pro', billingStatus: 1, lastChargeStatus: 'paid', storeBlocked: false, regularizationMessage: '' });
  });

  it('loads seller subscription charge history', () => {
    service.listMyCharges().subscribe((res) => {
      expect(res).toHaveLength(1);
      expect(res[0].gatewayChargeId).toBe('charge1');
    });

    const req = httpMock.expectOne('/api/subscriptions/my/charges');
    expect(req.request.method).toBe('GET');
    req.flush([{ gatewayChargeId: 'charge1', gatewayStatus: 'paid', billingStatus: 1, dueDateUtc: '2026-07-29T00:00:00Z', amount: 49.9 }]);
  });
});
