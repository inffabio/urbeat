import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { FacebookSdkLoader } from './facebook-sdk.loader';
import type {
  FacebookAuthResponse,
  FacebookGetLoginStatusParams,
  FacebookLoginStatus,
  FacebookLoginStatusResponse,
  FacebookSdk,
} from './facebook-sdk.types';

/**
 * `FB.getLoginStatus` may never call back (blocked tracker, broken build), so the
 * service races it against this timeout and resolves `null` instead of hanging.
 */
export const FACEBOOK_LOGIN_STATUS_TIMEOUT_MS = 10_000;

export interface FacebookLoginStatusOptions {
  readonly scope?: string;
  readonly forceRoundTrip?: boolean;
  readonly timeoutMs?: number;
}

/**
 * Optional browser-side Meta integration.
 *
 * Reads the public App ID/API version from the Angular environment and delegates the
 * actual loading to `FacebookSdkLoader`. It is not started at bootstrap: there is no
 * consent mechanism in the project yet, so callers must invoke `load()` explicitly
 * after the user opts in. An empty `metaAppId` disables the SDK.
 *
 * This is *not* a WhatsApp channel. WhatsApp Business Cloud API is server-side and
 * must keep its token/app secret in the backend; nothing secret belongs in this file.
 *
 * `getLoginStatus()` is intentionally read-only: it never calls `FB.login`, never
 * persists the Meta access token and never authenticates into urbeat. Urbeat sessions
 * keep coming exclusively from `AuthService`.
 */
@Injectable({ providedIn: 'root' })
export class FacebookSdkService {
  private readonly loader = new FacebookSdkLoader(inject(DOCUMENT));

  /** `true` only when a public App ID is configured for the current environment. */
  get isConfigured(): boolean {
    return (environment.metaAppId ?? '').trim().length > 0;
  }

  /** Loads and initializes the SDK, or resolves `null` when disabled/unavailable. */
  load(): Promise<FacebookSdk | null> {
    return this.loader.load({
      appId: environment.metaAppId ?? '',
      apiVersion: environment.metaApiVersion ?? '',
    });
  }

  /**
   * Reads the current Meta login status after ensuring the SDK is loaded.
   *
   * - resolves `null` when the SDK is disabled, unavailable or does not expose
   *   `FB.getLoginStatus`;
   * - invokes `FB.getLoginStatus` exactly once per call, with no retry loop;
   * - resolves `null` on callback exception, synchronous throw, invalid payload or
   *   timeout — it never rejects, so it cannot break the calling flow;
   * - returns the payload as-is (including `authResponse`) without storing the token
   *   anywhere. Any real login must validate `signedRequest`/`accessToken` on the
   *   backend before creating an urbeat session.
   */
  async getLoginStatus(
    options: FacebookLoginStatusOptions = {},
  ): Promise<FacebookLoginStatusResponse | null> {
    let sdk: FacebookSdk | null;
    try {
      sdk = await this.load();
    } catch {
      return null;
    }

    const getLoginStatus = sdk?.getLoginStatus;
    if (!sdk || typeof getLoginStatus !== 'function') return null;

    const timeoutMs = normalizeTimeout(options.timeoutMs);
    const params = buildParams(options);

    return new Promise<FacebookLoginStatusResponse | null>((resolve) => {
      let settled = false;
      let timeoutHandle: ReturnType<typeof setTimeout> | null = null;

      const finish = (response: FacebookLoginStatusResponse | null): void => {
        if (settled) return;
        settled = true;
        if (timeoutHandle !== null) clearTimeout(timeoutHandle);
        resolve(response);
      };

      timeoutHandle = setTimeout(() => finish(null), timeoutMs);

      try {
        const callback: (response: FacebookLoginStatusResponse) => void = (response) => {
          try {
            finish(normalizeResponse(response));
          } catch {
            finish(null);
          }
        };

        if (params) getLoginStatus.call(sdk, callback, params);
        else getLoginStatus.call(sdk, callback);
      } catch {
        finish(null);
      }
    });
  }
}

function buildParams(options: FacebookLoginStatusOptions): FacebookGetLoginStatusParams | null {
  const params: FacebookGetLoginStatusParams = {
    ...(options.scope ? { scope: options.scope } : {}),
    ...(options.forceRoundTrip === undefined ? {} : { forceRoundTrip: options.forceRoundTrip }),
  };
  return Object.keys(params).length > 0 ? params : null;
}

function normalizeTimeout(value: number | undefined): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) {
    return FACEBOOK_LOGIN_STATUS_TIMEOUT_MS;
  }
  return value;
}

/**
 * Defensive normalisation of a third-party payload: an unexpected `status` (or a
 * missing callback argument) makes the whole response untrustworthy, so it becomes
 * `null` instead of being forwarded as a partially valid state.
 */
function normalizeResponse(
  value: FacebookLoginStatusResponse | null | undefined,
): FacebookLoginStatusResponse | null {
  if (!value || typeof value !== 'object') return null;
  if (!isLoginStatus(value.status)) return null;

  const authResponse = normalizeAuthResponse(value.authResponse);

  // `connected` without a usable token is a contradiction: report it as `null`
  // rather than handing callers a state they cannot act on.
  if (value.status === 'connected') {
    return authResponse ? { status: 'connected', authResponse } : null;
  }

  return authResponse ? { status: value.status, authResponse } : { status: value.status };
}

function isLoginStatus(value: unknown): value is FacebookLoginStatus {
  return value === 'connected' || value === 'not_authorized' || value === 'unknown';
}

function normalizeAuthResponse(
  value: FacebookAuthResponse | null | undefined,
): FacebookAuthResponse | null {
  if (!value || typeof value !== 'object') return null;

  const {
    accessToken,
    userID,
    expiresIn,
    signedRequest,
    grantedScopes,
    graphDomain,
    data_access_expiration_time,
  } = value;
  if (typeof accessToken !== 'string' || accessToken.length === 0) return null;
  if (typeof userID !== 'string' || userID.length === 0) return null;

  return {
    accessToken,
    userID,
    // The SDK reports seconds; a missing value becomes 0 (unknown lifetime).
    expiresIn: typeof expiresIn === 'number' && Number.isFinite(expiresIn) ? expiresIn : 0,
    ...(typeof signedRequest === 'string' ? { signedRequest } : {}),
    ...(typeof grantedScopes === 'string' ? { grantedScopes } : {}),
    ...(typeof graphDomain === 'string' ? { graphDomain } : {}),
    ...(typeof data_access_expiration_time === 'number' && Number.isFinite(data_access_expiration_time)
      ? { data_access_expiration_time }
      : {}),
  };
}
