export enum StoreDeliveryDriverStatus {
  Pending = 1,
  Active = 2,
  Revoked = 3,
}

/**
 * Seller-side view of the operational motoboy link. The motoboy has no account, e-mail or
 * password; deliveries are operated through public bearer links.
 */
export interface SellerDeliveryDriver {
  /** Store-scoped link id used for assignment and route APIs. */
  id: string;
  /** Global driver id exposed by order assignment summaries. */
  deliveryDriverId: string;
  name: string;
  phoneNumber: string;
  whatsAppPhoneNumber?: string | null;
  isActive: boolean;
  status: StoreDeliveryDriverStatus;
  acceptedAtUtc?: string | null;
  revokedAtUtc?: string | null;
}

export interface UpsertStoreDeliveryDriverRequest {
  name: string;
  phoneNumber: string;
  whatsAppPhoneNumber?: string | null;
  isActive: boolean;
}

export interface AssignDeliveryDriverRequest {
  deliveryDriverId: string;
}
