import { OrderStatus } from '../enums/order-status.enum';

export enum DeliveryRouteStatus {
  Active = 1,
  Completed = 2,
  Revoked = 3,
  Expired = 4,
}

export interface DeliveryRouteOrder {
  orderId: string;
  sequence: number;
  code: string;
  customerName?: string | null;
  addressSummary?: string | null;
  status: OrderStatus;
  deliveryConfirmedAtUtc?: string | null;
  isDelivered: boolean;
  canDeliver: boolean;
}

export interface DeliveryRoute {
  id: string;
  deliveryDriverId: string;
  driverName: string;
  driverWhatsAppPhoneNumber?: string | null;
  status: DeliveryRouteStatus;
  createdAtUtc: string;
  expiresAtUtc: string;
  revokedAtUtc?: string | null;
  completedAtUtc?: string | null;
  totalStops: number;
  deliveredStops: number;
  orders: DeliveryRouteOrder[];
}

export interface DeliveryRouteOperationResult {
  notFound: boolean;
  forbidden: boolean;
  invalidState: boolean;
  driverNotAvailable: boolean;
  orderNotEligible: boolean;
  conflict: boolean;
  error?: string | null;
  shareUrl?: string | null;
  route?: DeliveryRoute | null;
}

export interface ShareIndividualDeliveryResult {
  notFound: boolean;
  forbidden: boolean;
  invalidState: boolean;
  error?: string | null;
  shareUrl?: string | null;
}

export interface CreateDeliveryRouteRequest {
  deliveryDriverId: string;
  orderIds: string[];
}

export interface UpdateDeliveryRouteRequest {
  deliveryDriverId: string;
  orderIds: string[];
}

export interface SendDeliveryRoutesRequest {
  orderIds: string[];
  /** Operational declaration that the seller physically checked the selected volumes. */
  physicalConfirmation: boolean;
}

export interface SendDeliveryRouteGroup {
  storeDeliveryDriverId: string;
  deliveryDriverId: string;
  driverName: string;
  whatsAppPhoneNumber?: string | null;
  whatsAppUrl?: string | null;
  shareUrl: string;
  message: string;
  orderCount: number;
  totalStops: number;
  route: DeliveryRoute;
}

export interface SendDeliveryRoutesResult {
  forbidden: boolean;
  orderNotEligible: boolean;
  driverNotAvailable: boolean;
  conflict: boolean;
  error?: string | null;
  groups: SendDeliveryRouteGroup[];
}

export interface PublicDeliveryRouteStop {
  sequence: number;
  orderCode: string;
  status: OrderStatus;
  isNext: boolean;
  canDeliver: boolean;
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
  destinationLatitude?: number | null;
  destinationLongitude?: number | null;
  notes?: string | null;
  deliveryConfirmedAtUtc?: string | null;
}

export interface PublicDeliveryRoute {
  storeName: string;
  driverName: string;
  status: DeliveryRouteStatus;
  expiresAtUtc: string;
  completedAtUtc?: string | null;
  originLatitude?: number | null;
  originLongitude?: number | null;
  nextSequence?: number | null;
  totalStops: number;
  deliveredStops: number;
  stops: PublicDeliveryRouteStop[];
}

export interface PublicDeliveryRoutePinResult {
  succeeded: boolean;
  notFound: boolean;
  gone: boolean;
  invalidState: boolean;
  pinInvalid: boolean;
  pinLocked: boolean;
  pinUnavailable: boolean;
  concurrentUpdate: boolean;
  remainingAttempts: number;
  orderStatus?: OrderStatus | null;
  deliveryConfirmedAtUtc?: string | null;
  route?: PublicDeliveryRoute | null;
}
