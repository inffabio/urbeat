import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SellerSubscriptionChargeHistoryItem, SellerSubscriptionMyResponse } from '../../shared/models/subscription.model';
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
}
