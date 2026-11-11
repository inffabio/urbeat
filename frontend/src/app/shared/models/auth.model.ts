export type AccountRole = 'Customer' | 'Seller';

export interface RegisterCustomerRequest {
  fullName: string;
  email: string;
  /** New password for the role being registered. It is independent from the other role. */
  password: string;
  phoneNumber: string;
  document?: string;
}

export interface RegisterResponse {
  succeeded: boolean;
  /** Omitted/null for an anonymous promotion request so the existing identity is not disclosed. */
  userId?: string | null;
  emailConfirmationPending: boolean;
  emailChangeChallenge?: string;
  roleAddedToExistingAccount?: boolean;
  /**
   * The e-mail already belongs to an urbeat identity and the caller is not signed in as its
   * owner. No role/credential was created; a single-use confirmation link was sent instead.
   */
  rolePromotionPending?: boolean;
}

export interface ConfirmRolePromotionRequest {
  token: string;
}

export interface ConfirmRolePromotionResponse {
  succeeded: boolean;
  alreadyApplied: boolean;
  role?: AccountRole | null;
  emailConfirmationPending: boolean;
  message: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthTokenResponse {
  accessToken: string;
  expiresAtUtc: string;
}

export interface CustomerProfileResponse {
  fullName: string;
  email: string;
  phoneNumber?: string | null;
  primaryAddressId?: string | null;
}

export interface UpdateCustomerProfileRequest {
  fullName: string;
  email: string;
  phoneNumber: string;
}

export interface CustomerCheckoutInfo {
  fullName: string;
  email: string;
  phoneNumber: string;
}

export interface ConfirmEmailRequest {
  userId: string;
  token: string;
}

export interface ConfirmEmailResponse {
  succeeded: boolean;
  /** Pre-existing contract field (used by the confirm page); not a new enumeration signal. */
  alreadyConfirmed: boolean;
  message: string;
  /**
   * Optional server-calculated cooldown deadline for a resend request. It is only a UX hint:
   * delivery is asynchronous and the server rate limiter remains authoritative. Older API
   * responses omit it, in which case the page falls back to a local 120-second cooldown.
   */
  resendAvailableAtUtc?: string | null;
}

/**
 * Public acknowledgement of a resend request. It deliberately carries no confirmation state:
 * unknown, pending and already-confirmed addresses must be indistinguishable to the client.
 */
export interface ResendConfirmationResponse {
  succeeded: boolean;
  message: string;
  /** Same server-calculated cooldown hint as the confirmation response. */
  resendAvailableAtUtc?: string | null;
}

export interface ResendConfirmationRequest {
  email: string;
}

export interface ForgotPasswordRequest {
  email: string;
  role: AccountRole;
}

export interface ForgotPasswordResponse {
  message: string;
}

export interface ValidateResetTokenResponse {
  valid: boolean;
  message?: string;
  role?: AccountRole;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
  confirmPassword: string;
  role: AccountRole;
}

export interface ChangePasswordRequest {
  role: AccountRole;
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface ResetPasswordResponse {
  message: string;
}

export interface UpdateEmailRequest {
  userId: string;
  currentEmail: string;
  newEmail: string;
  emailChangeChallenge: string;
}

export interface SellerProfileResponse {
  fullName: string;
  document: string | null;
  phoneNumber: string | null;
  email: string;
}

export interface UpdateSellerProfileRequest {
  fullName: string;
}
