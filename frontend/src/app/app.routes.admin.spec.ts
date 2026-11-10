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

    expect(childPaths).toEqual(['', 'visao-geral', 'operacoes', 'planos', 'configuracoes']);
    expect(childPaths).not.toContain('dashboard');
    expect(childPaths).not.toContain('pedidos');
  });

  it('keeps /painel/login as the unauthenticated entry point', () => {
    const loginRoute = routes.find((route) => route.path === 'painel/login');

    expect(loginRoute).toBeDefined();
    expect(loginRoute?.canActivate).toBeUndefined();
  });
});
