import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SubscriptionService } from '../../core/services/subscription.service';
import { SellerSubscriptionBillingStatus } from '../../shared/models/subscription.model';
import { SellerSubscriptionPageComponent } from './seller-subscription-page.component';

describe('SellerSubscriptionPageComponent', () => {
  let subscriptionServiceMock: { getMySubscription: jest.Mock; listMyCharges: jest.Mock };

  beforeEach(async () => {
    subscriptionServiceMock = {
      getMySubscription: jest.fn(),
      listMyCharges: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerSubscriptionPageComponent],
      providers: [{ provide: SubscriptionService, useValue: subscriptionServiceMock }],
    }).compileComponents();
  });

  it('renders subscription status and charge history', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      planName: 'Plano Pro',
      planAmount: 49.9,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      nextDueDateUtc: '2026-08-10T00:00:00Z',
      lastChargeStatus: 'paid',
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([
      { gatewayChargeId: 'charge1', gatewayStatus: 'paid', billingStatus: SellerSubscriptionBillingStatus.Active, dueDateUtc: '2026-07-10T00:00:00Z', paidAtUtc: '2026-07-09T12:00:00Z', amount: 49.9 },
    ]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(subscriptionServiceMock.getMySubscription).toHaveBeenCalled();
    expect(subscriptionServiceMock.listMyCharges).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Mensalidades');
    expect(fixture.nativeElement.textContent).toContain('Plano Pro');
    expect(fixture.nativeElement.textContent).toContain('está em dia, obrigado');
    expect(fixture.nativeElement.textContent).toContain('R$');
    expect(fixture.nativeElement.textContent).toContain('charge1');
  });

  it('renders regularization message when store is blocked', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      planName: 'Plano Pro',
      billingStatus: SellerSubscriptionBillingStatus.Blocked,
      lastChargeStatus: 'overdue',
      storeBlocked: true,
      regularizationMessage: 'Regularize sua mensalidade para reabrir a loja.',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Regularize sua mensalidade');
  });

  it('renders the shared empty state when there are no charges yet', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: false,
      billingStatus: null,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhuma cobranca registrada ainda.');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-empty')).not.toBeNull();
  });

  it('shows retry state when subscription data fails to load', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(throwError(() => new Error('network')));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nao foi possivel carregar a mensalidade');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-error button').textContent).toContain('Tentar novamente');
  });
});
