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

export interface SellerSubscriptionMyResponse {
  hasSubscription: boolean;
  storeId?: string | null;
  planId?: string | null;
  planName?: string | null;
  planAmount?: number | null;
  nextChargeAmount?: number | null;
  trialDays?: number | null;
  trialEndsAtUtc?: string | null;
  isTrialActive?: boolean;
  hasGatewaySubscription?: boolean;
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
}

/**
 * Plano ativo devolvido por `GET /api/subscriptions/plans`. Preço, trial e
 * benefícios são definidos pelo servidor; o cliente nunca os envia de volta.
 */
export interface SellerSubscriptionPlan {
  id: string;
  name: string;
  amount: number;
  description: string;
  isActive: boolean;
  trialDays: number;
  benefits: string[];
}

/**
 * Contrato mínimo aceito por `POST /api/subscriptions/contract`. Preço,
 * duração do trial e datas são calculados no servidor.
 */
export interface ContractSellerSubscriptionRequest {
  storeId: string;
  planId: string;
}

export interface ContractSellerSubscriptionResponse {
  subscriptionId: string;
  storeId: string;
  sellerUserId: string;
  planName: string;
  planAmount: number;
  status: SellerSubscriptionBillingStatus;
  startDateUtc: string;
  nextBillingDateUtc: string;
}
