import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * Both wizard previews render a fake storefront header. The `Destaques` string there is a
 * storefront navigation placeholder (a tab, like `Combos` or a seller category), not a product
 * classification label, so it is intentionally preserved. Product classification copy lives in
 * the product editor/list surfaces and uses `Ofertas`, `Mais vendido` and `Novidade` only.
 */
describe('storefront preview tabs are not product classification labels', () => {
  const previews = [
    ['store configuration', 'store-config-page.component.html'],
    ['publish', 'publish/store-publish-page.component.html'],
  ] as const;

  it.each(previews)(
    '%s preview keeps Destaques as a single navigation tab and never as a product classification',
    (_label, file) => {
      const html = readFileSync(resolve(__dirname, file), 'utf8');

      const tabsBlock = html.match(/<div class="preview-tabs">([\s\S]*?)<\/div>/)?.[1] ?? '';
      expect(tabsBlock).toContain('<span class="tab active">Destaques</span>');

      // Exactly one occurrence, inside the navigation tabs above.
      expect(html.match(/Destaque/g) ?? []).toHaveLength(1);

      // The previews never read or render the single special classification.
      expect(html).not.toContain('specialClassification');
      expect(html).not.toContain('tagDestaque');
      expect(html).not.toMatch(/\bOffers\b|\bBestSeller\b/);
    },
  );

  it.each(previews)('%s preview does not promote Destaques inside the product list', (_label, file) => {
    const html = readFileSync(resolve(__dirname, file), 'utf8');

    const productsBlock = html.match(/<div class="preview-products">([\s\S]*?)<div class="preview-cart-bar">/)?.[1] ?? '';
    expect(productsBlock).not.toBe('');
    expect(productsBlock).not.toContain('Destaque');
  });
});
