import { Injectable, inject } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  SKIP_GLOBAL_ERROR_TOAST,
  SKIP_SERVER_ACCESS_OVERLAY,
} from '../interceptors/server-access.context';
import { PaymentResponse, PaymentHistoryEntry } from '../../shared/models/payment.model';

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private readonly api = inject(ApiService);

  createPayment(orderId: string): Observable<PaymentResponse> {
    return this.api.post<PaymentResponse>('/api/payments/order', { orderId });
  }

  getPayment(
    orderId: string,
    options?: { background?: boolean },
  ): Observable<PaymentResponse> {
    // Automatic gateway confirmation polling must retry silently instead of
    // surfacing a transient server error as a fatal toast.
    const context = options?.background
      ? new HttpContext()
          .set(SKIP_SERVER_ACCESS_OVERLAY, true)
          .set(SKIP_GLOBAL_ERROR_TOAST, true)
      : undefined;
    return this.api.get<PaymentResponse>(`/api/payments/order/${orderId}`, undefined, context);
  }

  getPaymentHistory(orderId: string): Observable<PaymentHistoryEntry[]> {
    return this.api.get<PaymentHistoryEntry[]>(`/api/payments/order/${orderId}/history`);
  }
}
