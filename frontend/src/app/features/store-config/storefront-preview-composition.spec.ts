import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

describe('storefront preview banner/logo composition', () => {
  const configCss = () => readFileSync(resolve(__dirname, 'store-config-page.component.scss'), 'utf8');
  const publishCss = () => readFileSync(resolve(__dirname, 'publish/store-publish-page.component.scss'), 'utf8');

  function ruleBody(css: string, selector: string): string {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return css.match(new RegExp(`${escaped}\\s*\\{([\\s\\S]*?)\\}`))?.[1] ?? '';
  }

  it('keeps each phone preview banner at its existing scaled height with a full-bleed cover image', () => {
    const configBanner = ruleBody(configCss(), '.preview-banner');
    const publishBanner = ruleBody(publishCss(), '.preview-banner');
    const configBannerImg = ruleBody(configCss(), '.preview-banner img');

    expect(configBanner).toMatch(/height:\s*150px/);
    expect(configBanner).toMatch(/overflow:\s*hidden/);
    expect(publishBanner).toMatch(/height:\s*150px/);
    expect(configBannerImg).toMatch(/width:\s*100%/);
    expect(configBannerImg).toMatch(/height:\s*100%/);
    expect(configBannerImg).toMatch(/object-fit:\s*cover/);
    expect(publishCss()).toMatch(/\.preview-banner\s*\{[\s\S]*?img\s*\{[^}]*object-fit:\s*cover/);
  });

  it('centers the store-config preview logo on the banner bottom edge so half overhangs the banner', () => {
    const css = configCss();
    const header = ruleBody(css, '.preview-store-header');
    const wrap = ruleBody(css, '.store-logo-wrap');

    expect(header).toMatch(/margin-top:\s*-48px/);
    expect(header).toMatch(/position:\s*relative/);
    expect(wrap).toMatch(/width:\s*96px/);
    expect(wrap).toMatch(/height:\s*96px/);
  });

  it('centers the publish preview logo on the banner bottom edge so half overhangs the banner', () => {
    const css = publishCss();
    const header = ruleBody(css, '.preview-store-header');
    const wrap = ruleBody(css, '.store-logo-wrap');

    expect(header).toMatch(/margin-top:\s*-48px/);
    expect(wrap).toMatch(/width:\s*96px/);
    expect(wrap).toMatch(/height:\s*96px/);
  });

  it.each([
    ['store configuration', 'store-config-page.component.scss'],
    ['publish', 'publish/store-publish-page.component.scss'],
  ])('frames the %s preview logo in a white ring and fits artwork with contain', (_label, file) => {
    const css = readFileSync(resolve(__dirname, file), 'utf8');
    const wrap = ruleBody(css, '.store-logo-wrap');
    const img = ruleBody(css, '.store-logo-img');

    expect(wrap).toMatch(/border-radius:\s*50%/);
    expect(wrap).toMatch(/border:\s*3px solid var\(--app-surface\)/);
    expect(wrap).toMatch(/overflow:\s*hidden/);
    expect(wrap).toMatch(/background:\s*var\(--app-surface\)/);
    expect(img).toMatch(/object-fit:\s*contain/);
    expect(img).not.toMatch(/object-fit:\s*cover/);
    expect(img).toMatch(/border-radius:\s*50%/);
  });
});
