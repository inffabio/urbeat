import { HttpErrorResponse } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { SubscriptionService } from '../../core/services/subscription.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import {
  SellerSubscriptionBillingStatus,
  SellerSubscriptionChargeHistoryItem,
  SellerSubscriptionMyResponse,
  SellerSubscriptionPlan,
} from '../../shared/models/subscription.model';

export type SellerSubscriptionDisplayState =
  | 'none'
  | 'trial'
  | 'active'
  | 'due-soon'
  | 'overdue'
  | 'blocked'
  | 'cancelled';

/**
 * A API atual não expõe cobrança individual, PIX, boleto, invoice ou payment
 * link para a assinatura: somente o webhook do gateway atualiza o status. Por
 * isso a tela nunca simula pagamento; cobranças vencidas mostram
 * "Regularização em breve" e o contato do suporte. Um botão de pagamento só
 * deve voltar quando existir contrato de gateway dedicado a essa cobrança.
 */
@Component({
  selector: 'app-seller-subscription-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-subscription-page.component.html',
  styleUrl: './seller-subscription-page.component.scss',
})
export class SellerSubscriptionPageComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);

  readonly BillingStatus = SellerSubscriptionBillingStatus;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly subscription = signal<SellerSubscriptionMyResponse | null>(null);
  readonly charges = signal<SellerSubscriptionChargeHistoryItem[]>([]);
  readonly plans = signal<SellerSubscriptionPlan[]>([]);
  readonly plansLoading = signal(true);
  readonly plansError = signal(false);
  readonly selectedPlanId = signal<string | null>(null);
  readonly confirmVisible = signal(false);
  readonly contracting = signal(false);
  readonly contractError = signal<string | null>(null);
  readonly contractSuccess = signal<string | null>(null);

  readonly selectedPlan = computed<SellerSubscriptionPlan | null>(() => {
    const selectedId = this.selectedPlanId();
    if (!selectedId) return null;
    return this.plans().find((plan) => plan.id === selectedId) ?? null;
  });

  readonly displayState = computed<SellerSubscriptionDisplayState>(() => {
    const current = this.subscription();
    if (!current) return 'none';

    if (current.storeBlocked || current.billingStatus === SellerSubscriptionBillingStatus.Blocked) {
      return 'blocked';
    }
    if (current.billingStatus === SellerSubscriptionBillingStatus.Overdue) {
      return 'overdue';
    }
    if (current.billingStatus === SellerSubscriptionBillingStatus.Cancelled) {
      return 'cancelled';
    }
    if (current.isTrialActive) {
      return 'trial';
    }
    if (current.hasGatewaySubscription) {
      if (current.billingStatus === SellerSubscriptionBillingStatus.DueSoon) {
        return 'due-soon';
      }
      return this.isDueSoon(current.nextDueDateUtc) ? 'due-soon' : 'active';
    }
    return 'none';
  });

  readonly canContract = computed(() => {
    const current = this.subscription();
    if (!current?.storeId) return false;
    if (current.hasGatewaySubscription) return false;
    return this.displayState() === 'none' || this.displayState() === 'trial';
  });

  readonly needsRegularization = computed(() => {
    const state = this.displayState();
    return state === 'overdue' || state === 'blocked';
  });

  readonly stateTitle = computed(() => {
    switch (this.displayState()) {
      case 'trial':
        return 'Período grátis ativo';
      case 'active':
        return 'Assinatura ativa';
      case 'due-soon':
        return 'Assinatura vencendo';
      case 'overdue':
        return 'Assinatura em atraso';
      case 'blocked':
        return 'Loja bloqueada';
      case 'cancelled':
        return 'Assinatura cancelada';
      default:
        return 'Sem assinatura contratada';
    }
  });

  readonly stateStatusLabel = computed(() => {
    switch (this.displayState()) {
      case 'trial':
        return 'Período grátis';
      case 'active':
        return 'Ativa';
      case 'due-soon':
        return 'Vencendo';
      case 'overdue':
        return 'Em atraso';
      case 'blocked':
        return 'Bloqueada';
      case 'cancelled':
        return 'Cancelada';
      default:
        return 'Não contratada';
    }
  });

  readonly stateCopy = computed(() => {
    const current = this.subscription();
    if (!current) return '';

    switch (this.displayState()) {
      case 'trial': {
        const trialEnd = this.formatDateValue(current.trialEndsAtUtc);
        const amount = this.formatCurrency(current.nextChargeAmount ?? current.planAmount);
        const dueDate = this.formatDateValue(current.nextDueDateUtc);
        return `Seu período grátis vai até ${trialEnd}. A primeira cobrança de ${amount} está prevista para ${dueDate}.`;
      }
      case 'active':
        return `Sua assinatura está em dia. Próxima cobrança de ${this.formatCurrency(current.planAmount)} em ${this.formatDateValue(current.nextDueDateUtc)}.`;
      case 'due-soon':
        return `Sua mensalidade vence em ${this.formatDateValue(current.nextDueDateUtc)}. A cobrança é gerada automaticamente pelo meio de pagamento da assinatura.`;
      case 'overdue':
        return 'A mensalidade está atrasada. A regularização online ainda não está disponível; fale com o suporte para orientar o pagamento.';
      case 'blocked':
        return 'A loja está bloqueada por inadimplência. Fale com o suporte para regularizar a mensalidade e reabrir a loja.';
      case 'cancelled':
        return 'A assinatura foi cancelada. Fale com o suporte para contratar um novo plano.';
      default:
        return 'Escolha um plano ativo abaixo para ativar a cobrança recorrente da sua loja.';
    }
  });

  readonly planSelectionHint = computed(() => {
    const current = this.subscription();
    if (!current) return null;
    if (current.hasGatewaySubscription) {
      return 'Sua loja já possui uma assinatura contratada. A troca de plano estará disponível em breve.';
    }
    if (this.displayState() === 'cancelled') {
      return 'Fale com o suporte para contratar um novo plano.';
    }
    return null;
  });

  ngOnInit(): void {
    this.load();
    this.loadPlans();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    forkJoin({
      subscription: this.subscriptions.getMySubscription(),
      charges: this.subscriptions.listMyCharges(),
    }).subscribe({
      next: ({ subscription, charges }) => {
        this.subscription.set(subscription);
        this.charges.set(charges);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  loadPlans(): void {
    this.plansLoading.set(true);
    this.plansError.set(false);

    this.subscriptions.listPlans().subscribe({
      next: (plans) => {
        this.plans.set(plans);
        const selectedId = this.selectedPlanId();
        if (selectedId && !plans.some((plan) => plan.id === selectedId)) {
          this.selectedPlanId.set(null);
          this.confirmVisible.set(false);
        }
        this.plansLoading.set(false);
      },
      error: () => {
        this.plansError.set(true);
        this.plansLoading.set(false);
      },
    });
  }

  refresh(): void {
    this.contractError.set(null);
    this.contractSuccess.set(null);
    this.load();
    this.loadPlans();
  }

  selectPlan(plan: SellerSubscriptionPlan): void {
    if (!this.canContract()) return;
    this.selectedPlanId.set(plan.id);
    this.contractError.set(null);
    this.confirmVisible.set(false);
  }

  openConfirmation(): void {
    if (!this.canContract() || !this.selectedPlan() || this.contracting()) return;
    this.contractError.set(null);
    this.confirmVisible.set(true);
  }

  cancelConfirmation(): void {
    if (this.contracting()) return;
    this.confirmVisible.set(false);
  }

  confirmContract(): void {
    const plan = this.selectedPlan();
    const storeId = this.subscription()?.storeId ?? null;
    if (!plan || !storeId || this.contracting()) return;

    this.contracting.set(true);
    this.contractError.set(null);
    this.contractSuccess.set(null);

    this.subscriptions.contract({ storeId, planId: plan.id }).subscribe({
      next: (contract) => {
        this.contracting.set(false);
        this.confirmVisible.set(false);
        this.selectedPlanId.set(null);
        this.contractSuccess.set(
          `${contract.planName} contratado. A primeira cobrança de ${this.formatCurrency(contract.planAmount)} está prevista para ${this.formatDate(contract.nextBillingDateUtc)}. A cobrança será gerada na data de vencimento.`,
        );
        this.load();
        this.loadPlans();
      },
      error: (error: unknown) => {
        this.contracting.set(false);
        this.confirmVisible.set(false);
        this.contractError.set(this.contractErrorMessage(error));
        if (error instanceof HttpErrorResponse && error.status === 409) {
          // O backend já possui uma assinatura contratada: atualiza a tela para
          // refletir o estado real em vez de manter dados defasados.
          this.load();
        }
      },
    });
  }

  stateIcon(): string {
    switch (this.displayState()) {
      case 'trial':
        return 'bi-gift';
      case 'active':
        return 'bi-check-circle';
      case 'due-soon':
        return 'bi-clock-history';
      case 'overdue':
      case 'blocked':
        return 'bi-exclamation-triangle';
      case 'cancelled':
        return 'bi-x-circle';
      default:
        return 'bi-info-circle';
    }
  }

  planMeta(current: SellerSubscriptionMyResponse): string {
    if (current.planAmount == null) {
      return 'Escolha um plano para definir o valor';
    }
    return `${this.formatCurrency(current.planAmount)}/mês`;
  }

  nextChargeDate(current: SellerSubscriptionMyResponse): string {
    if (this.displayState() === 'none') return 'Após contratar';
    return this.formatDate(current.nextDueDateUtc);
  }

  nextChargeMeta(current: SellerSubscriptionMyResponse): string {
    const value = current.nextChargeAmount ?? current.planAmount;
    if (this.displayState() === 'none') {
      return 'O valor e a data são definidos pelo plano escolhido.';
    }
    if (value == null) {
      return 'Valor definido pelo plano contratado.';
    }
    const amount = this.formatCurrency(value);
    return current.isTrialActive ? `${amount}/mês após o período grátis` : `${amount}/mês`;
  }

  statusLabel(status: SellerSubscriptionBillingStatus | null | undefined): string {
    switch (status) {
      case SellerSubscriptionBillingStatus.Active:
        return 'Ativa';
      case SellerSubscriptionBillingStatus.DueSoon:
        return 'Vencendo';
      case SellerSubscriptionBillingStatus.Overdue:
        return 'Em atraso';
      case SellerSubscriptionBillingStatus.Blocked:
        return 'Bloqueada';
      case SellerSubscriptionBillingStatus.Cancelled:
        return 'Cancelada';
      default:
        return 'Não contratada';
    }
  }

  formatCurrency(value: number | null | undefined): string {
    if (value == null) return '—';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return 'Sem data';
    return formatSaoPauloDate(value);
  }

  formatDateValue(value: string | null | undefined): string {
    if (!value) return 'data a definir';
    return formatSaoPauloDate(value);
  }

  formatPeriod(charge: SellerSubscriptionChargeHistoryItem): string {
    if (!charge.billingPeriodStartUtc || !charge.billingPeriodEndUtc) {
      return 'Sem período';
    }
    return `${this.formatDate(charge.billingPeriodStartUtc)} - ${this.formatDate(charge.billingPeriodEndUtc)}`;
  }

  planTrialLabel(plan: SellerSubscriptionPlan): string {
    if (plan.trialDays > 0) {
      return `${plan.trialDays} ${plan.trialDays === 1 ? 'dia' : 'dias'} grátis`;
    }
    return 'Primeira cobrança imediata';
  }

  planActionLabel(plan: SellerSubscriptionPlan): string {
    if (!this.canContract()) {
      return 'Troca indisponível';
    }
    return this.selectedPlanId() === plan.id ? 'Plano selecionado' : 'Selecionar plano';
  }

  /**
   * O backend não emite DueSoon hoje; para assinaturas contratadas, o vencimento
   * nos próximos cinco dias é apresentado como "vencendo" sem alterar o status
   * autoritativo retornado pela API.
   */
  private isDueSoon(value: string | null | undefined): boolean {
    if (!value) return false;
    const dueDate = new Date(value).getTime();
    if (Number.isNaN(dueDate)) return false;
    const remaining = dueDate - Date.now();
    return remaining >= 0 && remaining <= 5 * 24 * 60 * 60 * 1000;
  }

  private contractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 400:
          return 'Este plano não está mais disponível. Escolha outro plano ativo e tente novamente.';
        case 403:
          return 'Você não tem permissão para contratar este plano para a loja informada.';
        case 404:
          return 'Não encontramos a loja ou o plano selecionado. Atualize a página e tente novamente.';
        case 409:
          return 'Sua loja já possui uma assinatura contratada. A troca de plano estará disponível em breve.';
        default:
          return 'Não foi possível concluir a contratação agora. Tente novamente em instantes.';
      }
    }
    return 'Não foi possível concluir a contratação agora. Tente novamente em instantes.';
  }
}
