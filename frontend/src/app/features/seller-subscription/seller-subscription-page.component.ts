import { HttpErrorResponse } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { SubscriptionService } from '../../core/services/subscription.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import {
  BILLING_PERIOD_LABELS,
  BillingPeriodOption,
  SellerSubscriptionBillingStatus,
  SellerSubscriptionChargeHistoryItem,
  SellerSubscriptionMyResponse,
  SellerSubscriptionPlan,
  SubscriptionBillingPeriod,
  subscriptionPlanPeriodOptions,
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
 * Visão do lojista sobre a própria assinatura. O lojista nunca administra o
 * catálogo: a tela mostra o plano contratado e, quando o catálogo tem mais de
 * um plano, oferece somente upgrade para um nível superior.
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
  readonly BillingPeriod = SubscriptionBillingPeriod;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly subscription = signal<SellerSubscriptionMyResponse | null>(null);
  readonly charges = signal<SellerSubscriptionChargeHistoryItem[]>([]);
  readonly plans = signal<SellerSubscriptionPlan[]>([]);
  readonly plansLoading = signal(true);
  readonly plansError = signal(false);
  readonly selectedPlanId = signal<string | null>(null);
  readonly selectedPeriod = signal<SubscriptionBillingPeriod | null>(null);
  readonly confirmVisible = signal(false);
  readonly contracting = signal(false);
  readonly contractError = signal<string | null>(null);
  readonly contractSuccess = signal<string | null>(null);

  readonly selectedPlan = computed<SellerSubscriptionPlan | null>(() => {
    const selectedId = this.selectedPlanId();
    if (!selectedId) return null;
    return this.plans().find((plan) => plan.id === selectedId) ?? null;
  });

  readonly eligiblePlans = computed<SellerSubscriptionPlan[]>(() => {
    const current = this.subscription();
    const plans = this.plans();
    if (!current?.planId) return plans;

    const currentPlan = plans.find((plan) => plan.id === current.planId);
    const currentLevel = current.planLevel ?? currentPlan?.level ?? 0;
    return plans.filter((plan) => plan.level > currentLevel);
  });

  /** Só há troca quando o catálogo tem mais de um plano e existe nível superior. */
  readonly showPlanSwitcher = computed(
    () => !this.planChangesBlocked() && this.plans().length > 1 && this.eligiblePlans().length > 0,
  );

  readonly planChangesBlocked = computed(() => {
    const state = this.displayState();
    return state === 'blocked' || state === 'overdue' || state === 'cancelled';
  });

  readonly hasNoPlan = computed(() => !!this.subscription()?.storeId && !this.subscription()?.planId);
  readonly canSelectPlan = computed(
    () => !this.planChangesBlocked() && (this.showPlanSwitcher() || (this.hasNoPlan() && this.plans().length > 0)),
  );

  readonly selectedOptions = computed<BillingPeriodOption[]>(() => {
    const plan = this.selectedPlan();
    return plan ? subscriptionPlanPeriodOptions(plan) : [];
  });

  readonly selectedAmount = computed<number | null>(() => {
    const option = this.selectedOptions().find(
      (item) => item.period === this.selectedPeriod(),
    );
    return option?.amount ?? null;
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
    return current.hasPendingGatewayCharge || current.planId ? 'active' : 'none';
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
        if (current.hasPendingGatewayCharge) {
          return `Sua assinatura está ativa. O boleto de ${this.formatCurrency(current.pendingChargeAmount ?? current.planAmount)} está aguardando a integração de gateway.`;
        }
        return `Sua assinatura está em dia. Próxima cobrança de ${this.formatCurrency(current.planAmount)} em ${this.formatDateValue(current.nextDueDateUtc)}.`;
      case 'due-soon':
        return `Sua mensalidade vence em ${this.formatDateValue(current.nextDueDateUtc)}. A cobrança será gerada quando a integração de gateway estiver disponível.`;
      case 'overdue':
        return 'A mensalidade está atrasada. A regularização online ainda não está disponível; fale com o suporte para orientar o pagamento.';
      case 'blocked':
        return 'A loja está bloqueada por inadimplência. Fale com o suporte para regularizar a mensalidade e reabrir a loja.';
      case 'cancelled':
        return 'A assinatura foi cancelada. Fale com o suporte para contratar um novo plano.';
      default:
        return 'Escolha um plano ativo abaixo para ativar a assinatura da sua loja.';
    }
  });

  readonly planSelectionHint = computed(() => {
    const current = this.subscription();
    if (!current) return null;
    if (this.planChangesBlocked()) {
      return 'Regularize a assinatura com o suporte antes de solicitar qualquer troca de plano.';
    }
    if (this.hasNoPlan()) {
      return 'Sua loja ainda não possui plano vinculado. Escolha um plano ativo para regularizar a assinatura.';
    }
    if (this.plans().length <= 1) {
      return 'Você está no único plano ativo do catálogo. Não há troca disponível no momento.';
    }
    if (this.eligiblePlans().length === 0) {
      return 'Você já está no plano de nível mais alto disponível. Downgrade não é permitido.';
    }
    return 'Você pode solicitar somente upgrade para um plano de nível superior. O nível é definido pela urbeat.';
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
          this.selectedPeriod.set(null);
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
    if (!this.canSelectPlan()) return;
    this.selectedPlanId.set(plan.id);
    const firstOption = subscriptionPlanPeriodOptions(plan)[0] ?? null;
    this.selectedPeriod.set(firstOption?.period ?? null);
    this.contractError.set(null);
    this.confirmVisible.set(false);
  }

  selectPeriod(period: SubscriptionBillingPeriod): void {
    if (!this.selectedPlan()) return;
    this.selectedPeriod.set(period);
  }

  isSelectedPeriod(period: SubscriptionBillingPeriod): boolean {
    return this.selectedPeriod() === period;
  }

  periodOptions(plan: SellerSubscriptionPlan): BillingPeriodOption[] {
    return subscriptionPlanPeriodOptions(plan);
  }

  openConfirmation(): void {
    if (!this.canSelectPlan() || !this.selectedPlan() || !this.selectedPeriod() || this.contracting()) {
      return;
    }
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
    const billingPeriod = this.selectedPeriod();
    if (!plan || !storeId || !billingPeriod || this.contracting()) return;

    this.contracting.set(true);
    this.contractError.set(null);
    this.contractSuccess.set(null);

    this.subscriptions.contract({ storeId, planId: plan.id, billingPeriod }).subscribe({
      next: (contract) => {
        this.contracting.set(false);
        this.confirmVisible.set(false);
        this.selectedPlanId.set(null);
        this.selectedPeriod.set(null);
        this.contractSuccess.set(
          `${contract.planName} ativado no período ${BILLING_PERIOD_LABELS[contract.billingPeriod].toLowerCase()}. A cobrança de ${this.formatCurrency(contract.planAmount)} está aguardando a integração de gateway.`,
        );
        this.load();
        this.loadPlans();
      },
      error: (error: unknown) => {
        this.contracting.set(false);
        this.confirmVisible.set(false);
        this.contractError.set(this.contractErrorMessage(error));
        if (error instanceof HttpErrorResponse && (error.status === 409 || error.status === 400)) {
          // O catálogo pode ter mudado durante a jornada: recarrega o estado
          // autoritativo em vez de manter dados defasados na tela.
          this.load();
          this.loadPlans();
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

  billingPeriodLabel(current: SellerSubscriptionMyResponse): string {
    return current.billingPeriod ? BILLING_PERIOD_LABELS[current.billingPeriod] : 'Mensal';
  }

  planMeta(current: SellerSubscriptionMyResponse): string {
    const period = this.billingPeriodLabel(current).toLowerCase();
    if (current.planAmount == null) {
      return `Periodicidade ${period}`;
    }
    return `${this.formatCurrency(current.planAmount)}/${period === 'anual' ? 'ano' : 'mês'}`;
  }

  nextChargeDate(current: SellerSubscriptionMyResponse): string {
    if (this.displayState() === 'none') return 'Após contratar';
    return this.formatDate(current.nextDueDateUtc);
  }

  nextChargeMeta(current: SellerSubscriptionMyResponse): string {
    if (current.hasPendingGatewayCharge) {
      return 'Boleto aguardando integração';
    }
    const value = current.nextChargeAmount ?? current.planAmount;
    if (this.displayState() === 'none') {
      return 'O valor e a data são definidos pelo plano escolhido.';
    }
    if (value == null) {
      return 'Valor definido pelo plano contratado.';
    }
    const amount = this.formatCurrency(value);
    return current.isTrialActive ? `${amount} após o período grátis` : amount;
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

  planAmountLabel(option: BillingPeriodOption): string {
    const suffix = option.period === SubscriptionBillingPeriod.Annual ? '/ano' : '/mês';
    const discount = option.discountPercent
      ? ` · ${option.discountPercent}% off`
      : '';
    return `${this.formatCurrency(option.amount)}${suffix}${discount}`;
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
          return 'A periodicidade escolhida não está disponível para este plano. Revalide a seleção.';
        case 403:
          return 'Você não tem permissão para alterar o plano desta loja.';
        case 404:
          return 'Não encontramos a loja ou o plano selecionado. Atualize a página e tente novamente.';
        case 409:
          return 'A troca é somente para um plano de nível superior. Downgrade não é permitido.';
        default:
          return 'Não foi possível concluir a alteração agora. Tente novamente em instantes.';
      }
    }
    return 'Não foi possível concluir a alteração agora. Tente novamente em instantes.';
  }
}
