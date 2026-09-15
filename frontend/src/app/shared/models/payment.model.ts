import { PaymentMethod } from '../enums/payment-method.enum';
import { PaymentStatus } from '../enums/payment-status.enum';
import { PaymentGateway } from '../enums/payment-gateway.enum';

export interface PaymentResponse {
  paymentId: string;
  orderId: string;
  gateway: PaymentGateway;
  gatewayTransactionId: string;
  gatewayCheckoutUrl: string;
  method: PaymentMethod;
  status: PaymentStatus;
  amount: number;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  expiresAtUtc?: string | null;
  history?: PaymentHistoryEntry[];
}

export interface PaymentHistoryEntry {
  createdAtUtc: string;
  previousStatus: PaymentStatus | null;
  newStatus: PaymentStatus;
  source: string;
  notes?: string;
}
