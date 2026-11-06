/**
 * Minimal, hand-written surface of the Facebook JS SDK used by urbeat.
 *
 * The SDK is a third-party global loaded at runtime, so only the members we
 * actually call are declared here. Everything else stays untyped instead of
 * widening the project to `any`.
 */
export interface FacebookSdkInitOptions {
  readonly appId: string;
  readonly cookie: boolean;
  readonly xfbml: boolean;
  readonly version: string;
}

export interface FacebookAppEvents {
  /** Present in current SDK builds, but always optional at runtime. */
  readonly logPageView?: () => void;
}

/**
 * `FB.getLoginStatus` reports the connection between the browser session and the
 * Meta app. It is *not* an urbeat session: a `connected` status only means the
 * Meta SDK holds a token, which must be validated server-side before it can be
 * trusted for anything.
 */
export type FacebookLoginStatus = 'connected' | 'not_authorized' | 'unknown';

/**
 * Present when `status === 'connected'`, but kept optional because the SDK omits
 * it for `not_authorized`/`unknown` and may omit it in degraded/blocked flows.
 */
export interface FacebookAuthResponse {
  readonly accessToken: string;
  readonly userID: string;
  /** Lifetime of `accessToken` in seconds, as reported by the SDK. */
  readonly expiresIn: number;
  /** Signed payload used by the server to validate the session without trusting the client. */
  readonly signedRequest?: string;
  /** Comma-separated granted scopes in some SDK builds. */
  readonly grantedScopes?: string;
  /** `'facebook'` (default) or `'instagram'` in current builds. */
  readonly graphDomain?: string;
  /** Unix timestamp (seconds) after which `data_access` expires. */
  readonly data_access_expiration_time?: number;
}

export interface FacebookLoginStatusResponse {
  readonly status: FacebookLoginStatus;
  readonly authResponse?: FacebookAuthResponse | null;
}

export type FacebookLoginStatusCallback = (response: FacebookLoginStatusResponse) => void;

/** Optional second argument introduced by recent SDK builds. */
export interface FacebookGetLoginStatusParams {
  /** Scopes the app will request; the SDK re-prompts when they are missing. */
  readonly scope?: string;
  /** Bypasses the SDK cache and round-trips to Facebook (slower, always network). */
  readonly forceRoundTrip?: boolean;
}

export type FacebookGetLoginStatus = (
  callback: FacebookLoginStatusCallback,
  params?: FacebookGetLoginStatusParams,
) => void;

export interface FacebookSdk {
  init(options: FacebookSdkInitOptions): void;
  readonly AppEvents?: FacebookAppEvents;
  /**
   * Optional on purpose: partial/older SDK builds and test doubles may not expose
   * it, so callers must guard before invoking.
   */
  readonly getLoginStatus?: FacebookGetLoginStatus;
}

declare global {
  interface Window {
    /** Populated by the Facebook JS SDK after its script is evaluated. */
    FB?: FacebookSdk;
    /** Invoked by the Facebook JS SDK once it is ready to be initialized. */
    fbAsyncInit?: () => void;
  }
}
