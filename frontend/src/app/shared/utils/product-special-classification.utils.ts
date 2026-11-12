import { Product, ProductSpecialClassification } from '../models/product.model';

/** Classificações especiais na ordem fixa aprovada para as vitrines. */
export const PRODUCT_SPECIAL_CLASSIFICATIONS: readonly ProductSpecialClassification[] = [
  'Offers',
  'BestSeller',
  'New',
];

/** Rótulo administrativo (singular) usado em editor, etiquetas e filtros. */
export const PRODUCT_SPECIAL_CLASSIFICATION_LABELS: Record<ProductSpecialClassification, string> = {
  Offers: 'Ofertas',
  BestSeller: 'Mais vendido',
  New: 'Novidade',
};

/** Rótulo das pílulas/seções do storefront. */
export const PRODUCT_SPECIAL_SECTION_LABELS: Record<ProductSpecialClassification, string> = {
  Offers: 'Ofertas',
  BestSeller: 'Mais vendidos',
  New: 'Novidades',
};

export function isProductSpecialClassification(value: unknown): value is ProductSpecialClassification {
  return value === 'Offers' || value === 'BestSeller' || value === 'New';
}

/**
 * Resolve a classificação autoritativa do produto. Os booleanos legados são lidos apenas
 * como fallback de transição, respeitando a precedência Offers > BestSeller > New.
 */
export function resolveProductSpecialClassification(
  product: Pick<Product, 'specialClassification' | 'isFeatured' | 'isBestSeller' | 'isNew'>,
): ProductSpecialClassification | null {
  if (isProductSpecialClassification(product.specialClassification)) return product.specialClassification;
  if (product.isFeatured) return 'Offers';
  if (product.isBestSeller) return 'BestSeller';
  if (product.isNew) return 'New';
  return null;
}
