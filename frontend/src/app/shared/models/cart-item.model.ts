export interface CartItem {
  id?: string; // unique frontend id to differentiate same product with diff options
  productId: string;
  productName: string;
  productImage?: string;
  productDescription?: string;
  quantity: number;
  unitPrice: number;
  notes?: string;
  
  variationId?: string;
  variationName?: string;

  weightGrams?: number;

  choiceOptionId?: string;
  choiceOptionName?: string;
  
  additionalIds?: string[];
  additionalNames?: string[];

  optionGroups?: CartItemOptionGroup[];
}

export interface CartItemOptionGroup {
  groupId: string;
  groupName: string;
  itemIds: string[];
  itemNames: string[];
}
