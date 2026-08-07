import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PaymentResponse, PaymentHistoryEntry } from '../../shared/models/payment.model';

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private readonly api = inject(ApiService);

  createPayment(orderId: string): Observable<PaymentResponse> {
    return this.api.post<PaymentResponse>('/api/payments/order', { orderId });
  }

  getPayment(orderId: string): Observable<PaymentResponse> {
    return this.api.get<PaymentResponse>(`/api/payments/order/${orderId}`);
  }

  getPaymentHistory(orderId: string): Observable<PaymentHistoryEntry[]> {
    return this.api.get<PaymentHistoryEntry[]>(`/api/payments/order/${orderId}/history`);
  }
}
