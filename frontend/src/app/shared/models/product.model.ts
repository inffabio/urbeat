export interface ProductCategory {
  id: string;
  storeId: string;
  name: string;
  description?: string;
  displayOrder: number;
  isActive: boolean;
  isFeatured: boolean;
}

export type ProductSaleMode = 'single' | 'size' | 'fixed_weight' | 'variable_weight';

export interface Product {
  id: string;
  storeId: string;
  categoryId: string;
  categoryName: string;
  name: string;
  description: string;
  price: number;
  imageUrl?: string;
  isAvailable: boolean;
  isFeatured: boolean;
  displayOrder: number;
  stockEnabled?: boolean;
  stockQuantity?: number;
  isBestSeller?: boolean;
  isNew?: boolean;
  tagPriority?: string;
  saleMode?: ProductSaleMode;
  createdAtUtc?: string;
  additionals: ProductAdditional[];
  choiceOptions: ProductChoiceOption[];
  variations: ProductVariation[];
  optionGroups: ProductOptionGroup[];
  weightConfig?: ProductWeightConfig | null;
}

export interface ProductWeightConfig {
  id?: string;
  pricePerKg: number;
  minGrams: number;
  maxGrams: number;
  incrementGrams: number;
  isEstimated: boolean;
}

export interface ProductOptionGroup {
  id?: string;
  name: string;
  isRequired: boolean;
  choiceType: 'single' | 'multiple';
  minChoices: number;
  maxChoices: number;
  displayOrder: number;
  items: ProductOptionItem[];
}

export interface ProductOptionItem {
  id?: string;
  name: string;
  price: number;
  displayOrder: number;
}

export interface ProductAdditional {
  id: string;
  name: string;
  price: number;
  isActive: boolean;
  isRequired: boolean;
  displayOrder: number;
}

export interface StoreAdditionalGroup {
  id: string;
  name: string;
  isActive: boolean;
}

export interface StoreAdditional {
  id: string;
  storeId: string;
  groupId: string;
  groupName: string;
  name: string;
  description: string;
  price: number;
  isActive: boolean;
  displayOrder: number;
  productCount: number;
}

export interface StoreAdditionalRequest {
  name: string;
  description?: string;
  groupId: string;
  price: number;
  isActive: boolean;
  displayOrder: number;
}

export interface ProductChoiceOption {
  id: string;
  name: string;
  price: number;
  isActive: boolean;
  isRequired: boolean;
  displayOrder: number;
}

export interface ProductVariation {
  id: string;
  name: string;
  description?: string;
  weightGrams?: number | null;
  price: number;
  isDefault?: boolean;
  isActive: boolean;
  isRequired: boolean;
  displayOrder: number;
}
