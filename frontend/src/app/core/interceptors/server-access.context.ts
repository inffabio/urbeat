import { HttpContextToken } from '@angular/common/http';

// Opt-in flag for background requests (recurring order/payment polling) that
// must not dim or block the UI through the global server-access overlay.
// Callers set it explicitly on the request context; there is no URL matching.
export const SKIP_SERVER_ACCESS_OVERLAY = new HttpContextToken<boolean>(() => false);

// Opt-in flag for automatic background polling. A transient 5xx/timeout in a
// poll is retried by the owning service (order tracking / Pix confirmation), so
// the global error interceptor must not surface it as a fatal generic toast.
// Authentication (401) handling is intentionally kept, and the error is still
// rethrown so callers can retry or mark the resource unavailable (404).
export const SKIP_GLOBAL_ERROR_TOAST = new HttpContextToken<boolean>(() => false);
