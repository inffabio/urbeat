import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminPlansService } from '../../core/services/admin-plans.service';
import { ToastService } from '../../core/services/toast.service';
import {
  AdminPlan,
  CreatePlanRequest,
  GatewayConfiguration,
  UpdateGatewayConfigurationRequest,
  UpdatePlanRequest,
} from '../../shared/models/subscription.model';

interface PlanFormState {
  name: string;
  description: string;
  monthlyAmount: string;
  trialDays: string;
  monthlyEnabled: boolean;
  annualEnabled: boolean;
  annualDiscountPercent: string;
  level: string;
  isActive: boolean;
  benefitsText: string;
}

interface GatewayFormState {
  provider: string;
  environment: string;
  apiBaseUrl: string;
  webhookUrl: string;
  apiKeySecretReference: string;
  webhookSecretReference: string;
  isEnabled: boolean;
}

function emptyPlanForm(): PlanFormState {
  return {
    name: '',
    description: '',
    monthlyAmount: '',
    trialDays: '15',
    monthlyEnabled: true,
    annualEnabled: false,
    annualDiscountPercent: '0',
    level: '0',
    isActive: true,
    benefitsText: '',
  };
}

@Component({
  selector: 'app-admin-plans-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-plans-page.component.html',
  styleUrl: './admin-plans-page.component.scss',
})
export class AdminPlansPageComponent implements OnInit {
  private readonly plansService = inject(AdminPlansService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly plans = signal<AdminPlan[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly totalItems = signal(0);
  readonly totalPages = signal(0);
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly saving = signal(false);
  readonly formVisible = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly formError = signal<string | null>(null);
  readonly form = signal<PlanFormState>(emptyPlanForm());

  readonly gateway = signal<GatewayConfiguration | null>(null);
  readonly gatewayPlanId = signal<string | null>(null);
  readonly gatewayPlanName = signal<string>('');
  readonly gatewayLoading = signal(false);
  readonly gatewaySaving = signal(false);
  readonly gatewayError = signal<string | null>(null);
  readonly gatewaySuccess = signal<string | null>(null);
  readonly gatewayForm = signal<GatewayFormState>({
    provider: 'Asaas',
    environment: 'Sandbox',
    apiBaseUrl: '',
    webhookUrl: '',
    apiKeySecretReference: '',
    webhookSecretReference: '',
    isEnabled: false,
  });

  readonly hasPrevious = computed(() => this.page() > 1);
  readonly hasNext = computed(() => this.page() < this.totalPages());
  readonly pages = computed(() => {
    const total = this.totalPages();
    if (total <= 0) return [];
    if (total <= 7) return Array.from({ length: total }, (_, index) => index + 1);

    const start = Math.max(1, Math.min(this.page() - 3, total - 6));
    return Array.from({ length: 7 }, (_, index) => start + index);
  });
  readonly formTitle = computed(() => (this.editingId() ? 'Editar plano' : 'Novo plano'));
  readonly annualPreview = computed(() => {
    const state = this.form();
    const monthly = Number(state.monthlyAmount);
    const discount = Number(state.annualDiscountPercent);
    if (!Number.isFinite(monthly) || monthly <= 0) return null;
    const safeDiscount = Number.isFinite(discount) ? Math.min(100, Math.max(0, discount)) : 0;
    return Math.round(monthly * 12 * (100 - safeDiscount)) / 100;
  });

  ngOnInit(): void {
    this.readUrlState();
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.plansService.list({ page: this.page(), pageSize: this.pageSize() }).subscribe({
      next: (response) => {
        const lastValidPage = Math.max(response.totalPages, 1);

        // The catalog can shrink when another administrator changes it. Never keep the operator
        // on a page that no longer exists.
        if (this.page() > lastValidPage) {
          this.page.set(lastValidPage);
          this.syncUrl();
          this.load();
          return;
        }

        this.plans.set(this.sortPlans(response.items));
        this.totalItems.set(response.totalItems);
        this.totalPages.set(response.totalPages);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  goToPage(page: number): void {
    if (page < 1 || page > Math.max(this.totalPages(), 1) || page === this.page()) return;

    this.page.set(page);
    this.syncUrl();
    this.load();
  }

  previousPage(): void {
    if (this.hasPrevious()) this.goToPage(this.page() - 1);
  }

  nextPage(): void {
    if (this.hasNext()) this.goToPage(this.page() + 1);
  }

  openGateway(plan: AdminPlan): void {
    this.gatewayPlanId.set(plan.id);
    this.gatewayPlanName.set(plan.name);
    this.gateway.set(null);
    this.gatewayError.set(null);
    this.gatewaySuccess.set(null);
    this.loadGateway(plan.id);
  }

  closeGateway(): void {
    if (this.gatewaySaving()) return;
    this.gatewayPlanId.set(null);
    this.gatewayPlanName.set('');
    this.gateway.set(null);
    this.gatewayError.set(null);
    this.gatewaySuccess.set(null);
  }

  loadGateway(planId: string): void {
    this.gatewayLoading.set(true);
    this.gatewayError.set(null);
    this.plansService.getGatewayConfiguration(planId).subscribe({
      next: (configuration) => {
        this.gateway.set(configuration);
        this.gatewayForm.set({
          provider: configuration.provider || 'Asaas',
          environment: configuration.environment || 'Sandbox',
          apiBaseUrl: configuration.apiBaseUrl ?? '',
          webhookUrl: configuration.webhookUrl ?? '',
          apiKeySecretReference: '',
          webhookSecretReference: '',
          isEnabled: configuration.isEnabled,
        });
        this.gatewayLoading.set(false);
      },
      error: () => {
        this.gatewayError.set(
          'Não foi possível carregar a configuração de gateway deste plano.',
        );
        this.gatewayLoading.set(false);
      },
    });
  }

  startCreate(): void {
    this.editingId.set(null);
    this.form.set(emptyPlanForm());
    this.formError.set(null);
    this.formVisible.set(true);
  }

  startEdit(plan: AdminPlan): void {
    this.editingId.set(plan.id);
    this.form.set({
      name: plan.name,
      description: plan.description,
      monthlyAmount: String(plan.monthlyAmount),
      trialDays: String(plan.trialDays),
      monthlyEnabled: plan.monthlyEnabled,
      annualEnabled: plan.annualEnabled,
      annualDiscountPercent: String(plan.annualDiscountPercent),
      level: String(plan.level),
      isActive: plan.isActive,
      benefitsText: plan.benefits.join('\n'),
    });
    this.formError.set(null);
    this.formVisible.set(true);
  }

  cancelForm(): void {
    if (this.saving()) return;
    this.formVisible.set(false);
    this.formError.set(null);
  }

  save(): void {
    if (this.saving()) return;

    const state = this.form();
    const monthlyAmount = Number(state.monthlyAmount);
    if (!state.name.trim()) {
      this.formError.set('Informe o nome do plano.');
      return;
    }
    if (!Number.isFinite(monthlyAmount) || monthlyAmount <= 0) {
      this.formError.set('Informe um valor mensal maior que zero.');
      return;
    }
    if (!state.monthlyEnabled && !state.annualEnabled) {
      this.formError.set('Habilite a cobrança mensal, a anual ou ambas.');
      return;
    }

    const benefits = this.parseBenefits(state.benefitsText);
    const annualDiscountPercent = this.parseInteger(state.annualDiscountPercent, 0);
    if (annualDiscountPercent < 0 || annualDiscountPercent > 100) {
      this.formError.set('O desconto anual deve estar entre 0 e 100%.');
      return;
    }

    const level = this.parseInteger(state.level, 0);
    if (level < 0) {
      this.formError.set('O nível do plano não pode ser negativo.');
      return;
    }

    const editingId = this.editingId();
    this.saving.set(true);
    this.formError.set(null);

    if (editingId) {
      const request: UpdatePlanRequest = {
        name: state.name.trim(),
        monthlyAmount,
        description: state.description.trim(),
        trialDays: this.parseInteger(state.trialDays, 0),
        benefits,
        monthlyEnabled: state.monthlyEnabled,
        annualEnabled: state.annualEnabled,
        annualDiscountPercent,
        level,
      };
      this.plansService.update(editingId, request).subscribe({
        next: (plan) => this.handleSaved(plan, 'Plano atualizado.'),
        error: (error: unknown) => this.handleSaveError(error),
      });
      return;
    }

    const request: CreatePlanRequest = {
      name: state.name.trim(),
      monthlyAmount,
      description: state.description.trim(),
      isActive: state.isActive,
      trialDays: this.parseInteger(state.trialDays, 0),
      benefits,
      monthlyEnabled: state.monthlyEnabled,
      annualEnabled: state.annualEnabled,
      annualDiscountPercent,
      level,
    };
    this.plansService.create(request).subscribe({
      next: (plan) => this.handleSaved(plan, 'Plano criado.'),
      error: (error: unknown) => this.handleSaveError(error),
    });
  }

  toggleStatus(plan: AdminPlan): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.plansService.updateStatus(plan.id, !plan.isActive).subscribe({
      next: (updated) => {
        this.saving.set(false);
        this.plans.update((plans) =>
          this.sortPlans(plans.map((item) => (item.id === updated.id ? updated : item))),
        );
        this.toast.showSuccess(updated.isActive ? 'Plano ativado.' : 'Plano desativado.');
      },
      error: () => {
        this.saving.set(false);
        this.toast.showError('Não foi possível alterar o status do plano.');
      },
    });
  }

  saveGateway(): void {
    const planId = this.gatewayPlanId();
    if (!planId || this.gatewaySaving()) return;

    const state = this.gatewayForm();
    const request: UpdateGatewayConfigurationRequest = {
      provider: state.provider.trim() || 'Asaas',
      environment: state.environment,
      apiBaseUrl: state.apiBaseUrl.trim(),
      webhookUrl: state.webhookUrl.trim(),
      isEnabled: state.isEnabled,
      ...(state.apiKeySecretReference.trim()
        ? { apiKeySecretReference: state.apiKeySecretReference.trim() }
        : {}),
      ...(state.webhookSecretReference.trim()
        ? { webhookSecretReference: state.webhookSecretReference.trim() }
        : {}),
    };

    this.gatewaySaving.set(true);
    this.gatewayError.set(null);
    this.gatewaySuccess.set(null);
    this.plansService.updateGatewayConfiguration(planId, request).subscribe({
      next: (configuration) => {
        this.gateway.set(configuration);
        this.gatewayForm.update((form) => ({
          ...form,
          apiKeySecretReference: '',
          webhookSecretReference: '',
        }));
        this.gatewaySaving.set(false);
        this.gatewaySuccess.set(
          'Configuração salva. Nenhuma cobrança real é gerada nesta versão.',
        );
        this.toast.showSuccess('Configuração de gateway do plano atualizada.');
      },
      error: (error: unknown) => {
        this.gatewaySaving.set(false);
        this.gatewayError.set(this.errorMessage(error, 'Não foi possível salvar a configuração de gateway.'));
      },
    });
  }

  gatewayStateLabel(hasSecret: boolean): string {
    return hasSecret ? 'Configurado' : 'Não configurado';
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  updateForm(patch: Partial<PlanFormState>): void {
    this.form.update((state) => ({ ...state, ...patch }));
  }

  updateGatewayForm(patch: Partial<GatewayFormState>): void {
    this.gatewayForm.update((state) => ({ ...state, ...patch }));
  }

  planPriceLabel(plan: AdminPlan): string {
    if (plan.monthlyEnabled && plan.annualEnabled) {
      return `${this.formatCurrency(plan.monthlyAmount)}/mês ou ${this.formatCurrency(plan.annualAmount)}/ano`;
    }
    if (plan.annualEnabled) {
      return `${this.formatCurrency(plan.annualAmount)}/ano`;
    }
    return `${this.formatCurrency(plan.monthlyAmount)}/mês`;
  }

  private handleSaved(_plan: AdminPlan, message: string): void {
    this.saving.set(false);
    this.formVisible.set(false);
    this.editingId.set(null);
    this.formError.set(null);
    // The server owns ordering and pagination: reload so a new plan lands on its real page.
    this.load();
    this.toast.showSuccess(message);
  }

  private handleSaveError(error: unknown): void {
    this.saving.set(false);
    this.formError.set(this.errorMessage(error, 'Não foi possível salvar o plano.'));
  }

  private errorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse || (typeof error === 'object' && error !== null && 'status' in error)) {
      const httpError = error as HttpErrorResponse;
      const body = httpError.error as { error?: string; errors?: Record<string, string[]> } | null;
      if (body?.error) return body.error;
      if (body?.errors) {
        const messages = Object.values(body.errors).flat().filter(Boolean);
        if (messages.length > 0) return messages.join(' ');
      }
      if (httpError.status === 409) return 'Já existe um plano com este nome.';
      if (httpError.status === 400) return 'Revise os dados do plano e tente novamente.';
    }
    return fallback;
  }

  private parseBenefits(value: string): string[] {
    const seen = new Set<string>();
    const result: string[] = [];
    for (const line of value.split('\n')) {
      const benefit = line.trim();
      if (benefit && !seen.has(benefit)) {
        seen.add(benefit);
        result.push(benefit);
      }
    }
    return result;
  }

  private parseInteger(value: string, fallback: number): number {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  private sortPlans(plans: AdminPlan[]): AdminPlan[] {
    return [...plans].sort(
      (left, right) => left.level - right.level || left.name.localeCompare(right.name, 'pt-BR'),
    );
  }

  private syncUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: { page: this.page() > 1 ? this.page() : null },
    });
  }

  private readUrlState(): void {
    const page = Number(this.route.snapshot.queryParamMap.get('page'));
    this.page.set(Number.isFinite(page) && page >= 1 ? Math.floor(page) : 1);
  }
}
