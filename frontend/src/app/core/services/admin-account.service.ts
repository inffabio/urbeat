import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  AdminAccountSetupRequest,
  AdminAccountSetupResult,
  AdminAccountsQuery,
  AdminAdministrator,
  AdminInvitationDeliveryResponse,
  AdminInvitationValidation,
  InviteAdminAccountResponse,
  PagedAdminAdministrators,
} from '../../shared/models/admin-account.model';

/**
 * Client of the administrative account endpoints. It never builds an authorization decision:
 * the server is authoritative for roles, the last-active-admin invariant and token validity.
 */
@Injectable({ providedIn: 'root' })
export class AdminAccountService {
  private readonly api = inject(ApiService);

  list(query: AdminAccountsQuery = {}): Observable<PagedAdminAdministrators> {
    let params = new HttpParams();
    if (query.status && query.status !== 'all') params = params.set('status', query.status);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.page) params = params.set('page', String(query.page));
    if (query.pageSize) params = params.set('pageSize', String(query.pageSize));

    return this.api.get<PagedAdminAdministrators>('/api/admin/administrators', params);
  }

  invite(email: string): Observable<InviteAdminAccountResponse> {
    return this.api.post<InviteAdminAccountResponse>('/api/admin/administrators', { email });
  }

  updateEmail(id: string, email: string): Observable<AdminAdministrator> {
    return this.api.put<AdminAdministrator>(
      `/api/admin/administrators/${id}/email`,
      { email },
    );
  }

  resendInvitation(id: string): Observable<AdminInvitationDeliveryResponse> {
    return this.api.post<AdminInvitationDeliveryResponse>(
      `/api/admin/administrators/${id}/invitation/resend`,
      {},
    );
  }

  cancelInvitation(id: string): Observable<void> {
    return this.api.delete<void>(`/api/admin/administrators/${id}/invitation`);
  }

  requestPasswordReset(id: string): Observable<AdminInvitationDeliveryResponse> {
    return this.api.post<AdminInvitationDeliveryResponse>(
      `/api/admin/administrators/${id}/password-reset`,
      {},
    );
  }

  block(id: string, reason?: string): Observable<AdminAdministrator> {
    return this.api.post<AdminAdministrator>(
      `/api/admin/administrators/${id}/block`,
      { reason: reason?.trim() || null },
    );
  }

  unblock(id: string): Observable<AdminAdministrator> {
    return this.api.post<AdminAdministrator>(
      `/api/admin/administrators/${id}/unblock`,
      {},
    );
  }

  remove(id: string, reason?: string): Observable<void> {
    const query = reason?.trim() ? `?reason=${encodeURIComponent(reason.trim())}` : '';
    return this.api.delete<void>(`/api/admin/administrators/${id}${query}`);
  }

  validateInvitationToken(token: string): Observable<AdminInvitationValidation> {
    return this.api.post<AdminInvitationValidation>(
      '/api/auth/admin-account/invitation/validate',
      { token },
    );
  }

  acceptInvitation(request: AdminAccountSetupRequest): Observable<AdminAccountSetupResult> {
    return this.api.post<AdminAccountSetupResult>(
      '/api/auth/admin-account/invitation/accept',
      request,
    );
  }

  validatePasswordResetToken(token: string): Observable<AdminInvitationValidation> {
    return this.api.post<AdminInvitationValidation>(
      '/api/auth/admin-account/password-reset/validate',
      { token },
    );
  }

  confirmPasswordReset(request: AdminAccountSetupRequest): Observable<AdminAccountSetupResult> {
    return this.api.post<AdminAccountSetupResult>(
      '/api/auth/admin-account/password-reset/confirm',
      request,
    );
  }
}
