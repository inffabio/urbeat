export interface PublicPlanOfferItem {
  id: string;
  name: string;
  amount: number;
  description: string;
  trialDays: number;
  benefits: string[];
}

export interface PublicPlanOffer {
  plans: PublicPlanOfferItem[];
}
