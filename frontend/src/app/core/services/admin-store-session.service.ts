import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AdminStoresService } from './admin-stores.service';
import { AuthService } from './auth.service';
import { isAdmin } from '../utils/jwt.helper';
import {
  AdminStoreContext,
  AdminStoreContextMetadata,
  AdminStoreSession,
} from '../../shared/models/admin-store.model';

const STORAGE_KEY = 'urbeat_admin_store_context';
const DEFAULT_RETURN_URL = '/admin/lojas';

/**
 * Error codes that mean the delegated context can no longer be used. `ADMIN_STORE_ACTION_NOT_ALLOWED`
 * is deliberately absent: it describes an endpoint outside the delegation and must follow the
 * generic 403 handling without discarding a still valid session.
 */
export const ADMIN_STORE_CONTEXT_ERROR_CODES: ReadonlySet<string> = new Set([
  'ADMIN_STORE_CONTEXT_INVALID',
  'ADMIN_STORE_CONTEXT_EXPIRED',
  'ADMIN_STORE_CONTEXT_STORE_MISMATCH',
  'ADMIN_STORE_CONTEXT_STORE_MISSING',
  'ADMIN_STORE_CONTEXT_ADMIN_NOT_ALLOWED',
]);

/**
 * Tab-scoped delegated administrator session. The token lives only in `sessionStorage` so two tabs
 * can inspect different stores, and it never survives the tab. The server remains authoritative for
 * every validation; this service only keeps the UX coherent.
 */
@Injectable({ providedIn: 'root' })
export class AdminStoreSessionService {
  private readonly stores = inject(AdminStoresService);
  private readonly auth = inject(AuthService);

  readonly session = signal<AdminStoreSession | null>(readStoredSession());
  readonly isExpired = signal(false);
  readonly isActive = computed(() => this.session() !== null && !this.isExpired());

  /**
   * True only when the tab holds both an administrator JWT and a store session. A seller login in
   * the same tab leaves the previous `sessionStorage` entry behind; without this guard the header
   * would be sent with seller requests (rejected by the middleware as not allowed).
   */
  readonly isAdminMode = computed(() => {
    const token = this.auth.token();
    return this.session() !== null && !!token && isAdmin(token);
  });

  /** Token forwarded only by the delegable-request interceptor. */
  readonly contextToken = computed(() =>
    this.isAdminMode() && !this.isExpired() ? this.session()?.token ?? null : null,
  );

  private restorePromise: Promise<boolean> | null = null;
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;
  private lastNotifiedCode: string | null = null;

  constructor() {
    const current = this.session();
    if (current) {
      this.scheduleExpiry(current);
    }

    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible' && this.isAdminMode()) {
          void this.revalidate();
        }
      });
    }
  }

  /** Restores and validates the stored context once; concurrent callers share the same promise. */
  ensureRestored(): Promise<boolean> {
    if (this.restorePromise) return this.restorePromise;

    const current = this.session();
    if (!current) return Promise.resolve(false);

    this.restorePromise = firstValueFrom(this.stores.getContext(current.storeId, current.token))
      .then((metadata) => {
        this.applyMetadata(metadata);
        return this.isActive();
      })
      .catch((error: unknown) => {
        // A context-specific 403 must not erase the expiration banner: `markInvalid` keeps the
        // in-memory snapshot when the TTL is over. Any other failure is fail-closed.
        const code = (error as { error?: { code?: unknown } } | null)?.error?.code;
        if (typeof code === 'string' && ADMIN_STORE_CONTEXT_ERROR_CODES.has(code)) {
          this.markInvalid(code);
        } else {
          this.clear();
        }

        return false;
      })
      .finally(() => {
        this.restorePromise = null;
      });

    return this.restorePromise;
  }

  /** Forces a server-side revalidation (visibility change), ignoring the cached promise. */
  revalidate(): Promise<boolean> {
    if (!this.isAdminMode()) return Promise.resolve(false);

    this.restorePromise = null;
    return this.ensureRestored();
  }

  start(response: AdminStoreContext, returnUrl: string): void {
    const session: AdminStoreSession = {
      token: response.token,
      contextId: response.contextId,
      storeId: response.storeId,
      storeName: response.storeName,
      storeSlug: response.storeSlug,
      adminUserId: response.adminUserId,
      expiresAtUtc: response.expiresAtUtc,
      returnUrl: normalizeReturnUrl(returnUrl),
    };

    this.session.set(session);
    this.isExpired.set(false);
    this.lastNotifiedCode = null;
    persist(session);
    this.scheduleExpiry(session);
  }

  /**
   * Closes the context and always clears local state, even when the network call fails. The
   * explicit DELETE is best effort: the server TTL still applies.
   */
  end(): void {
    const current = this.session();
    this.clear();

    if (current?.token) {
      this.stores.closeContext(current.storeId, current.token).subscribe({ error: () => undefined });
    }
  }

  /**
   * Marks the session unusable for a context-specific error code. An expired context keeps the
   * in-memory snapshot so the banner can announce the store and the expiration state.
   */
  markInvalid(code: string): void {
    if (!ADMIN_STORE_CONTEXT_ERROR_CODES.has(code) || !this.session()) return;

    // Once the TTL is known to be over, later "invalid" responses (requests without the header)
    // must not erase the expiration banner the administrator is looking at.
    if (this.isExpired() && code !== 'ADMIN_STORE_CONTEXT_EXPIRED') return;

    this.clearExpiryTimer();

    if (code === 'ADMIN_STORE_CONTEXT_EXPIRED') {
      this.isExpired.set(true);
      removeStored();
      return;
    }

    this.clear();
  }

  /** True the first time a given code is notified, so the interceptor shows one toast. */
  shouldNotify(code: string): boolean {
    if (this.lastNotifiedCode === code) return false;
    this.lastNotifiedCode = code;
    return true;
  }

  returnUrl(): string {
    return normalizeReturnUrl(this.session()?.returnUrl ?? DEFAULT_RETURN_URL);
  }

  private applyMetadata(metadata: AdminStoreContextMetadata): void {
    const current = this.session();
    if (!current) return;

    const updated: AdminStoreSession = {
      ...current,
      contextId: metadata.contextId,
      storeId: metadata.storeId,
      storeName: metadata.storeName,
      storeSlug: metadata.storeSlug,
      adminUserId: metadata.adminUserId,
      expiresAtUtc: metadata.expiresAtUtc,
    };

    this.session.set(updated);
    persist(updated);
    this.scheduleExpiry(updated);
  }

  private clear(): void {
    this.clearExpiryTimer();
    this.session.set(null);
    this.isExpired.set(false);
    this.lastNotifiedCode = null;
    removeStored();
  }

  private scheduleExpiry(session: AdminStoreSession): void {
    this.clearExpiryTimer();

    const expiresAt = new Date(session.expiresAtUtc).getTime();
    if (Number.isNaN(expiresAt)) return;

    const delay = expiresAt - Date.now();
    if (delay <= 0) {
      this.isExpired.set(true);
      removeStored();
      return;
    }

    this.expiryTimer = setTimeout(() => {
      this.isExpired.set(true);
      removeStored();
      this.expiryTimer = null;
    }, delay);
  }

  private clearExpiryTimer(): void {
    if (this.expiryTimer !== null) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = null;
    }
  }
}

function normalizeReturnUrl(value: string): string {
  return value?.startsWith(DEFAULT_RETURN_URL) ? value : DEFAULT_RETURN_URL;
}

function storage(): Storage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage;
  } catch {
    return null;
  }
}

function persist(session: AdminStoreSession): void {
  try {
    storage()?.setItem(STORAGE_KEY, JSON.stringify(session));
  } catch {
    /* storage may be unavailable (private mode/quota); the in-memory session still works */
  }
}

function removeStored(): void {
  try {
    storage()?.removeItem(STORAGE_KEY);
  } catch {
    /* ignore */
  }
}

function readStoredSession(): AdminStoreSession | null {
  try {
    const raw = storage()?.getItem(STORAGE_KEY);
    if (!raw) return null;

    const parsed = JSON.parse(raw) as Partial<AdminStoreSession> | null;
    if (!parsed?.token || !parsed.storeId || !parsed.expiresAtUtc) return null;

    return {
      token: parsed.token,
      contextId: parsed.contextId ?? '',
      storeId: parsed.storeId,
      storeName: parsed.storeName ?? '',
      storeSlug: parsed.storeSlug ?? '',
      adminUserId: parsed.adminUserId ?? '',
      expiresAtUtc: parsed.expiresAtUtc,
      returnUrl: normalizeReturnUrl(parsed.returnUrl ?? DEFAULT_RETURN_URL),
    };
  } catch {
    return null;
  }
}
