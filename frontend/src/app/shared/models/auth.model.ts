export interface RegisterCustomerRequest {
  fullName: string;
  email: string;
  password: string;
  phoneNumber: string;
}

export interface RegisterResponse {
  succeeded: boolean;
  userId: string;
  emailConfirmationPending: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthTokenResponse {
  accessToken: string;
  expiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

export interface CustomerProfileResponse {
  fullName: string;
  email: string;
  phoneNumber?: string | null;
  primaryAddressId?: string | null;
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
}

export interface ForgotPasswordResponse {
  found: boolean;
  message: string;
}

export interface ValidateResetTokenResponse {
  valid: boolean;
  message?: string;
}

export interface ResetPasswordRequest {
  token: string;
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
}
