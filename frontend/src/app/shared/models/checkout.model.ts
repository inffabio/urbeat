import { FulfillmentType } from '../enums/fulfillment-type.enum';
import { PaymentMethod } from '../enums/payment-method.enum';

export interface CheckoutItemRequest {
  productId: string;
  quantity: number;
  notes?: string;
  variationId?: string;
  choiceOptionId?: string;
  weightGrams?: number;
  additionalIds?: string[];
  optionGroups?: CheckoutOptionGroupSelection[];
}

export interface CheckoutOptionGroupSelection {
  groupId: string;
  itemIds: string[];
}

export interface CheckoutRequest {
  storeId: string;
  fulfillmentType: FulfillmentType;
  customerAddressId?: string;
  paymentMethod?: PaymentMethod;
  notes?: string;
  items: CheckoutItemRequest[];
}

export interface CheckoutPreviewResponse {
  storeId: string;
  fulfillmentType: FulfillmentType;
  customerAddressId?: string;
  paymentMethod?: PaymentMethod;
  subtotal: number;
  deliveryFee: number;
  minimumOrderValue: number;
  freeShippingThreshold?: number;
  freeShippingApplied: boolean;
  total: number;
  storeIsOpen: boolean;
}

export interface CheckoutConfirmResponse {
  orderId: string;
  code: string;
  fulfillmentType: FulfillmentType;
  status: number;
  subtotal: number;
  deliveryFee: number;
  total: number;
}

export interface StartCustomerVerificationRequest {
  storeId: string;
  customer: {
    fullName: string;
    email: string;
    phoneNumber: string;
  };
  address: {
    cep: string;
    street: string;
    number: string;
    complement?: string;
    neighborhood: string;
    city: string;
    state: string;
  };
}

export interface StartCustomerVerificationResponse {
  verificationId: string;
  expiresAtUtc: string;
  resendAvailableAtUtc: string;
  maskedPhone: string;
}

export interface ConfirmCustomerVerificationRequest {
  verificationId: string;
  code: string;
}

export interface ConfirmCustomerVerificationResponse {
  succeeded: boolean;
  errorCode?: string;
  error?: string;
  accessToken?: string;
  expiresAtUtc?: string;
  refreshToken?: string;
  refreshTokenExpiresAtUtc?: string;
  customerAddressId?: string;
}

export interface ResendCustomerVerificationRequest {
  verificationId: string;
}

export interface ResendCustomerVerificationResponse {
  succeeded: boolean;
  errorCode?: string;
  error?: string;
  expiresAtUtc?: string;
  resendAvailableAtUtc?: string;
}
