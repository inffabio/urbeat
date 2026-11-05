import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SubscriptionService } from '../../core/services/subscription.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import {
  SellerSubscriptionBillingStatus,
  SellerSubscriptionPlan,
} from '../../shared/models/subscription.model';
import { SellerSubscriptionPageComponent } from './seller-subscription-page.component';

const ACTIVE_PLAN: SellerSubscriptionPlan = {
  id: 'plan-active',
  name: 'Plano Pro',
  amount: 49.9,
  description: 'Plano completo',
  isActive: true,
  trialDays: 15,
  benefits: ['Cardápio digital', 'Relatórios'],
};

const CONTRACT_RESPONSE = {
  subscriptionId: 'subscription-1',
  storeId: 'store-1',
  sellerUserId: 'seller-1',
  planName: 'Plano Pro',
  planAmount: 49.9,
  status: SellerSubscriptionBillingStatus.Active,
  startDateUtc: '2026-08-01T00:00:00Z',
  nextBillingDateUtc: '2026-08-16T00:00:00Z',
};

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
      listMyCharges: jest.fn(),
      listPlans: jest.fn().mockReturnValue(of([])),
      contract: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerSubscriptionPageComponent],
      providers: [{ provide: SubscriptionService, useValue: subscriptionServiceMock }],
    }).compileComponents();
  });

  it('renders an active contracted subscription and charge history', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      hasGatewaySubscription: true,
      isTrialActive: false,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      nextDueDateUtc: '2026-08-10T00:00:00Z',
      lastChargeStatus: 'Pago',
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([
      { gatewayChargeId: 'charge1', gatewayStatus: 'paid', billingStatus: SellerSubscriptionBillingStatus.Active, dueDateUtc: '2026-07-10T00:00:00Z', billingPeriodStartUtc: '2026-07-10T00:00:00Z', billingPeriodEndUtc: '2026-08-10T00:00:00Z', paidAtUtc: '2026-07-09T12:00:00Z', amount: 49.9 },
    ]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(subscriptionServiceMock.getMySubscription).toHaveBeenCalled();
    expect(subscriptionServiceMock.listMyCharges).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Mensalidades');
    expect(fixture.nativeElement.textContent).toContain('Plano Pro');
    expect(fixture.nativeElement.textContent).toContain('Sua assinatura está em dia');
    expect(fixture.nativeElement.textContent).toContain('R$');
    expect(fixture.nativeElement.textContent).toContain('Pago em');
  });

  it('presents an active subscription close to due date as vencendo', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      hasGatewaySubscription: true,
      isTrialActive: false,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      nextDueDateUtc: new Date(Date.now() + 2 * 24 * 60 * 60 * 1000).toISOString(),
      lastChargeStatus: 'Pago',
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Assinatura vencendo');
    expect(fixture.nativeElement.textContent).toContain('Sua mensalidade vence em');
  });

  it('renders the trial window and the first charge amount', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      nextChargeAmount: 39.9,
      trialDays: 15,
      trialEndsAtUtc: '2026-08-16T00:00:00Z',
      isTrialActive: true,
      hasGatewaySubscription: false,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      nextDueDateUtc: '2026-08-16T00:00:00Z',
      lastChargeStatus: 'Periodo gratuito',
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Período grátis ativo');
    expect(fixture.nativeElement.textContent).toContain(formatSaoPauloDate('2026-08-16T00:00:00Z'));
    expect(fixture.nativeElement.textContent).toContain('39,90');
    expect(fixture.nativeElement.textContent).toContain('Fim do período grátis');
  });

  it('never presents a zero-day plan as a free trial', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      trialDays: 0,
      trialEndsAtUtc: null,
      isTrialActive: false,
      hasGatewaySubscription: false,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      nextDueDateUtc: '2026-09-01T00:00:00Z',
      lastChargeStatus: 'Nao contratado',
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));
    subscriptionServiceMock.listPlans.mockReturnValue(of([{ ...ACTIVE_PLAN, trialDays: 0 }]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('0 dias grátis');
    expect(fixture.nativeElement.textContent).toContain('Primeira cobrança imediata');
    expect(fixture.nativeElement.textContent).toContain('Não se aplica');
    expect(fixture.nativeElement.textContent).toContain('Sem assinatura contratada');
  });

  it('renders the billing period using persisted start and end dates', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      planName: 'Plano Pro',
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: true,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([
      { gatewayChargeId: 'charge1', gatewayStatus: 'paid', billingStatus: SellerSubscriptionBillingStatus.Active, dueDateUtc: '2026-08-10T00:00:00Z', billingPeriodStartUtc: '2026-08-10T00:00:00Z', billingPeriodEndUtc: '2026-09-10T00:00:00Z', amount: 49.9 },
    ]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    const expected = `${formatSaoPauloDate('2026-08-10T00:00:00Z')} - ${formatSaoPauloDate('2026-09-10T00:00:00Z')}`;
    expect(fixture.nativeElement.textContent).toContain(expected);
  });

  it('renders the period fallback for legacy charges without a period', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      planName: 'Plano Pro',
      billingStatus: SellerSubscriptionBillingStatus.Active,
      hasGatewaySubscription: true,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([
      { gatewayChargeId: 'charge1', gatewayStatus: 'paid', billingStatus: SellerSubscriptionBillingStatus.Active, dueDateUtc: '2026-08-10T00:00:00Z', amount: 49.9 },
    ]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Sem período');
  });

  it('replaces the disabled Pagar button with an honest regularization action', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      planName: 'Plano Pro',
      billingStatus: SellerSubscriptionBillingStatus.Overdue,
      hasGatewaySubscription: true,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([
      { gatewayChargeId: 'charge1', gatewayStatus: 'overdue', billingStatus: SellerSubscriptionBillingStatus.Overdue, dueDateUtc: '2026-08-10T00:00:00Z', billingPeriodStartUtc: '2026-08-10T00:00:00Z', billingPeriodEndUtc: '2026-09-10T00:00:00Z', amount: 49.9 },
    ]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.btn-pagar')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Regularização em breve');
    expect(fixture.nativeElement.textContent).toContain('Falar com o suporte');
    expect(fixture.nativeElement.textContent).not.toContain('Pagar');
  });

  it('renders the blocked state with the support action', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      planName: 'Plano Pro',
      billingStatus: SellerSubscriptionBillingStatus.Blocked,
      hasGatewaySubscription: true,
      lastChargeStatus: 'overdue',
      storeBlocked: true,
      regularizationMessage: 'Regularize sua mensalidade para reabrir a loja.',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Loja bloqueada');
    expect(fixture.nativeElement.textContent).toContain('Falar com o suporte');
  });

  it('lists active plans and contracts the selected plan only after confirmation', () => {
    const secondPlan: SellerSubscriptionPlan = {
      id: 'plan-growth',
      name: 'Plano Crescer',
      amount: 79.9,
      description: 'Para crescer',
      isActive: true,
      trialDays: 15,
      benefits: [],
    };

    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      isTrialActive: true,
      hasGatewaySubscription: false,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      nextDueDateUtc: '2026-08-16T00:00:00Z',
      trialEndsAtUtc: '2026-08-16T00:00:00Z',
      lastChargeStatus: 'Periodo gratuito',
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));
    subscriptionServiceMock.listPlans.mockReturnValue(of([ACTIVE_PLAN, secondPlan]));
    subscriptionServiceMock.contract.mockReturnValue(of({ ...CONTRACT_RESPONSE, planName: secondPlan.name, planAmount: secondPlan.amount }));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.plan-card')).toHaveLength(2);

    const radios = fixture.nativeElement.querySelectorAll('.plan-radio') as NodeListOf<HTMLInputElement>;
    radios[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const openButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Contratar plano selecionado'));
    expect(openButton).toBeDefined();
    openButton!.click();
    fixture.detectChanges();

    expect(subscriptionServiceMock.contract).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Confirmar contratação');

    const confirmButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Confirmar contratação'));
    expect(confirmButton).toBeDefined();
    confirmButton!.click();
    fixture.detectChanges();

    expect(subscriptionServiceMock.contract).toHaveBeenCalledWith({ storeId: 'store-1', planId: 'plan-growth' });
    expect(fixture.nativeElement.textContent).toContain('Plano Crescer contratado');
    expect(subscriptionServiceMock.getMySubscription).toHaveBeenCalledTimes(2);
    expect(subscriptionServiceMock.listMyCharges).toHaveBeenCalledTimes(2);
    expect(subscriptionServiceMock.listPlans).toHaveBeenCalledTimes(2);
  });

  it('shows a clear message and reloads when the store is already contracted', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      isTrialActive: true,
      hasGatewaySubscription: false,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));
    subscriptionServiceMock.listPlans.mockReturnValue(of([ACTIVE_PLAN]));
    subscriptionServiceMock.contract.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    const radio = fixture.nativeElement.querySelector('.plan-radio') as HTMLInputElement;
    radio.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const openButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Contratar plano selecionado'));
    openButton!.click();
    fixture.detectChanges();

    const confirmButton = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Confirmar contratação'));
    confirmButton!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('já possui uma assinatura contratada');
    expect(subscriptionServiceMock.getMySubscription).toHaveBeenCalledTimes(2);
  });

  it('does not offer plan changes when a gateway contract already exists', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: true,
      storeId: 'store-1',
      planName: 'Plano Pro',
      planAmount: 49.9,
      hasGatewaySubscription: true,
      billingStatus: SellerSubscriptionBillingStatus.Active,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));
    subscriptionServiceMock.listPlans.mockReturnValue(of([ACTIVE_PLAN]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('A troca de plano estará disponível em breve');
    const radio = fixture.nativeElement.querySelector('.plan-radio') as HTMLInputElement;
    expect(radio.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('Contratar plano selecionado');
  });

  it('offers a retry when plans fail to load', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(of({
      hasSubscription: false,
      billingStatus: null,
      storeBlocked: false,
      regularizationMessage: '',
    }));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));
    subscriptionServiceMock.listPlans.mockReturnValue(throwError(() => new Error('down')));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar os planos');
    const retry = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find((button) => button.textContent?.includes('Tentar novamente'));
    expect(retry).toBeDefined();
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

    expect(fixture.nativeElement.textContent).toContain('Nenhuma cobrança registrada ainda.');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-empty')).not.toBeNull();
  });

  it('shows retry state when subscription data fails to load', () => {
    subscriptionServiceMock.getMySubscription.mockReturnValue(throwError(() => new Error('network')));
    subscriptionServiceMock.listMyCharges.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerSubscriptionPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar a mensalidade');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-error button').textContent).toContain('Tentar novamente');
  });
});
