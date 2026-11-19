export type AdminAccountStatus = 'Active' | 'Blocked' | 'Invited';

export type AdminInvitationState =
  | 'Pending'
  | 'Sending'
  | 'Expired'
  | 'Accepted'
  | 'Revoked'
  | 'Superseded';

export type AdminInvitationPurpose = 'AccountInvitation' | 'PasswordReset';

export interface AdminInvitation {
  id: string;
  purpose: AdminInvitationPurpose;
  state: AdminInvitationState;
  expiresAtUtc: string;
  emailSentAtUtc: string | null;
  createdAtUtc: string;
}

export interface AdminAdministrator {
  id: string;
  email: string;
  status: AdminAccountStatus;
  isEmailConfirmed: boolean;
  isBlocked: boolean;
  isCurrentUser: boolean;
  isLastActiveAdmin: boolean;
  createdAtUtc: string;
  invitation: AdminInvitation | null;
}

export interface PagedAdminAdministrators {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  items: AdminAdministrator[];
}

export interface AdminAccountsQuery {
  status?: 'all' | 'active' | 'blocked' | 'invited';
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface InviteAdminAccountResponse {
  administratorId: string;
  email: string;
  status: AdminAccountStatus;
  invitation: AdminInvitation;
  /**
   * False when the invitation was committed but its delivery job never reached the queue.
   * Absent on older payloads, which are treated as queued.
   */
  emailQueued?: boolean;
  emailDeliveryPending?: boolean;
}

export interface AdminInvitationDeliveryResponse {
  invitationId: string;
  expiresAtUtc: string;
  emailDeliveryPending: boolean;
  /** False when the delivery job never reached the queue; absent on older payloads. */
  emailQueued?: boolean;
}

export interface AdminInvitationValidation {
  valid: boolean;
  purpose?: AdminInvitationPurpose | null;
  emailMasked?: string | null;
  expiresAtUtc?: string | null;
  message?: string | null;
}

export interface AdminAccountSetupRequest {
  token: string;
  newPassword: string;
  confirmPassword: string;
}

export interface AdminAccountSetupResult {
  succeeded: boolean;
  purpose: AdminInvitationPurpose;
  message: string;
}
