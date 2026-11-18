import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SubscriptionService } from '../../core/services/subscription.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import {
  SellerSubscriptionBillingStatus,
  SellerSubscriptionPlan,
  SubscriptionBillingPeriod,
} from '../../shared/models/subscription.model';
import { SellerSubscriptionPageComponent } from './seller-subscription-page.component';

function plan(overrides: Partial<SellerSubscriptionPlan>): SellerSubscriptionPlan {
  return {
    id: 'plan-base',
    name: 'Plano Base',
    amount: 19.9,
    monthlyAmount: 19.9,
    annualAmount: 214.92,
    annualDiscountPercent: 10,
    monthlyEnabled: true,
    annualEnabled: true,
    description: 'Plano base',
    isActive: true,
    trialDays: 15,
    benefits: ['Cardápio digital'],
    level: 0,
    ...overrides,
  };
}

const BASE_PLAN = plan({ id: 'plan-base', name: 'Começar', level: 0 });
const GROWTH_PLAN = plan({ id: 'plan-growth', name: 'Crescer', level: 1, monthlyAmount: 39.9, amount: 39.9, annualAmount: 407 });
const PREMIUM_PLAN = plan({ id: 'plan-premium', name: 'Profissional', level: 2, monthlyAmount: 79.9, amount: 79.9, annualAmount: 767.04 });

describe('SellerSubscriptionPageComponent', () => {
  let subscriptionServiceMock: {
    getMySubscription: jest.Mock;
    listMyCharges: jest.Mock;
    listPlans: jest.Mock;
    contract: jest.Mock;
  };

  beforeEach(async () => {
    subscriptionServiceMock = {
      getMySubscription: jest.fn(),
      listMyCharges: jest.fn().mockReturnValue(of([])),
      listPlans: jest.fn().mockReturnValue(of([BASE_PLAN])),
      contract: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerSubscriptionPageComponent],
      providers: [{ provide: SubscriptionService, useValue: subscriptionServiceMock }],
    }).compileComponents();
  });

  function create() {
    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the contracted plan, period, trial, snapshot amount and pending boleto state', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-base',
      planName: 'Começar',
      planDescription: 'Plano de entrada',
      planAmount: 214.92,
      billingPeriod: SubscriptionBillingPeriod.Annual,
      planLevel: 0,
      trialDays: 15,
      trialEndsAtUtc: '2026-12-03T00:00:00Z',
      isTrialActive: true,
      hasGatewaySubscription: false,
      hasPendingGatewayCharge: true,
      pendingChargeAmount: 214.92,
      pendingChargeDueDateUtc: '2026-12-03T00:00:00Z',
      pendingChargeStatus: 'Boleto aguardando integração',
      billingStatus: SellerSubscriptionBillingStatus.Active,
      nextDueDateUtc: '2026-12-03T00:00:00Z',
      lastChargeStatus: 'Periodo gratuito',
      storeBlocked: false,
      regularizationMessage: '',
    }));

    const fixture = create();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Começar');
    expect(text).toContain('Periodicidade');
    expect(text).toContain('Anual');
    expect(text).toContain('Período grátis');
    expect(text).toContain('214,92');
    expect(text).toContain('Boleto aguardando integração');
    expect(text).toContain(formatSaoPauloDate('2026-12-03T00:00:00Z'));
    expect(text).not.toContain('Pagar');
  });

  it('does not offer plan administration or a switch when there is only one active plan', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-base',
      planName: 'Começar',
      planAmount: 19.9,
      billingPeriod: SubscriptionBillingPeriod.Monthly,
      planLevel: 0,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: false,
      storeBlocked: false,
      regularizationMessage: '',
    }));

    const fixture = create();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('único plano ativo');
    expect(fixture.nativeElement.querySelectorAll('.plan-radio')).toHaveLength(0);
    expect(text).not.toContain('Solicitar upgrade');
    expect(text).not.toContain('Editar plano');
    expect(text).not.toContain('Desativar');
  });

  it('offers only superior plans when more than one plan is active', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-base',
      planName: 'Começar',
      planAmount: 19.9,
      billingPeriod: SubscriptionBillingPeriod.Monthly,
      planLevel: 0,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: false,
      hasPendingGatewayCharge: true,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listPlans.mockReturnValue(of([BASE_PLAN, GROWTH_PLAN, PREMIUM_PLAN]));

    const fixture = create();
    const text = fixture.nativeElement.textContent as string;
    const planNames = Array.from(fixture.nativeElement.querySelectorAll('.plan-name') as NodeListOf<HTMLElement>)
      .map((element) => element.textContent?.trim());

    expect(fixture.nativeElement.querySelectorAll('.plan-radio')).toHaveLength(2);
    expect(planNames).toEqual(['Crescer', 'Profissional']);
    expect(text).toContain('somente upgrade');
  });

  it('offers no upgrade when the seller is already at the highest level', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-premium',
      planName: 'Profissional',
      planAmount: 79.9,
      billingPeriod: SubscriptionBillingPeriod.Monthly,
      planLevel: 2,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: false,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listPlans.mockReturnValue(of([BASE_PLAN, GROWTH_PLAN, PREMIUM_PLAN]));

    const fixture = create();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('nível mais alto');
    expect(text).not.toContain('Solicitar upgrade');
    expect(fixture.nativeElement.querySelectorAll('.plan-radio')).toHaveLength(0);
  });

  it('selects a superior plan and its annual period before confirming', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-base',
      planName: 'Começar',
      planAmount: 19.9,
      billingPeriod: SubscriptionBillingPeriod.Monthly,
      planLevel: 0,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: false,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listPlans.mockReturnValue(of([BASE_PLAN, GROWTH_PLAN]));
    subscriptionServiceMock.contract.mockReturnValue(of({
      subscriptionId: 'sub-1',
      storeId: 'store-1',
      sellerUserId: 'seller-1',
      planName: 'Crescer',
      planDescription: '',
      planAmount: 407,
      billingPeriod: SubscriptionBillingPeriod.Annual,
      status: SellerSubscriptionBillingStatus.Active,
      startDateUtc: '2026-11-18T00:00:00Z',
      trialEndsAtUtc: null,
      nextBillingDateUtc: '2026-11-18T00:00:00Z',
      awaitingGateway: true,
    }));

    const fixture = create();
    const growthRadio = fixture.nativeElement.querySelector('input[name="subscription-plan"]') as HTMLInputElement;
    growthRadio.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const periodRadios = fixture.nativeElement.querySelectorAll('input[name="period-plan-growth"]') as NodeListOf<HTMLInputElement>;
    expect(periodRadios).toHaveLength(2);
    periodRadios[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const upgradeButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Solicitar upgrade'));
    upgradeButton!.click();
    fixture.detectChanges();

    expect(subscriptionServiceMock.contract).not.toHaveBeenCalled();
    const confirm = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.trim() === 'Confirmar');
    confirm!.click();
    fixture.detectChanges();

    expect(subscriptionServiceMock.contract).toHaveBeenCalledWith({
      storeId: 'store-1',
      planId: 'plan-growth',
      billingPeriod: SubscriptionBillingPeriod.Annual,
    });
    expect(fixture.nativeElement.textContent).toContain('aguardando a integração de gateway');
  });

  it('renders an awaiting-gateway charge row without payment actions', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-base',
      planName: 'Começar',
      planAmount: 19.9,
      billingPeriod: SubscriptionBillingPeriod.Monthly,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: false,
      hasPendingGatewayCharge: true,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([
      {
        gatewayChargeId: 'stub:1',
        gatewayStatus: 'awaiting_gateway',
        billingStatus: SellerSubscriptionBillingStatus.Active,
        dueDateUtc: '2026-12-03T00:00:00Z',
        billingPeriodStartUtc: '2026-12-03T00:00:00Z',
        billingPeriodEndUtc: '2027-01-03T00:00:00Z',
        amount: 19.9,
        isGatewayPending: true,
      },
    ]));

    const fixture = create();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Boleto aguardando integração');
    expect(fixture.nativeElement.querySelector('.btn-pagar')).toBeNull();
    expect(text).not.toContain('Pagar');
  });

  it('renders overdue and blocked states with the support action', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-base',
      planName: 'Começar',
      planAmount: 19.9,
      billingPeriod: SubscriptionBillingPeriod.Monthly,
      billingStatus: SellerSubscriptionBillingStatus.Blocked,
      hasGatewaySubscription: false,
      hasPendingGatewayCharge: true,
      storeBlocked: true,
      regularizationMessage: 'Regularize sua mensalidade para reabrir a loja.',
    }));

    const fixture = create();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Loja bloqueada');
    expect(text).toContain('Falar com o suporte');
  });

  it('reloads authoritative state when the upgrade is rejected', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planId: 'plan-base',
      planName: 'Começar',
      planAmount: 19.9,
      billingPeriod: SubscriptionBillingPeriod.Monthly,
      planLevel: 0,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: false,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listPlans.mockReturnValue(of([BASE_PLAN, GROWTH_PLAN]));
    subscriptionServiceMock.contract.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));

    const fixture = create();
    const growthRadio = fixture.nativeElement.querySelector('.plan-radio') as HTMLInputElement;
    growthRadio.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const upgradeButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Solicitar upgrade'));
    upgradeButton!.click();
    fixture.detectChanges();

    const confirm = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.trim() === 'Confirmar');
    confirm!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Downgrade não é permitido');
    expect(subscriptionServiceMock.getMySubscription).toHaveBeenCalledTimes(2);
  });

  it('shows retry state when subscription data fails to load', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(throwError(() => new Error('network')));

    const fixture = create();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar a mensalidade');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-error button').textContent).toContain('Tentar novamente');
  });
});
