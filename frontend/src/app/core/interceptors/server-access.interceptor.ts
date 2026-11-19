import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { ServerAccessService } from '../services/server-access.service';
import { SKIP_SERVER_ACCESS_OVERLAY } from './server-access.context';

// The local print agent runs on the user's machine and polls frequently, so it
// must never dim the UI or block interaction. Only this exact origin is
// excluded; every other request (including relative backend URLs) is tracked.
// Keep this in sync with error.interceptor.ts.
const LOCAL_AGENT_ORIGIN = new URL('http://127.0.0.1:43111').origin;

function isLocalPrintAgentRequest(url: string): boolean {
  try {
    return new URL(url).origin === LOCAL_AGENT_ORIGIN;
  } catch {
    return false;
  }
}

export const serverAccessInterceptor: HttpInterceptorFn = (req, next) => {
  // Background polls set this token explicitly so a recurring request never
  // flashes the global overlay on top of the screen the user is reading.
  if (isLocalPrintAgentRequest(req.url) || req.context.get(SKIP_SERVER_ACCESS_OVERLAY)) {
    return next(req);
  }

  const serverAccess = inject(ServerAccessService);
  serverAccess.begin();

  return next(req).pipe(finalize(() => serverAccess.end()));
};
