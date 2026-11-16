/**
 * Contracts of the administrative store directory and the delegated dashboard context.
 * The token is delivered only by the creation endpoint and is never part of the listing or of the
 * context metadata response.
 */

export type AdminStoreStatus = 'Onboarding' | 'Active' | 'Suspended';

export type AdminStoreSubscriptionStatus = 'Active' | 'Overdue' | 'Blocked' | 'None';

export type AdminStoresSort =
  | 'name'
  | 'name_desc'
  | 'created'
  | 'created_desc'
  | 'updated'
  | 'updated_desc';

export type AdminStoresStatusFilter = 'all' | 'onboarding' | 'active' | 'suspended';

export type AdminStoresPublicationFilter = 'all' | 'published' | 'draft';

export type AdminStoresOpenStateFilter = 'all' | 'open' | 'closed';

export type AdminStoresSubscriptionFilter = 'all' | 'active' | 'overdue' | 'blocked' | 'none';

export interface AdminStoreOwner {
  userId: string;
  email: string | null;
  name: string | null;
}

export interface AdminStoreSubscription {
  status: AdminStoreSubscriptionStatus;
  planName: string | null;
  nextDueDateUtc: string | null;
}

export interface AdminStoreSummary {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  owner: AdminStoreOwner;
  status: AdminStoreStatus;
  isPublished: boolean;
  isOpen: boolean;
  isSubscriptionBlocked: boolean;
  subscription: AdminStoreSubscription;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface PagedAdminStoreSummary {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  items: AdminStoreSummary[];
}

export interface AdminStoresQuery {
  search?: string;
  status?: AdminStoresStatusFilter;
  publication?: AdminStoresPublicationFilter;
  openState?: AdminStoresOpenStateFilter;
  subscription?: AdminStoresSubscriptionFilter;
  sort?: AdminStoresSort;
  page?: number;
  pageSize?: number;
}

/** One-time creation response. The token must be stored in sessionStorage by the caller. */
export interface AdminStoreContext {
  contextId: string;
  adminUserId: string;
  storeId: string;
  storeName: string;
  storeSlug: string;
  expiresAtUtc: string;
  token: string;
}

/** Context metadata. Never contains the token. */
export interface AdminStoreContextMetadata {
  contextId: string;
  adminUserId: string;
  storeId: string;
  storeName: string;
  storeSlug: string;
  expiresAtUtc: string;
}

/** Tab-scoped persisted session (sessionStorage key `urbeat_admin_store_context`). */
export interface AdminStoreSession {
  token: string;
  contextId: string;
  storeId: string;
  storeName: string;
  storeSlug: string;
  adminUserId: string;
  expiresAtUtc: string;
  returnUrl: string;
}
