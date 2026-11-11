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
  alreadyConfirmed: boolean;
  message: string;
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
