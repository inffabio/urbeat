import { HttpContextToken } from '@angular/common/http';

// Opt-out flag for anonymous requests whose credential is the URL itself (public delivery
// route/delivery links). The global auth interceptor must never attach a seller/customer
// bearer token to these calls, even when another identity is signed in on the same tab.
export const SKIP_AUTH_HEADER = new HttpContextToken<boolean>(() => false);
