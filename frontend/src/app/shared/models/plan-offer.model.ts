import { SubscriptionBillingPeriod } from './subscription.model';

export interface PublicPlanOfferItem {
  id: string;
  name: string;
  /** Alias legado do preço mensal. */
  amount: number;
  monthlyAmount: number;
  annualAmount: number;
  annualDiscountPercent: number;
  monthlyEnabled: boolean;
  annualEnabled: boolean;
  description: string;
  trialDays: number;
  benefits: string[];
  level: number;
}

export interface PublicPlanOffer {
  plans: PublicPlanOfferItem[];
}

export interface PlanPeriodOption {
  period: SubscriptionBillingPeriod;
  amount: number;
  label: string;
  discountPercent?: number;
}

/** Monta as opções de periodicidade habilitadas pelo catálogo autoritativo. */
export function planPeriodOptions(plan: PublicPlanOfferItem): PlanPeriodOption[] {
  const options: PlanPeriodOption[] = [];
  if (plan.monthlyEnabled) {
    options.push({ period: SubscriptionBillingPeriod.Monthly, amount: plan.monthlyAmount, label: 'Mensal' });
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
