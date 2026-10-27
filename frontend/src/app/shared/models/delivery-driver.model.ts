import { OrderStatus } from '../enums/order-status.enum';
import { FulfillmentType } from '../enums/fulfillment-type.enum';
import { PaymentMethod } from '../enums/payment-method.enum';
import { OrderItem } from './order.model';

export enum StoreDeliveryDriverStatus {
  Pending = 1,
  Active = 2,
  Revoked = 3,
}

export interface SellerDeliveryDriver {
  /** Store-scoped link id used for assignment and route APIs. */
  id: string;
  /** Global driver id exposed by order assignment summaries. */
  deliveryDriverId: string;
  name: string;
  email: string;
  phoneNumber: string;
  whatsAppPhoneNumber?: string | null;
  isActive: boolean;
  hasAccount: boolean;
  status: StoreDeliveryDriverStatus;
  inviteSentAtUtc?: string | null;
  inviteExpiresAtUtc?: string | null;
  acceptedAtUtc?: string | null;
  revokedAtUtc?: string | null;
  canResendInvite: boolean;
}

export interface UpsertStoreDeliveryDriverRequest {
  name: string;
  phoneNumber: string;
  whatsAppPhoneNumber?: string | null;
  isActive: boolean;
}

export interface InviteDeliveryDriverRequest {
  fullName: string;
  email: string;
  phoneNumber: string;
}

export interface AssignDeliveryDriverRequest {
  deliveryDriverId: string;
}

export interface DeliveryDriverInvitePreview {
  valid: boolean;
  alreadyAccepted: boolean;
  storeName: string;
  driverName: string;
  maskedEmail: string;
  expiresAtUtc?: string | null;
}

export interface AcceptDeliveryDriverInviteRequest {
  password: string;
  fullName?: string;
}

export interface AcceptDeliveryDriverInviteResponse {
  succeeded: boolean;
  storeName?: string | null;
  error?: string | null;
}

export interface DeliveryDriverStore {
  storeId: string;
  storeName: string;
  acceptedAtUtc?: string | null;
  assignedOrderCount: number;
}

export interface DriverOrder {
  id: string;
  code: string;
  storeId: string;
  status: OrderStatus;
  fulfillmentType: FulfillmentType;
  paymentMethod: PaymentMethod;
  customerName?: string | null;
  customerPhoneNumber?: string | null;
  addressCep?: string | null;
  addressStreet?: string | null;
  addressNumber?: string | null;
  addressNeighborhood?: string | null;
  addressCity?: string | null;
  addressState?: string | null;
  addressComplement?: string | null;
  addressReference?: string | null;
  addressSummary?: string | null;
  /**
   * Persisted coordinates exposed by the driver endpoint. Null means the address has no
   * geocoded coordinates, so the portal must fall back to the address text + external map.
   */
  originLatitude?: number | null;
  originLongitude?: number | null;
  destinationLatitude?: number | null;
  destinationLongitude?: number | null;
  notes?: string | null;
  subtotal: number;
  deliveryFee: number;
  total: number;
  createdAtUtc: string;
  deliveryStartedAtUtc?: string | null;
  deliveryConfirmedAtUtc?: string | null;
  items: OrderItem[];
  canStartDelivery: boolean;
  canConfirmPin: boolean;
}

export interface DriverOrdersResponse {
  storeNotAvailable: boolean;
  orders: DriverOrder[];
}

export interface DeliveryDriverActionResult {
  notFound: boolean;
  forbidden: boolean;
  invalidState: boolean;
  pinInvalid: boolean;
  pinLocked: boolean;
  pinUnavailable: boolean;
  concurrentUpdate?: boolean;
  remainingAttempts: number;
  status?: OrderStatus;
  deliveryConfirmedAtUtc?: string | null;
}
