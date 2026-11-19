import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  AdminPlan,
  AdminPlansQuery,
  CreatePlanRequest,
  GatewayConfiguration,
  PagedAdminPlans,
  UpdateGatewayConfigurationRequest,
  UpdatePlanRequest,
} from '../../shared/models/subscription.model';

/**
 * Cliente do catálogo administrativo de planos. A autorização Admin é aplicada
 * pelo servidor; a configuração de gateway é sempre vinculada ao plano e
 * trafega apenas referências a segredos do Vault, nunca os segredos.
 */
@Injectable({ providedIn: 'root' })
export class AdminPlansService {
  private readonly api = inject(ApiService);

  list(query: AdminPlansQuery = {}): Observable<PagedAdminPlans> {
    let params = new HttpParams();
    if (query.page) params = params.set('page', String(query.page));
    if (query.pageSize) params = params.set('pageSize', String(query.pageSize));

    return this.api.get<PagedAdminPlans>('/api/admin/plans', params);
  }

  create(request: CreatePlanRequest): Observable<AdminPlan> {
    return this.api.post<AdminPlan>('/api/admin/plans', request);
  }

  update(planId: string, request: UpdatePlanRequest): Observable<AdminPlan> {
    return this.api.put<AdminPlan>(`/api/admin/plans/${planId}`, request);
  }

  updateStatus(planId: string, isActive: boolean): Observable<AdminPlan> {
    return this.api.patch<AdminPlan>(`/api/admin/plans/${planId}/status`, { isActive });
  }

  getGatewayConfiguration(planId: string): Observable<GatewayConfiguration> {
    return this.api.get<GatewayConfiguration>(
      `/api/admin/plans/${planId}/gateway-configuration`,
    );
  }

  updateGatewayConfiguration(
    planId: string,
    request: UpdateGatewayConfigurationRequest,
  ): Observable<GatewayConfiguration> {
    return this.api.put<GatewayConfiguration>(
      `/api/admin/plans/${planId}/gateway-configuration`,
      request,
    );
  }
}
