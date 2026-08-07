import { Router } from '@angular/router';

export function getStorePathFromUrl(router: Router): string {
  const m = router.url.match(/^\/([^/]+)\//);
  return m?.[1] ?? '';
}
