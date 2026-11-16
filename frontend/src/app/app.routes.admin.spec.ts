import { routes } from './app.routes';
import { adminGuard } from './core/guards/admin.guard';

describe('admin routes', () => {
  const adminIndex = routes.findIndex((route) => route.path === 'admin');
  const storePathIndex = routes.findIndex((route) => route.path === ':storePath');

  it('declares /admin before the store catch-all route', () => {
    expect(adminIndex).toBeGreaterThanOrEqual(0);
    expect(storePathIndex).toBeGreaterThanOrEqual(0);
    expect(adminIndex).toBeLessThan(storePathIndex);
  });

  it('protects the admin area with the admin guard', () => {
    const admin = routes[adminIndex];

    expect(admin.canActivate).toContain(adminGuard);
    expect(admin.canActivateChild).toContain(adminGuard);
  });

  it('exposes only the administrative children', () => {
    const admin = routes[adminIndex];
    const childPaths = (admin.children ?? []).map((child) => child.path);

    expect(childPaths).toEqual(['', 'visao-geral', 'lojas', 'operacoes', 'planos', 'configuracoes']);
    expect(childPaths).not.toContain('dashboard');
    expect(childPaths).not.toContain('pedidos');
  });

  it('keeps /painel/login as the unauthenticated entry point', () => {
    const loginRoute = routes.find((route) => route.path === 'painel/login');

    expect(loginRoute).toBeDefined();
    expect(loginRoute?.canActivate).toBeUndefined();
  });

  it('declares the public administrative link pages before the store catch-all and without guards', () => {
    const inviteIndex = routes.findIndex((route) => route.path === 'convite-admin');
    const resetIndex = routes.findIndex((route) => route.path === 'redefinir-senha-admin');

    expect(inviteIndex).toBeGreaterThanOrEqual(0);
    expect(resetIndex).toBeGreaterThanOrEqual(0);
    expect(inviteIndex).toBeLessThan(storePathIndex);
    expect(resetIndex).toBeLessThan(storePathIndex);
    expect(routes[inviteIndex].canActivate).toBeUndefined();
    expect(routes[resetIndex].canActivate).toBeUndefined();
  });
});
