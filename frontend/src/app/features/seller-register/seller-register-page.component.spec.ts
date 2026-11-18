import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { PlanOfferService } from '../../core/services/plan-offer.service';
import { ToastService } from '../../core/services/toast.service';
import { PublicPlanOfferItem } from '../../shared/models/plan-offer.model';
import { SubscriptionBillingPeriod } from '../../shared/models/subscription.model';
import { SellerRegisterPageComponent } from './seller-register-page.component';

const PLAN_BASE: PublicPlanOfferItem = {
  id: 'plan-base',
  name: 'Começar',
  amount: 19.9,
  monthlyAmount: 19.9,
  annualAmount: 214.92,
  annualDiscountPercent: 10,
  monthlyEnabled: true,
  annualEnabled: true,
  description: 'Plano de entrada',
  trialDays: 15,
  benefits: [],
  level: 0,
};

const PLAN_GROWTH: PublicPlanOfferItem = {
  ...PLAN_BASE,
  id: 'plan-growth',
  name: 'Crescer',
  amount: 39.9,
  monthlyAmount: 39.9,
  annualAmount: 407,
  annualDiscountPercent: 15,
  level: 1,
};

describe('SellerRegisterPageComponent', () => {
  let fixture: ComponentFixture<SellerRegisterPageComponent>;
  let component: SellerRegisterPageComponent;
  let authServiceMock: {
    registerSeller: jest.Mock;
    logout: jest.Mock;
    isAuthenticated: jest.Mock;
  };
  let planOfferMock: { getOffer: jest.Mock };
  let routerMock: { navigate: jest.Mock };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock; showWarning: jest.Mock };
  let queryParams: Record<string, string>;

  const registerResponse = {
    succeeded: true,
    userId: 'user-1',
    emailConfirmationPending: false,
  };

  beforeEach(async () => {
    authServiceMock = {
      registerSeller: jest.fn(),
      logout: jest.fn(),
      isAuthenticated: jest.fn().mockReturnValue(false),
    };
    planOfferMock = {
      getOffer: jest.fn().mockReturnValue(of({ plans: [PLAN_BASE, PLAN_GROWTH] })),
    };
    routerMock = { navigate: jest.fn() };
    toastMock = {
      showSuccess: jest.fn(),
      showError: jest.fn(),
      showWarning: jest.fn(),
    };
    queryParams = {};

    await TestBed.configureTestingModule({
      imports: [SellerRegisterPageComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: PlanOfferService, useValue: planOfferMock },
        { provide: Router, useValue: routerMock },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: {
                get: (key: string) => queryParams[key] ?? null,
              },
            },
            root: null,
            firstChild: null,
            children: [],
            pathFromRoot: [],
          },
        },
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SellerRegisterPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function fillValidForm(): void {
    component.fullName.set('Contratante Teste');
    component.document.set('');
    component.whatsapp.set('(21) 99999-9999');
    component.email.set('novo@vendedor.com');
    component.password.set('Senha@123');
    component.confirmPassword.set('Senha@123');
    if (!component.selectedPlan()) {
      component.selectPlan(PLAN_BASE);
    }
  }

  function submit(response: typeof registerResponse & {
    rolePromotionPending?: boolean;
    roleAddedToExistingAccount?: boolean;
  }): void {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(of(response));
    component.submit();
  }

  it('keeps the signed-in customer session when adding a seller access', () => {
    authServiceMock.isAuthenticated.mockReturnValue(true);

    submit(registerResponse);

    expect(authServiceMock.logout).not.toHaveBeenCalled();
  });

  it('never sends an existing account password', () => {
    submit(registerResponse);

    const request = authServiceMock.registerSeller.mock.calls[0][0];
    expect(request).not.toHaveProperty('currentPassword');
  });

  it('does not render the current-password field nor keep its state', () => {
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;
    expect(html.querySelector('#currentPassword')).toBeNull();
    expect(html.textContent).not.toContain('Já tem conta urbeat?');
    expect((component as unknown as Record<string, unknown>)['currentPassword']).toBeUndefined();
    expect((component as unknown as Record<string, unknown>)['showCurrentPassword']).toBeUndefined();
  });

  it('sends the document without relying on untyped casts', () => {
    fillValidForm();
    component.document.set('123.456.789-09');
    authServiceMock.registerSeller.mockReturnValue(of(registerResponse));

    component.submit();

    expect(authServiceMock.registerSeller).toHaveBeenCalledWith(
      expect.objectContaining({ document: '12345678909' }),
    );
  });

  it('explains that the customer password was preserved after promoting an existing account', () => {
    submit({ ...registerResponse, roleAddedToExistingAccount: true, emailConfirmationPending: false });

    expect(toastMock.showSuccess).toHaveBeenCalledWith(
      'Acesso lojista criado! Sua senha de cliente continua a mesma.',
    );
  });

  it('uses a generic conflict message that never asks for an existing password', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(throwError(() => ({ status: 409 })));

    component.submit();

    expect(toastMock.showError).toHaveBeenCalledWith(
      'Não foi possível adicionar o acesso lojista com este e-mail.',
    );
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('shows the pending promotion message and keeps the session when e-mail ownership must be confirmed', () => {
    authServiceMock.isAuthenticated.mockReturnValue(true);
    submit({ ...registerResponse, rolePromotionPending: true, emailConfirmationPending: false });

    expect(toastMock.showSuccess).toHaveBeenCalledWith(
      'Se este e-mail já pertence a uma conta urbeat, enviamos um link para confirmar o novo acesso de lojista. Sua senha atual não foi alterada.',
    );
    expect(authServiceMock.logout).not.toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com', promotionPending: true, role: 'Seller' } },
    );
  });

  it('navigates to /login-vendedor when the account is immediately activated', () => {
    submit({ ...registerResponse, emailConfirmationPending: false });

    expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
    expect(routerMock.navigate).not.toHaveBeenCalledWith(['/login']);
  });

  it('navigates to the email confirmation page when confirmation is pending', () => {
    submit({ ...registerResponse, emailConfirmationPending: true });

    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com', userId: 'user-1' } },
    );
  });

  it('allows registering a seller whose contractor name matches an existing seller', () => {
    fillValidForm();
    component.fullName.set('Contratante Teste');
    authServiceMock.registerSeller.mockReturnValue(of(registerResponse));

    component.submit();

    expect(authServiceMock.registerSeller).toHaveBeenCalledWith(
      expect.objectContaining({ fullName: 'Contratante Teste' }),
    );
    expect(component.errors()['fullName']).toBeUndefined();
    expect(toastMock.showError).not.toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(['/login-vendedor']);
  });

  it('preserves document already registered error handling', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(
      throwError(() => ({
        status: 400,
        error: { documentAlreadyRegistered: true, emailConfirmationPending: false },
      })),
    );

    component.submit();

    expect(toastMock.showError).toHaveBeenCalledWith('CPF já cadastrado.');
  });

  it('navigates to email confirmation when the document is registered and confirmation is pending', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(
      throwError(() => ({
        status: 409,
        error: {
          documentAlreadyRegistered: true,
          emailConfirmationPending: true,
          userId: 'existing-user-9',
        },
      })),
    );

    component.submit();

    expect(toastMock.showSuccess).toHaveBeenCalledWith(
      'CPF já cadastrado. Um novo link de confirmação foi enviado para o seu e-mail.',
    );
    expect(toastMock.showError).not.toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com', userId: 'existing-user-9' } },
    );
  });

  it('navigates to email confirmation without a userId when the document response omits it', () => {
    fillValidForm();
    authServiceMock.registerSeller.mockReturnValue(
      throwError(() => ({
        status: 409,
        error: {
          documentAlreadyRegistered: true,
          emailConfirmationPending: true,
        },
      })),
    );

    component.submit();

    expect(routerMock.navigate).toHaveBeenCalledWith(
      ['/confirmacao-email'],
      { queryParams: { email: 'novo@vendedor.com' } },
    );
  });

  it('applies planId and billingPeriod from the landing query and shows the plan summary', () => {
    queryParams = { planId: 'plan-growth', billingPeriod: 'annual' };
    TestBed.resetTestingModule();

    // Rebuild with query params available during ngOnInit.
    const localFixture = TestBed.configureTestingModule({
      imports: [SellerRegisterPageComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: PlanOfferService, useValue: planOfferMock },
        { provide: Router, useValue: routerMock },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: { get: (key: string) => queryParams[key] ?? null } },
            root: null,
            firstChild: null,
            children: [],
            pathFromRoot: [],
          },
        },
        { provide: ToastService, useValue: toastMock },
      ],
    }).createComponent(SellerRegisterPageComponent);
    localFixture.detectChanges();
    const localComponent = localFixture.componentInstance;

    expect(localComponent.selectedPlanId()).toBe('plan-growth');
    expect(localComponent.selectedBillingPeriod()).toBe(SubscriptionBillingPeriod.Annual);
    const text = localFixture.nativeElement.textContent as string;
    expect(text).toContain('Crescer');
    expect(text).toContain('Anual');
    expect(text).toContain('407,00');
  });

  it('sends only plan identifiers and never commercial values', () => {
    fillValidForm();
    component.selectPlan(PLAN_GROWTH);
    component.selectPeriod(PLAN_GROWTH, SubscriptionBillingPeriod.Annual);
    authServiceMock.registerSeller.mockReturnValue(of(registerResponse));

    component.submit();

    const request = authServiceMock.registerSeller.mock.calls[0][0];
    expect(request.planId).toBe('plan-growth');
    expect(request.billingPeriod).toBe(SubscriptionBillingPeriod.Annual);
    expect(request).not.toHaveProperty('amount');
    expect(request).not.toHaveProperty('monthlyAmount');
    expect(request).not.toHaveProperty('annualAmount');
    expect(request).not.toHaveProperty('trialDays');
    expect(request).not.toHaveProperty('benefits');
  });

  it('blocks submission and asks for revalidation when the selected plan is unavailable', () => {
    queryParams = { planId: 'plan-removed', billingPeriod: 'monthly' };

    TestBed.resetTestingModule();
    const localFixture = TestBed.configureTestingModule({
      imports: [SellerRegisterPageComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: PlanOfferService, useValue: planOfferMock },
        { provide: Router, useValue: routerMock },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: { get: (key: string) => queryParams[key] ?? null } },
            root: null,
            firstChild: null,
            children: [],
            pathFromRoot: [],
          },
        },
        { provide: ToastService, useValue: toastMock },
      ],
    }).createComponent(SellerRegisterPageComponent);
    localFixture.detectChanges();
    const localComponent = localFixture.componentInstance;

    localComponent.fullName.set('Contratante Teste');
    localComponent.whatsapp.set('(21) 99999-9999');
    localComponent.email.set('novo@vendedor.com');
    localComponent.password.set('Senha@123');
    localComponent.confirmPassword.set('Senha@123');
    localComponent.submit();

    expect(localComponent.errors()['plan']).toContain('Revalide');
    expect(authServiceMock.registerSeller).not.toHaveBeenCalled();
  });

  it('requires a plan selection on a direct visit without query params', () => {
    component.fullName.set('Contratante Teste');
    component.whatsapp.set('(21) 99999-9999');
    component.email.set('novo@vendedor.com');
    component.password.set('Senha@123');
    component.confirmPassword.set('Senha@123');
    component.submit();

    expect(component.errors()['plan']).toContain('Escolha um plano');
    expect(authServiceMock.registerSeller).not.toHaveBeenCalled();
  });
});
