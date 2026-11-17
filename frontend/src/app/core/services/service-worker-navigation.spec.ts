import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { Generator, type Config, type Filesystem } from '@angular/service-worker/config';

/**
 * Locks the contract between the deployed `ngsw-config.json` and the service
 * worker request handling that the storefront relies on:
 *
 * - storefront routes are served as navigations from the prefetched
 *   `index.html`, so `/loja/carrinho` never depends on the network or on the
 *   HTTP cache;
 * - every root chunk is prefetched, so a route change can open its lazy
 *   component while the document is served from the service worker;
 * - API responses are never cached, so prices, totals and order transitions
 *   stay authoritative on the server.
 *
 * The manifest is produced by Angular's own generator, not by a copy of its
 * matching rules, so a change in the config that would break these invariants
 * fails here instead of in production as a blank route.
 */
const DEPLOYED_FILES = [
  '/index.html',
  '/main-ABC123.js',
  '/chunk-CART.js',
  '/chunk-CHECKOUT.js',
  '/styles-XYZ.css',
  '/favicon.ico',
  '/manifest.webmanifest',
  '/assets/logo_urbeat.png',
  '/images/hero.webp',
];

function createMemoryFilesystem(files: readonly string[]): Filesystem {
  const contents = new Map(files.map((file) => [file, `content:${file}`]));
  return {
    list: async () => [...files],
    read: async (file) => contents.get(file) ?? '',
    hash: async (file) => createHash('sha1').update(contents.get(file) ?? '').digest('hex'),
    write: async () => undefined,
  };
}

function readNgswConfig(): Config {
  const path = resolve(process.cwd(), 'ngsw-config.json');
  return JSON.parse(readFileSync(path, 'utf8')) as Config;
}

interface NavigationUrlSpec {
  positive: boolean;
  regex: string;
}

interface GeneratedManifest {
  index: string;
  navigationUrls: NavigationUrlSpec[];
  assetGroups: Array<{ name: string; installMode: string; urls: string[] }>;
  dataGroups: unknown[];
}

async function generateManifest(): Promise<GeneratedManifest> {
  const generator = new Generator(createMemoryFilesystem(DEPLOYED_FILES), '/');
  return (await generator.process(readNgswConfig())) as unknown as GeneratedManifest;
}

function isNavigationRequest(
  manifest: GeneratedManifest,
  urlWithoutQueryOrHash: string,
): boolean {
  const included = manifest.navigationUrls
    .filter((spec) => spec.positive)
    .some((spec) => new RegExp(spec.regex).test(urlWithoutQueryOrHash));
  const excluded = manifest.navigationUrls
    .filter((spec) => !spec.positive)
    .some((spec) => new RegExp(spec.regex).test(urlWithoutQueryOrHash));
  return included && !excluded;
}

describe('ngsw navigation handling', () => {
  it('serves storefront routes as navigations from the cached index document', async () => {
    const manifest = await generateManifest();

    expect(manifest.index).toBe('/index.html');
    for (const url of [
      '/loja',
      '/loja/carrinho',
      '/loja/checkout/cadastro',
      '/loja/checkout/pagamento',
      '/loja/produto/42',
      '/loja/pedido/abc',
    ]) {
      expect(isNavigationRequest(manifest, url)).toBe(true);
    }
  });

  it('does not treat deployed assets as navigations', async () => {
    const manifest = await generateManifest();

    for (const url of ['/main-ABC123.js', '/chunk-CART.js', '/ngsw.json', '/styles-XYZ.css']) {
      expect(isNavigationRequest(manifest, url)).toBe(false);
    }
  });

  it('prefetches the document and every root chunk so a route change works offline', async () => {
    const manifest = await generateManifest();
    const appGroup = manifest.assetGroups.find((group) => group.name === 'app');

    expect(appGroup?.installMode).toBe('prefetch');
    expect(appGroup?.urls).toEqual(
      expect.arrayContaining([
        '/index.html',
        '/main-ABC123.js',
        '/chunk-CART.js',
        '/chunk-CHECKOUT.js',
      ]),
    );
  });

  it('never caches API traffic, which stays authoritative on the server', async () => {
    const manifest = await generateManifest();

    expect(manifest.dataGroups).toEqual([]);
    for (const group of manifest.assetGroups) {
      expect(group.urls.some((url) => url.startsWith('/api'))).toBe(false);
    }
  });
});
