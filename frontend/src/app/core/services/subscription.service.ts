import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ContractSellerSubscriptionRequest,
  ContractSellerSubscriptionResponse,
  SellerSubscriptionChargeHistoryItem,
  SellerSubscriptionMyResponse,
  SellerSubscriptionPlan,
} from '../../shared/models/subscription.model';
import { ApiService } from './api.service';

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private readonly api = inject(ApiService);

  getMySubscription(): Observable<SellerSubscriptionMyResponse> {
    return this.api.get<SellerSubscriptionMyResponse>('/api/subscriptions/my');
  }

  listMyCharges(): Observable<SellerSubscriptionChargeHistoryItem[]> {
    return this.api.get<SellerSubscriptionChargeHistoryItem[]>('/api/subscriptions/my/charges');
  }

  /** Planos ativos disponíveis para contratação. O backend filtra por `IsActive`. */
  listPlans(): Observable<SellerSubscriptionPlan[]> {
    return this.api.get<SellerSubscriptionPlan[]>('/api/subscriptions/plans');
  }

  /**
   * Contrata um plano para a própria loja. Apenas `storeId` e `planId` são
   * enviados: o servidor valida o ownership, o plano ativo, o preço e o trial.
   */
  contract(
    request: ContractSellerSubscriptionRequest,
  ): Observable<ContractSellerSubscriptionResponse> {
    return this.api.post<ContractSellerSubscriptionResponse>('/api/subscriptions/contract', request);
  }
}
