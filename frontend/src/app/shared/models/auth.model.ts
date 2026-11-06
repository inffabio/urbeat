export type AccountRole = 'Customer' | 'Seller';

export interface RegisterCustomerRequest {
  fullName: string;
  email: string;
  /** New password for the role being registered. */
  password: string;
  phoneNumber: string;
  document?: string;
  /**
   * Proof of ownership when this e-mail already belongs to an urbeat account. It is the
   * existing role password (never the new role password) and is only sent on promotion.
   */
  currentPassword?: string;
}

export interface RegisterResponse {
  succeeded: boolean;
  userId: string;
  emailConfirmationPending: boolean;
  emailChangeChallenge?: string;
  roleAddedToExistingAccount?: boolean;
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
