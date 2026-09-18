import { HttpContextToken } from '@angular/common/http';

// Opt-in flag for background requests (recurring order/payment polling) that
// must not dim or block the UI through the global server-access overlay.
// Callers set it explicitly on the request context; there is no URL matching.
export const SKIP_SERVER_ACCESS_OVERLAY = new HttpContextToken<boolean>(() => false);
