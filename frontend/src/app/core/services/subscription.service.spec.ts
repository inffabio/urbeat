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

  it('loads active subscription plans', () => {
    service.listPlans().subscribe((plans) => {
      expect(plans).toHaveLength(1);
      expect(plans[0].name).toBe('Plano Pro');
      expect(plans[0].trialDays).toBe(15);
      expect(plans[0].benefits).toEqual(['Cardápio digital']);
    });

    const req = httpMock.expectOne('/api/subscriptions/plans');
    expect(req.request.method).toBe('GET');
    req.flush([{
      id: 'plan-1',
      name: 'Plano Pro',
      amount: 49.9,
      description: 'Plano completo',
      isActive: true,
      trialDays: 15,
      benefits: ['Cardápio digital'],
    }]);
  });

  it('contracts a plan sending only store and plan identifiers', () => {
    service.contract({ storeId: 'store-1', planId: 'plan-1' }).subscribe((res) => {
      expect(res.planName).toBe('Plano Pro');
      expect(res.nextBillingDateUtc).toBe('2026-08-16T00:00:00Z');
    });

    const req = httpMock.expectOne('/api/subscriptions/contract');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ storeId: 'store-1', planId: 'plan-1' });
    req.flush({
      subscriptionId: 'subscription-1',
      storeId: 'store-1',
      sellerUserId: 'seller-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      status: 1,
      startDateUtc: '2026-08-01T00:00:00Z',
      nextBillingDateUtc: '2026-08-16T00:00:00Z',
    });
  });
});
