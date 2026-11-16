import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { AdminStoresService } from './admin-stores.service';
import { AuthService } from './auth.service';
import { AdminStoreSessionService } from './admin-store-session.service';
import { AdminStoreContext, AdminStoreContextMetadata } from '../../shared/models/admin-store.model';

const STORAGE_KEY = 'urbeat_admin_store_context';
const ADMIN_TOKEN = `header.${btoa(JSON.stringify({ role: 'Admin' }))}.signature`;
const SELLER_TOKEN = `header.${btoa(JSON.stringify({ role: 'Seller' }))}.signature`;

const context: AdminStoreContext = {
  contextId: 'context-1',
  adminUserId: 'admin-1',
  storeId: 'store-1',
  storeName: 'Pizza Hunter',
  storeSlug: 'pizza-hunter',
  expiresAtUtc: new Date(Date.now() + 30 * 60_000).toISOString(),
  token: 'opaque-token',
};

const metadata: AdminStoreContextMetadata = {
  contextId: 'context-1',
  adminUserId: 'admin-1',
  storeId: 'store-1',
  storeName: 'Pizza Hunter',
  storeSlug: 'pizza-hunter',
  expiresAtUtc: context.expiresAtUtc,
};

describe('AdminStoreSessionService', () => {
  let stores: { getContext: jest.Mock; closeContext: jest.Mock };
  let auth: { token: jest.Mock<string | null, []> };

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    stores = {
      getContext: jest.fn(() => of(metadata)),
      closeContext: jest.fn(() => of(void 0)),
    };
    auth = { token: jest.fn(() => ADMIN_TOKEN) };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AdminStoresService, useValue: stores },
        { provide: AuthService, useValue: auth },
      ],
    });
  });

  function createService(): AdminStoreSessionService {
    return TestBed.inject(AdminStoreSessionService);
  }

  it('stores the session only in sessionStorage and keeps the return url inside /admin/lojas', () => {
    const service = createService();

    service.start(context, '/admin/lojas?page=2&search=pizza');

    const stored = JSON.parse(sessionStorage.getItem(STORAGE_KEY)!);
    expect(stored.token).toBe('opaque-token');
    expect(stored.returnUrl).toBe('/admin/lojas?page=2&search=pizza');
    expect(service.session()?.storeName).toBe('Pizza Hunter');
    expect(service.isActive()).toBe(true);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('discards a return url outside /admin/lojas', () => {
    const service = createService();

    service.start(context, 'https://evil.example/app');

    expect(service.session()?.returnUrl).toBe('/admin/lojas');
  });

  it('does not expose the context token for a seller session in the same tab', () => {
    const service = createService();
    service.start(context, '/admin/lojas');

    auth.token.mockReturnValue(SELLER_TOKEN);

    expect(service.isAdminMode()).toBe(false);
    expect(service.contextToken()).toBeNull();
  });

  it('preserves the expired banner state when restoration is rejected as expired', async () => {
    stores.getContext.mockReturnValue(
      throwError(() => ({ status: 403, error: { code: 'ADMIN_STORE_CONTEXT_EXPIRED' } })),
    );
    const service = createService();
    service.start(context, '/admin/lojas');

    await expect(service.ensureRestored()).resolves.toBe(false);

    expect(service.session()?.storeName).toBe('Pizza Hunter');
    expect(service.isExpired()).toBe(true);
    expect(service.isActive()).toBe(false);
  });

  it('restores and validates the stored session once for concurrent callers', async () => {
    const pending = new Subject<AdminStoreContextMetadata>();
    stores.getContext.mockReturnValue(pending.asObservable());
    const service = createService();
    service.start(context, '/admin/lojas');

    const first = service.ensureRestored();
    const second = service.ensureRestored();
    expect(stores.getContext).toHaveBeenCalledTimes(1);

    pending.next(metadata);
    pending.complete();

    await expect(first).resolves.toBe(true);
    await expect(second).resolves.toBe(true);
    expect(service.session()?.expiresAtUtc).toBe(metadata.expiresAtUtc);
  });

  it('clears the session when server validation fails', async () => {
    stores.getContext.mockReturnValue(throwError(() => ({ status: 403 })));
    const service = createService();
    service.start(context, '/admin/lojas');

    await expect(service.ensureRestored()).resolves.toBe(false);

    expect(service.session()).toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('marks the session expired when the TTL elapses and removes the stored key', () => {
    jest.useFakeTimers();
    try {
      const service = createService();
      service.start(
        { ...context, expiresAtUtc: new Date(Date.now() + 1000).toISOString() },
        '/admin/lojas',
      );

      jest.advanceTimersByTime(1500);

      expect(service.isExpired()).toBe(true);
      expect(service.isActive()).toBe(false);
      expect(service.session()?.storeName).toBe('Pizza Hunter');
      expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
    } finally {
      jest.useRealTimers();
    }
  });

  it('closes the context and always clears local state, even when the request fails', () => {
    stores.closeContext.mockReturnValue(throwError(() => ({ status: 500 })));
    const service = createService();
    service.start(context, '/admin/lojas');

    service.end();

    expect(stores.closeContext).toHaveBeenCalledWith('store-1', 'opaque-token');
    expect(service.session()).toBeNull();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('ignores ADMIN_STORE_ACTION_NOT_ALLOWED and invalidates on context codes', () => {
    const service = createService();
    service.start(context, '/admin/lojas');
    service.markInvalid('ADMIN_STORE_ACTION_NOT_ALLOWED');
    expect(service.isActive()).toBe(true);

    service.markInvalid('ADMIN_STORE_CONTEXT_STORE_MISMATCH');
    expect(service.session()).toBeNull();
    expect(service.isActive()).toBe(false);
  });

  it('keeps the store name in the expired banner state', () => {
    const service = createService();
    service.start(context, '/admin/lojas');

    service.markInvalid('ADMIN_STORE_CONTEXT_EXPIRED');

    expect(service.isExpired()).toBe(true);
    expect(service.isActive()).toBe(false);
    expect(service.session()?.storeName).toBe('Pizza Hunter');
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });
});
