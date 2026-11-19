/**
 * Os valores numéricos espelham o enum `SellerSubscriptionBillingStatus` do
 * backend. O servidor emite atualmente Active=1, Overdue=2 e Blocked=3;
 * DueSoon e Cancelled ficam reservados para estados futuros do servidor.
 */
export enum SellerSubscriptionBillingStatus {
  Active = 1,
  Overdue = 2,
  Blocked = 3,
  DueSoon = 4,
  Cancelled = 5,
}

/** Espelha `SubscriptionBillingPeriod` no backend. */
export enum SubscriptionBillingPeriod {
  Monthly = 1,
  Annual = 2,
}

export type BillingPeriodQuery = 'monthly' | 'annual';

export const BILLING_PERIOD_LABELS: Record<SubscriptionBillingPeriod, string> = {
  [SubscriptionBillingPeriod.Monthly]: 'Mensal',
  [SubscriptionBillingPeriod.Annual]: 'Anual',
};

export function billingPeriodToQuery(period: SubscriptionBillingPeriod): BillingPeriodQuery {
  return period === SubscriptionBillingPeriod.Annual ? 'annual' : 'monthly';
}

export function queryToBillingPeriod(value: string | null | undefined): SubscriptionBillingPeriod | null {
  if (value === 'annual') return SubscriptionBillingPeriod.Annual;
  if (value === 'monthly') return SubscriptionBillingPeriod.Monthly;
  return null;
}

export interface BillingPeriodOption {
  period: SubscriptionBillingPeriod;
  amount: number;
  label: string;
  discountPercent?: number;
}

/** Opções de periodicidade habilitadas no plano retornado pelo servidor. */
export function subscriptionPlanPeriodOptions(plan: SellerSubscriptionPlan): BillingPeriodOption[] {
  const options: BillingPeriodOption[] = [];
  if (plan.monthlyEnabled) {
    options.push({
      period: SubscriptionBillingPeriod.Monthly,
      amount: plan.monthlyAmount,
      label: 'Mensal',
    });
  }
  if (plan.annualEnabled) {
    options.push({
      period: SubscriptionBillingPeriod.Annual,
      amount: plan.annualAmount,
      label: 'Anual',
      discountPercent: plan.annualDiscountPercent,
    });
  }
  return options;
}

export interface SellerSubscriptionMyResponse {
  hasSubscription: boolean;
  storeId?: string | null;
  planId?: string | null;
  planName?: string | null;
  planDescription?: string | null;
  planAmount?: number | null;
  billingPeriod?: SubscriptionBillingPeriod | null;
  planLevel?: number | null;
  nextChargeAmount?: number | null;
  trialDays?: number | null;
  trialEndsAtUtc?: string | null;
  isTrialActive?: boolean;
  hasGatewaySubscription?: boolean;
  hasPendingGatewayCharge?: boolean;
  pendingChargeAmount?: number | null;
  pendingChargeDueDateUtc?: string | null;
  pendingChargeStatus?: string;
  billingStatus?: SellerSubscriptionBillingStatus | null;
  nextDueDateUtc?: string | null;
  lastChargeStatus: string;
  storeBlocked: boolean;
  regularizationMessage: string;
}

export interface SellerSubscriptionChargeHistoryItem {
  gatewayChargeId: string;
  gatewayStatus: string;
  billingStatus: SellerSubscriptionBillingStatus;
  dueDateUtc: string;
  billingPeriodStartUtc?: string | null;
  billingPeriodEndUtc?: string | null;
  paidAtUtc?: string | null;
  amount?: number | null;
  externalReference?: string | null;
  isGatewayPending?: boolean;
  createdAtUtc?: string;
}

/**
 * Plano ativo devolvido por `GET /api/subscriptions/plans`. Preço, trial e
 * benefícios são definidos pelo servidor; o cliente nunca os envia de volta.
 * `amount` é o alias legado do preço mensal.
 */
export interface SellerSubscriptionPlan {
  id: string;
  name: string;
  amount: number;
  monthlyAmount: number;
  annualAmount: number;
  annualDiscountPercent: number;
  monthlyEnabled: boolean;
  annualEnabled: boolean;
  description: string;
  isActive: boolean;
  trialDays: number;
  benefits: string[];
  level: number;
}

/**
 * Contrato mínimo aceito por `POST /api/subscriptions/contract`. Preço,
 * duração do trial e datas são calculados no servidor. A troca é somente
 * upgrade para um plano de nível superior.
 */
export interface ContractSellerSubscriptionRequest {
  storeId: string;
  planId: string;
  billingPeriod?: SubscriptionBillingPeriod;
}

export interface ContractSellerSubscriptionResponse {
  subscriptionId: string;
  storeId: string;
  sellerUserId: string;
  planName: string;
  planDescription: string;
  planAmount: number;
  billingPeriod: SubscriptionBillingPeriod;
  status: SellerSubscriptionBillingStatus;
  startDateUtc: string;
  trialEndsAtUtc?: string | null;
  nextBillingDateUtc: string;
  awaitingGateway?: boolean;
}

export interface AdminPlan {
  id: string;
  name: string;
  amount: number;
  monthlyAmount: number;
  annualAmount: number;
  annualDiscountPercent: number;
  monthlyEnabled: boolean;
  annualEnabled: boolean;
  description: string;
  isActive: boolean;
  trialDays: number;
  benefits: string[];
  level: number;
}

export interface AdminPlansQuery {
  page?: number;
  pageSize?: number;
}

export interface PagedAdminPlans {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  items: AdminPlan[];
}

export interface CreatePlanRequest {
  name: string;
  monthlyAmount: number;
  description: string;
  isActive: boolean;
  trialDays?: number;
  benefits?: string[];
  monthlyEnabled?: boolean;
  annualEnabled?: boolean;
  annualDiscountPercent?: number;
  level?: number;
}

export interface UpdatePlanRequest {
  name: string;
  monthlyAmount?: number;
  description: string;
  trialDays?: number;
  benefits?: string[];
  monthlyEnabled?: boolean;
  annualEnabled?: boolean;
  annualDiscountPercent?: number;
  level?: number;
}

export interface GatewayConfiguration {
  id: string;
  planId: string;
  provider: string;
  environment: string;
  apiBaseUrl: string;
  webhookUrl: string;
  hasApiKey: boolean;
  hasWebhookSecret: boolean;
  isEnabled: boolean;
  updatedAtUtc?: string | null;
}

export interface UpdateGatewayConfigurationRequest {
  provider: string;
  environment: string;
  apiBaseUrl: string;
  webhookUrl: string;
  apiKeySecretReference?: string | null;
  webhookSecretReference?: string | null;
  isEnabled: boolean;
}
