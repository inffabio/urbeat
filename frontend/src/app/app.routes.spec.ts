import { authChildGuard, authGuard } from './core/guards/auth.guard';
import { routes } from './app.routes';

describe('app routes', () => {
  it('declares /app before the public store slug route', () => {
    const appIndex = routes.findIndex((route) => route.path === 'app');
    const storeIndex = routes.findIndex((route) => route.path === ':storePath');

    expect(appIndex).toBeGreaterThanOrEqual(0);
    expect(storeIndex).toBeGreaterThanOrEqual(0);
    expect(appIndex).toBeLessThan(storeIndex);
  });

  it('protects /app children with the seller auth guard', () => {
    const appRoute = routes.find((route) => route.path === 'app');

    expect(appRoute?.canActivate).toEqual([authGuard]);
    expect(appRoute?.canActivateChild).toEqual([authChildGuard]);
  });

  it('configurar-loja children load their own components', () => {
    const legacyRoute = routes.find((route) => route.path === 'configurar-loja' && route.children);
    const children = legacyRoute?.children ?? [];
    expect(children.length).toBeGreaterThanOrEqual(5);
    expect(children.some((child) => child.path === 'horarios' && !!child.loadComponent)).toBe(true);
    expect(children.some((child) => child.path === 'produtos' && !!child.loadComponent)).toBe(true);
    expect(children.some((child) => child.path === 'publicar' && !!child.loadComponent)).toBe(true);
  });

  it('declares seller dashboard child routes for all documented dashboard screens', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const childPaths = appRoute?.children?.map((child) => child.path) ?? [];

    expect(childPaths).toContain('cardapio');
    expect(childPaths).toContain('cardapio/produtos');
    expect(childPaths).toContain('cardapio/categorias');
    expect(childPaths).toContain('cardapio/adicionais');
    expect(childPaths).toContain('configuracoes/horarios');
    expect(childPaths).toContain('configuracoes/bio');
    expect(childPaths).toContain('configuracoes/bairros');
    expect(childPaths).toContain('mensalidade');
    expect(childPaths).toContain('clientes');
    expect(childPaths).toContain('avaliacoes');
    expect(childPaths).toContain('marketing');
    expect(childPaths).toContain('instalar');
  });

  it('routes /app/instalar to the real seller install page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const installRoute = appRoute?.children?.find((child) => child.path === 'instalar');

    expect(installRoute?.loadComponent?.toString()).toContain('seller-install-page.component');
  });

  it('routes /app/marketing to the seller marketing page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const marketingRoute = appRoute?.children?.find((child) => child.path === 'marketing');

    expect(marketingRoute?.loadComponent?.toString()).toContain('seller-marketing-page.component');
  });

  it('routes /app/avaliacoes to the real seller reviews page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const reviewsRoute = appRoute?.children?.find((child) => child.path === 'avaliacoes');

    expect(reviewsRoute?.loadComponent?.toString()).toContain('seller-reviews-page.component');
  });

  it('routes /app/clientes to the real seller customers page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const customersRoute = appRoute?.children?.find((child) => child.path === 'clientes');

    expect(customersRoute?.loadComponent?.toString()).toContain('seller-customers-page.component');
  });

  it('routes /app/mensalidade to the real seller subscription page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const subscriptionRoute = appRoute?.children?.find((child) => child.path === 'mensalidade');

    expect(subscriptionRoute?.loadComponent?.toString()).toContain('seller-subscription-page.component');
  });

  it('routes /app/cardapio to products and /app/cardapio/categorias to the dedicated categories manager', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const menuRoute = appRoute?.children?.find((child) => child.path === 'cardapio');
    const categoriesRoute = appRoute?.children?.find((child) => child.path === 'cardapio/categorias');

    expect(menuRoute?.redirectTo).toBe('cardapio/produtos');
    expect(categoriesRoute?.loadComponent?.toString()).toContain('seller-categories-page.component');
  });

  it('routes /app/cardapio/adicionais to the dedicated seller additionals page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const additionalsRoute = appRoute?.children?.find((child) => child.path === 'cardapio/adicionais');

    expect(additionalsRoute?.loadComponent?.toString()).toContain('seller-additionals-page.component');
  });

  it('routes seller dashboard config and menu pages to dedicated seller components instead of wizard pages', () => {
    const appRoute = routes.find((route) => route.path === 'app');

    expect(appRoute?.children?.find((child) => child.path === 'cardapio/produtos')?.loadComponent?.toString()).toContain('seller-products-page.component');
    expect(appRoute?.children?.find((child) => child.path === 'configuracoes/informacoes')?.loadComponent?.toString()).toContain('seller-store-info-page.component');
    expect(appRoute?.children?.find((child) => child.path === 'configuracoes/horarios')?.loadComponent?.toString()).toContain('seller-hours-page.component');
    expect(appRoute?.children?.find((child) => child.path === 'configuracoes/bairros')?.loadComponent?.toString()).toContain('seller-neighborhoods-page.component');
  });

  it('routes /app/configuracoes/bio to the dedicated bio page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const bioRoute = appRoute?.children?.find((child) => child.path === 'configuracoes/bio');

    expect(bioRoute?.loadComponent?.toString()).toContain('seller-bio-page.component');
  });

  it('routes /app/pedidos to the real seller orders page', () => {
    const appRoute = routes.find((route) => route.path === 'app');
    const ordersRoute = appRoute?.children?.find((child) => child.path === 'pedidos');

    expect(ordersRoute?.loadComponent?.toString()).toContain('seller-orders-page.component');
  });
});
