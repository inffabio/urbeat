import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { ServerAccessService } from '../services/server-access.service';

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
  if (isLocalPrintAgentRequest(req.url)) {
    return next(req);
  }

  const serverAccess = inject(ServerAccessService);
  serverAccess.begin();

  return next(req).pipe(finalize(() => serverAccess.end()));
};
