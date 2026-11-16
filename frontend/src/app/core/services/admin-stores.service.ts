import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiService } from './api.service';
import {
  AdminStoreContext,
  AdminStoreContextMetadata,
  AdminStoresQuery,
  PagedAdminStoreSummary,
} from '../../shared/models/admin-store.model';

/**
 * Client of the administrative store endpoints. Authorization is decided by the server; this
 * service only transports the opaque context token on the explicit context calls (the global
 * interceptor never adds it to `/api/admin/` requests).
 */
@Injectable({ providedIn: 'root' })
export class AdminStoresService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  list(query: AdminStoresQuery = {}): Observable<PagedAdminStoreSummary> {
    let params = new HttpParams();
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.status && query.status !== 'all') params = params.set('status', query.status);
    if (query.publication && query.publication !== 'all') params = params.set('publication', query.publication);
    if (query.openState && query.openState !== 'all') params = params.set('openState', query.openState);
    if (query.subscription && query.subscription !== 'all') params = params.set('subscription', query.subscription);
    if (query.sort) params = params.set('sort', query.sort);
    if (query.page) params = params.set('page', String(query.page));
    if (query.pageSize) params = params.set('pageSize', String(query.pageSize));

    return this.api.get<PagedAdminStoreSummary>('/api/admin/stores', params);
  }

  openContext(storeId: string, reason?: string): Observable<AdminStoreContext> {
    return this.api.post<AdminStoreContext>(`/api/admin/stores/${storeId}/access-context`, {
      reason: reason?.trim() || null,
    });
  }

  getContext(storeId: string, token: string): Observable<AdminStoreContextMetadata> {
    return this.http.get<AdminStoreContextMetadata>(
      `${this.baseUrl}/api/admin/stores/${storeId}/access-context`,
      { headers: { 'X-Admin-Store-Context': token } },
    );
  }

  closeContext(storeId: string, token: string): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/admin/stores/${storeId}/access-context`,
      { headers: { 'X-Admin-Store-Context': token } },
    );
  }
}
