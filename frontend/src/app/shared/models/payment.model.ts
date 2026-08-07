import { PaymentMethod } from '../enums/payment-method.enum';
import { PaymentStatus } from '../enums/payment-status.enum';

export interface PaymentResponse {
  paymentId: string;
  orderId: string;
  gateway: number; // 1 = MercadoPago
  gatewayTransactionId: string;
  gatewayCheckoutUrl: string;
  method: PaymentMethod;
  status: PaymentStatus;
  amount: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  history?: PaymentHistoryEntry[];
}

export interface PaymentHistoryEntry {
  createdAtUtc: string;
  previousStatus: PaymentStatus | null;
  newStatus: PaymentStatus;
  source: string;
  notes?: string;
}
