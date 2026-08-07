export enum SellerSubscriptionBillingStatus {
  Active = 1,
  DueSoon = 2,
  Overdue = 3,
  Blocked = 4,
  Cancelled = 5,
}

export interface SellerSubscriptionMyResponse {
  hasSubscription: boolean;
  planName?: string | null;
  planAmount?: number | null;
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
  paidAtUtc?: string | null;
  amount?: number | null;
  externalReference?: string | null;
}
