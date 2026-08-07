import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  CheckoutRequest,
  CheckoutPreviewResponse,
  CheckoutConfirmResponse,
  StartCustomerVerificationRequest,
  StartCustomerVerificationResponse,
  ConfirmCustomerVerificationRequest,
  ConfirmCustomerVerificationResponse,
  ResendCustomerVerificationRequest,
  ResendCustomerVerificationResponse,
} from '../../shared/models/checkout.model';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { CustomerCheckoutInfo } from '../../shared/models/auth.model';
import { UpsertCustomerAddress } from '../../shared/models/address.model';

@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly api = inject(ApiService);

  // Estado persistido em memória durante o fluxo do checkout
  readonly fulfillmentType = signal<FulfillmentType>(FulfillmentType.Delivery);
  readonly paymentMethod = signal<PaymentMethod | null>(null);
  readonly customerInfo = signal<CustomerCheckoutInfo | null>(null);
  readonly customerAddress = signal<UpsertCustomerAddress | null>(null);
  readonly customerAddressId = signal<string | null>(null);
  readonly verificationId = signal<string | null>(null);
  readonly verificationExpiresAtUtc = signal<string | null>(null);
  readonly verificationResendAvailableAtUtc = signal<string | null>(null);
  readonly verificationMaskedPhone = signal<string | null>(null);
  readonly orderNotes = signal<string>('');
  readonly lastOrderId = signal<string | null>(null);
  readonly lastOrderCode = signal<string | null>(null);

  preview(req: CheckoutRequest): Observable<CheckoutPreviewResponse> {
    return this.api.post<CheckoutPreviewResponse>('/api/checkout/preview', req);
  }

  confirm(req: CheckoutRequest): Observable<CheckoutConfirmResponse> {
    return this.api.post<CheckoutConfirmResponse>('/api/checkout/confirm', req);
  }

  createCustomerSession(req: StartCustomerVerificationRequest): Observable<ConfirmCustomerVerificationResponse> {
    return this.api.post<ConfirmCustomerVerificationResponse>('/api/checkout/customer-session', req);
  }

  startCustomerVerification(req: StartCustomerVerificationRequest): Observable<StartCustomerVerificationResponse> {
    return this.api.post<StartCustomerVerificationResponse>('/api/checkout/customer-verification/start', req);
  }

  confirmCustomerVerification(req: ConfirmCustomerVerificationRequest): Observable<ConfirmCustomerVerificationResponse> {
    return this.api.post<ConfirmCustomerVerificationResponse>('/api/checkout/customer-verification/confirm', req);
  }

  resendCustomerVerification(req: ResendCustomerVerificationRequest): Observable<ResendCustomerVerificationResponse> {
    return this.api.post<ResendCustomerVerificationResponse>('/api/checkout/customer-verification/resend', req);
  }

  resetCheckout(): void {
    this.paymentMethod.set(null);
    this.orderNotes.set('');
    this.lastOrderId.set(null);
    this.lastOrderCode.set(null);
    this.verificationId.set(null);
    this.verificationExpiresAtUtc.set(null);
    this.verificationResendAvailableAtUtc.set(null);
    this.verificationMaskedPhone.set(null);
  }
}
