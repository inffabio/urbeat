import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpParams } from '@angular/common/http';
import { of } from 'rxjs';
import { ApiService } from './api.service';
import { AdminStoresService } from './admin-stores.service';
import {
  AdminStoreContext,
  AdminStoreContextMetadata,
  PagedAdminStoreSummary,
} from '../../shared/models/admin-store.model';

const context: AdminStoreContext = {
  contextId: 'context-1',
  adminUserId: 'admin-1',
  storeId: 'store-1',
  storeName: 'Pizza Hunter',
  storeSlug: 'pizza-hunter',
  expiresAtUtc: '2026-11-16T12:30:00Z',
  token: 'opaque-token',
};

const metadata: AdminStoreContextMetadata = {
  contextId: 'context-1',
  adminUserId: 'admin-1',
  storeId: 'store-1',
  storeName: 'Pizza Hunter',
  storeSlug: 'pizza-hunter',
  expiresAtUtc: '2026-11-16T12:30:00Z',
};

describe('AdminStoresService', () => {
  let api: {
    get: jest.Mock;
    post: jest.Mock;
    put: jest.Mock;
    patch: jest.Mock;
    delete: jest.Mock;
  };
  let http: { get: jest.Mock; delete: jest.Mock };
  let service: AdminStoresService;

  beforeEach(() => {
    api = {
      get: jest.fn(() => of({ items: [] } as unknown as PagedAdminStoreSummary)),
      post: jest.fn(() => of(context)),
      put: jest.fn(),
      patch: jest.fn(),
      delete: jest.fn(() => of(void 0)),
    };
    http = {
      get: jest.fn(() => of(metadata)),
      delete: jest.fn(() => of(void 0)),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: ApiService, useValue: api },
        { provide: HttpClient, useValue: http },
      ],
    });

    service = TestBed.inject(AdminStoresService);
  });

  it('lists stores with the documented query parameters', () => {
    service
      .list({
        search: ' pizza ',
        status: 'onboarding',
        publication: 'draft',
        openState: 'closed',
        subscription: 'none',
        sort: 'name_desc',
        page: 2,
        pageSize: 50,
      })
      .subscribe();

    expect(api.get).toHaveBeenCalledWith('/api/admin/stores', expect.any(HttpParams));
    const params = api.get.mock.calls[0][1] as HttpParams;
    expect(params.get('search')).toBe('pizza');
    expect(params.get('status')).toBe('onboarding');
    expect(params.get('publication')).toBe('draft');
    expect(params.get('openState')).toBe('closed');
    expect(params.get('subscription')).toBe('none');
    expect(params.get('sort')).toBe('name_desc');
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('50');
  });

  it('omits "all" filters and blank searches', () => {
    service.list({ status: 'all', publication: 'all', openState: 'all', subscription: 'all', search: '  ' }).subscribe();

    const params = api.get.mock.calls[0][1] as HttpParams;
    expect(params.keys()).not.toContain('status');
    expect(params.keys()).not.toContain('publication');
    expect(params.keys()).not.toContain('openState');
    expect(params.keys()).not.toContain('subscription');
    expect(params.keys()).not.toContain('search');
  });

  it('opens a context with an optional sanitized reason', () => {
    service.openContext('store-1', '  Chamado 4821 ').subscribe();
    expect(api.post).toHaveBeenCalledWith('/api/admin/stores/store-1/access-context', {
      reason: 'Chamado 4821',
    });

    service.openContext('store-1').subscribe();
    expect(api.post).toHaveBeenLastCalledWith('/api/admin/stores/store-1/access-context', {
      reason: null,
    });
  });

  it('reads and closes the context with the explicit header', () => {
    service.getContext('store-1', 'opaque-token').subscribe();
    service.closeContext('store-1', 'opaque-token').subscribe();

    expect(http.get).toHaveBeenCalledWith(
      '/api/admin/stores/store-1/access-context',
      { headers: { 'X-Admin-Store-Context': 'opaque-token' } },
    );
    expect(http.delete).toHaveBeenCalledWith(
      '/api/admin/stores/store-1/access-context',
      { headers: { 'X-Admin-Store-Context': 'opaque-token' } },
    );
  });
});
