import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminPlansService } from '../../core/services/admin-plans.service';
import { ToastService } from '../../core/services/toast.service';
import { AdminPlan, GatewayConfiguration } from '../../shared/models/subscription.model';
import { AdminPlansPageComponent } from './admin-plans-page.component';

const PLAN: AdminPlan = {
  id: 'plan-1',
  name: 'Crescer',
  amount: 39.9,
  monthlyAmount: 39.9,
  annualAmount: 407.0,
  annualDiscountPercent: 15,
  monthlyEnabled: true,
  annualEnabled: true,
  description: 'Para negócios em crescimento.',
  isActive: true,
  trialDays: 15,
  benefits: ['Relatórios de vendas'],
  level: 1,
};

const GATEWAY: GatewayConfiguration = {
  id: 'gateway-1',
  planId: 'plan-1',
  provider: 'Asaas',
  environment: 'Sandbox',
  apiBaseUrl: 'https://sandbox.asaas.com',
  webhookUrl: '',
  hasApiKey: true,
  hasWebhookSecret: false,
  isEnabled: false,
  updatedAtUtc: '2026-11-18T00:00:00Z',
};

describe('AdminPlansPageComponent', () => {
  let fixture: ComponentFixture<AdminPlansPageComponent>;
  let component: AdminPlansPageComponent;
  let serviceMock: {
    list: jest.Mock;
    create: jest.Mock;
    update: jest.Mock;
    updateStatus: jest.Mock;
    getGatewayConfiguration: jest.Mock;
    updateGatewayConfiguration: jest.Mock;
  };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock };

  beforeEach(async () => {
    serviceMock = {
      list: jest.fn().mockReturnValue(of([PLAN])),
      create: jest.fn(),
      update: jest.fn(),
      updateStatus: jest.fn(),
      getGatewayConfiguration: jest.fn().mockReturnValue(of(GATEWAY)),
      updateGatewayConfiguration: jest.fn(),
    };
    toastMock = { showSuccess: jest.fn(), showError: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [AdminPlansPageComponent],
      providers: [
        { provide: AdminPlansService, useValue: serviceMock },
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminPlansPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('lists plans with monthly/annual pricing, level and status', () => {
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Crescer');
    expect(text).toContain('Nível 1');
    expect(text).toContain('39,90');
    expect(text).toContain('407,00');
    expect(text).toContain('Ativo');
  });

  it('creates a plan carrying the server-authoritative pricing configuration', () => {
    serviceMock.create.mockReturnValue(of({ ...PLAN, id: 'plan-new', name: 'Novo Plano' }));

    component.startCreate();
    component.updateForm({
      name: 'Novo Plano',
      description: 'Descrição',
      monthlyAmount: '49.90',
      trialDays: '10',
      annualEnabled: true,
      annualDiscountPercent: '20',
      level: '3',
      benefitsText: 'Benefício A\nBenefício B\nBenefício A',
    });
    component.save();

    expect(serviceMock.create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Novo Plano',
        monthlyAmount: 49.9,
        trialDays: 10,
        monthlyEnabled: true,
        annualEnabled: true,
        annualDiscountPercent: 20,
        level: 3,
        benefits: ['Benefício A', 'Benefício B'],
      }),
    );
    expect(component.plans().some((plan) => plan.id === 'plan-new')).toBe(true);
  });

  it('edits a plan and preserves its identity when saving', () => {
    serviceMock.update.mockReturnValue(of({ ...PLAN, level: 4, annualDiscountPercent: 25 }));

    component.startEdit(PLAN);
    expect(component.form().name).toBe('Crescer');
    expect(component.form().benefitsText).toBe('Relatórios de vendas');

    component.updateForm({ level: '4', annualDiscountPercent: '25' });
    component.save();

    expect(serviceMock.update).toHaveBeenCalledWith(
      'plan-1',
      expect.objectContaining({ name: 'Crescer', level: 4, annualDiscountPercent: 25 }),
    );
  });

  it('deactivates a plan without deleting existing data', () => {
    serviceMock.updateStatus.mockReturnValue(of({ ...PLAN, isActive: false }));

    component.toggleStatus(PLAN);

    expect(serviceMock.updateStatus).toHaveBeenCalledWith('plan-1', false);
    expect(component.plans()[0].isActive).toBe(false);
  });

  it('opens the gateway configuration for a specific plan and masks secrets', () => {
    component.openGateway(PLAN);
    fixture.detectChanges();

    expect(serviceMock.getGatewayConfiguration).toHaveBeenCalledWith('plan-1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Gateway do plano Crescer');
    expect(text).toContain('Referência da chave de API:');
    expect(text).toContain('Configurado');
    expect(text).toContain('Referência do segredo do webhook:');
    expect(text).toContain('Não configurado');
    expect(fixture.nativeElement.querySelector('input[name="apiKeySecretReference"]').value).toBe('');
    expect(fixture.nativeElement.querySelector('input[name="webhookSecretReference"]').value).toBe('');
  });

  it('saves gateway references scoped to the selected plan only', () => {
    serviceMock.updateGatewayConfiguration.mockReturnValue(of({ ...GATEWAY, hasWebhookSecret: true }));

    component.openGateway(PLAN);
    component.updateGatewayForm({
      isEnabled: true,
      apiKeySecretReference: 'vault://urbeat/asaas/plan-1/api-key',
      webhookSecretReference: 'vault://urbeat/asaas/plan-1/webhook',
    });
    component.saveGateway();

    expect(serviceMock.updateGatewayConfiguration).toHaveBeenCalledWith(
      'plan-1',
      expect.objectContaining({
        isEnabled: true,
        apiKeySecretReference: 'vault://urbeat/asaas/plan-1/api-key',
        webhookSecretReference: 'vault://urbeat/asaas/plan-1/webhook',
      }),
    );
  });

  it('does not resend secret references when the gateway fields stay blank', () => {
    serviceMock.updateGatewayConfiguration.mockReturnValue(of(GATEWAY));

    component.openGateway(PLAN);
    component.updateGatewayForm({ isEnabled: true });
    component.saveGateway();

    const [planId, payload] = serviceMock.updateGatewayConfiguration.mock.calls[0];
    expect(planId).toBe('plan-1');
    expect(payload).not.toHaveProperty('apiKeySecretReference');
    expect(payload).not.toHaveProperty('webhookSecretReference');
    expect(payload.isEnabled).toBe(true);
  });

  it('rejects invalid form combinations before calling the server', () => {
    component.startCreate();
    component.updateForm({ name: 'Sem período', monthlyAmount: '10', monthlyEnabled: false, annualEnabled: false });
    component.save();

    expect(component.formError()).toContain('mensal');
    expect(serviceMock.create).not.toHaveBeenCalled();
  });

  it('surfaces server validation errors without closing the form', () => {
    serviceMock.create.mockReturnValue(
      throwError(() => ({ status: 400, error: { error: 'Já existe um plano com este nome.' } })),
    );

    component.startCreate();
    component.updateForm({ name: 'Crescer', monthlyAmount: '39.90' });
    component.save();

    expect(component.formError()).toContain('Já existe um plano');
    expect(component.formVisible()).toBe(true);
  });
});
