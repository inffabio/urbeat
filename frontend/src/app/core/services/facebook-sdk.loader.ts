import type { FacebookSdk } from './facebook-sdk.types';

/** Stable id used by the official Facebook loader snippet. */
export const FACEBOOK_SDK_SCRIPT_ID = 'facebook-jssdk';
/** Browser SDK entry point. The App ID is passed to `FB.init`, never to the script URL. */
export const FACEBOOK_SDK_SCRIPT_SRC = 'https://connect.facebook.net/en_US/sdk.js';
/** Graph API version used when the environment leaves `metaApiVersion` empty. */
export const DEFAULT_FACEBOOK_API_VERSION = 'v26.0';
/** Guards the boot flow: a slow/blocked SDK resolves `null` instead of hanging forever. */
export const FACEBOOK_SDK_TIMEOUT_MS = 10_000;

export interface FacebookSdkLoadOptions {
  readonly appId: string;
  readonly apiVersion?: string;
  readonly timeoutMs?: number;
  /** Injectable for tests and non-browser renderers; defaults to the global document. */
  readonly document?: Document | null;
}

/**
 * Loads the Facebook JS SDK once per loader instance.
 *
 * Behaviour:
 * - empty `appId` is the kill switch: resolves `null` without touching the DOM;
 * - without a usable `document` (SSR/tests) it resolves `null` immediately;
 * - concurrent calls share the same promise and the script tag is inserted once;
 * - `FB.init` runs inside `window.fbAsyncInit`, so it only sees a ready SDK;
 * - `FB.AppEvents.logPageView()` is guarded because the API is optional;
 * - timeout, script error and `FB.init` failure resolve `null` (never reject), so a
 *   third-party outage cannot block application boot;
 * - a failed attempt is not cached, allowing a later retry.
 *
 * The loader is intentionally inert until called: call it only after the user has
 * consented to Meta tracking (the project has no consent banner yet, so no component
 * invokes it automatically).
 */
export class FacebookSdkLoader {
  private pending: Promise<FacebookSdk | null> | null = null;

  constructor(
    private readonly documentRef: Document | null = typeof document === 'undefined'
      ? null
      : document,
  ) {}

  load(options: FacebookSdkLoadOptions): Promise<FacebookSdk | null> {
    const appId = options.appId?.trim() ?? '';
    if (!appId) return Promise.resolve(null);

    if (this.pending) return this.pending;

    const doc = options.document === undefined ? this.documentRef : options.document;
    const win = doc?.defaultView ?? null;
    if (!doc || !win) return Promise.resolve(null);

    const version = options.apiVersion?.trim() || DEFAULT_FACEBOOK_API_VERSION;
    const timeoutMs = normalizeTimeout(options.timeoutMs);

    const promise = new Promise<FacebookSdk | null>((resolve) => {
      let settled = false;
      let timeoutHandle: ReturnType<typeof setTimeout> | null = null;

      const finish = (sdk: FacebookSdk | null): void => {
        if (settled) return;
        settled = true;
        if (timeoutHandle !== null) clearTimeout(timeoutHandle);
        resolve(sdk);
      };

      const initialize = (): void => {
        try {
          const sdk = win.FB;
          if (!sdk || typeof sdk.init !== 'function') {
            finish(null);
            return;
          }

          sdk.init({ appId, cookie: true, xfbml: true, version });
          sdk.AppEvents?.logPageView?.();
          finish(sdk);
        } catch {
          finish(null);
        }
      };

      timeoutHandle = setTimeout(() => finish(null), timeoutMs);

      // Another integration already loaded the SDK: initialize it without a new script.
      if (win.FB && typeof win.FB.init === 'function') {
        initialize();
        return;
      }

      win.fbAsyncInit = initialize;

      // The script may already be in flight (same page, same id). Reusing it keeps the
      // `fbAsyncInit` contract and avoids duplicate downloads/initialization.
      if (doc.getElementById(FACEBOOK_SDK_SCRIPT_ID)) return;

      const script = doc.createElement('script');
      script.id = FACEBOOK_SDK_SCRIPT_ID;
      script.src = FACEBOOK_SDK_SCRIPT_SRC;
      script.async = true;
      script.defer = true;
      script.onerror = () => finish(null);
      (doc.head ?? doc.body ?? doc.documentElement).appendChild(script);
    });

    this.pending = promise;
    void promise.then((sdk) => {
      // Do not cache failures: a blocked network or a transient timeout must not
      // disable the SDK for the rest of the session.
      if (!sdk && this.pending === promise) this.pending = null;
    });

    return promise;
  }
}

function normalizeTimeout(value: number | undefined): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) {
    return FACEBOOK_SDK_TIMEOUT_MS;
  }
  return value;
}
