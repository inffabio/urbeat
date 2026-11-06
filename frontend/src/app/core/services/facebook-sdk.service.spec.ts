import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { FACEBOOK_SDK_SCRIPT_ID, FACEBOOK_SDK_TIMEOUT_MS } from './facebook-sdk.loader';
import {
  FACEBOOK_LOGIN_STATUS_TIMEOUT_MS,
  FacebookSdkService,
} from './facebook-sdk.service';
import type {
  FacebookAuthResponse,
  FacebookGetLoginStatusParams,
  FacebookLoginStatus,
  FacebookLoginStatusCallback,
} from './facebook-sdk.types';

const APP_ID = '1234567890';
const CONNECTED_AUTH: FacebookAuthResponse = {
  accessToken: 'meta-access-token-never-stored',
  userID: 'meta-user-id',
  expiresIn: 5_184_000,
  signedRequest: 'signed-request-payload',
};

interface SdkMock {
  readonly sdk: FacebookSdkWithGlobalProps;
  readonly init: jest.Mock;
  readonly logPageView: jest.Mock;
  readonly login: jest.Mock;
  readonly getLoginStatus: jest.Mock;
}

/** The SDK also exposes `FB.login`; the service must never call it. */
interface FacebookSdkWithGlobalProps {
  init(options: unknown): void;
  readonly AppEvents?: { readonly logPageView?: () => void };
  readonly getLoginStatus?: (
    callback: FacebookLoginStatusCallback,
    params?: FacebookGetLoginStatusParams,
  ) => void;
  readonly login?: jest.Mock;
}

function createSdk(respond?: (callback: FacebookLoginStatusCallback) => void): SdkMock {
  const init = jest.fn();
  const logPageView = jest.fn();
  const login = jest.fn();
  const getLoginStatus = jest.fn(
    (callback: FacebookLoginStatusCallback, _params?: FacebookGetLoginStatusParams) => {
      respond?.(callback);
    },
  );
  const sdk: FacebookSdkWithGlobalProps = { init, AppEvents: { logPageView }, getLoginStatus, login };
  return { sdk, init, logPageView, login, getLoginStatus };
}

function setAppId(value: string): void {
  (environment as { metaAppId: string }).metaAppId = value;
}

function createService(): FacebookSdkService {
  TestBed.configureTestingModule({});
  return TestBed.inject(FacebookSdkService);
}

/** Lets the `load()` microtasks settle before fake timers take over. */
async function flushMicrotasks(times = 10): Promise<void> {
  for (let index = 0; index < times; index += 1) await Promise.resolve();
}

describe('FacebookSdkService', () => {
  const originalAppId = environment.metaAppId;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    setAppId(APP_ID);
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    document.querySelectorAll(`script#${FACEBOOK_SDK_SCRIPT_ID}`).forEach((script) => script.remove());
    delete window.FB;
    delete window.fbAsyncInit;
    setAppId(originalAppId);
    jest.useRealTimers();
    jest.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('stays disabled and loads nothing when metaAppId is empty', async () => {
    setAppId('');
    const service = createService();

    expect(service.isConfigured).toBe(false);
    await expect(service.load()).resolves.toBeNull();
    await expect(service.getLoginStatus()).resolves.toBeNull();
    expect(document.querySelector(`script#${FACEBOOK_SDK_SCRIPT_ID}`)).toBeNull();
  });

  it('resolves null when the loaded SDK does not expose getLoginStatus', async () => {
    const init = jest.fn();
    window.FB = { init };
    const service = createService();

    await expect(service.getLoginStatus()).resolves.toBeNull();
    // The loader still initialized the SDK (AppEvents flow stays untouched).
    expect(init).toHaveBeenCalledTimes(1);
  });

  it('returns the connected payload without replacing urbeat authentication', async () => {
    const loginSpy = jest.spyOn(AuthService.prototype, 'login');
    const loginSellerSpy = jest.spyOn(AuthService.prototype, 'loginSeller');
    const loginAdminSpy = jest.spyOn(AuthService.prototype, 'loginAdmin');
    const saveTokenSpy = jest.spyOn(AuthService.prototype, 'saveToken');

    const mock = createSdk((callback) =>
      callback({ status: 'connected', authResponse: CONNECTED_AUTH }),
    );
    window.FB = mock.sdk;
    const service = createService();

    await expect(service.getLoginStatus()).resolves.toEqual({
      status: 'connected',
      authResponse: CONNECTED_AUTH,
    });

    expect(mock.getLoginStatus).toHaveBeenCalledTimes(1);
    expect(mock.login).not.toHaveBeenCalled();
    expect(loginSpy).not.toHaveBeenCalled();
    expect(loginSellerSpy).not.toHaveBeenCalled();
    expect(loginAdminSpy).not.toHaveBeenCalled();
    expect(saveTokenSpy).not.toHaveBeenCalled();
    expect(localStorage.getItem('urbeat_token')).toBeNull();
    expect(sessionStorage.getItem('urbeat_seller_token')).toBeNull();
    expect(localStorage.length + sessionStorage.length).toBe(0);
  });

  it('keeps FB.AppEvents.logPageView from the loader', async () => {
    const mock = createSdk((callback) => callback({ status: 'unknown' }));
    window.FB = mock.sdk;
    const service = createService();

    await service.getLoginStatus();

    expect(mock.init).toHaveBeenCalledTimes(1);
    expect(mock.logPageView).toHaveBeenCalledTimes(1);
  });

  it.each<FacebookLoginStatus>(['not_authorized', 'unknown'])(
    'normalises the %s response without authResponse',
    async (status) => {
      const mock = createSdk((callback) => callback({ status }));
      window.FB = mock.sdk;
      const service = createService();

      await expect(service.getLoginStatus()).resolves.toEqual({ status });
      expect(mock.getLoginStatus).toHaveBeenCalledTimes(1);
    },
  );

  it('calls FB.getLoginStatus once, with the callback only', async () => {
    const mock = createSdk((callback) => callback({ status: 'unknown' }));
    window.FB = mock.sdk;
    const service = createService();

    await service.getLoginStatus();

    expect(mock.getLoginStatus).toHaveBeenCalledTimes(1);
    expect(mock.getLoginStatus.mock.calls[0]).toHaveLength(1);
    expect(mock.getLoginStatus.mock.calls[0][0]).toEqual(expect.any(Function));
  });

  it('forwards scope and forceRoundTrip when requested', async () => {
    const mock = createSdk((callback) => callback({ status: 'unknown' }));
    window.FB = mock.sdk;
    const service = createService();

    await service.getLoginStatus({ scope: 'email', forceRoundTrip: true });

    expect(mock.getLoginStatus).toHaveBeenCalledTimes(1);
    expect(mock.getLoginStatus).toHaveBeenCalledWith(expect.any(Function), {
      scope: 'email',
      forceRoundTrip: true,
    });
  });

  it('ignores a second callback invocation', async () => {
    const mock = createSdk((callback) => {
      callback({ status: 'not_authorized' });
      callback({ status: 'connected', authResponse: CONNECTED_AUTH });
    });
    window.FB = mock.sdk;
    const service = createService();

    await expect(service.getLoginStatus()).resolves.toEqual({ status: 'not_authorized' });
  });

  it('resolves null when FB.getLoginStatus throws synchronously', async () => {
    const mock = createSdk();
    mock.getLoginStatus.mockImplementation(() => {
      throw new Error('SDK blocked');
    });
    window.FB = mock.sdk;
    const service = createService();

    await expect(service.getLoginStatus()).resolves.toBeNull();
    expect(mock.getLoginStatus).toHaveBeenCalledTimes(1);
  });

  it.each<[string, unknown]>([
    ['undefined', undefined],
    ['null', null],
    ['an unknown status', { status: 'expired' }],
    ['a non-object payload', 'connected'],
    ['a connected status without token', { status: 'connected', authResponse: {} }],
  ])('resolves null for %s', async (_label, payload) => {
    const mock = createSdk((callback) =>
      callback(payload as unknown as Parameters<FacebookLoginStatusCallback>[0]),
    );
    window.FB = mock.sdk;
    const service = createService();

    await expect(service.getLoginStatus()).resolves.toBeNull();
  });

  it('resolves null instead of hanging when the SDK never calls back', async () => {
    const mock = createSdk();
    window.FB = mock.sdk;
    const service = createService();

    await expect(service.getLoginStatus({ timeoutMs: 10 })).resolves.toBeNull();
    expect(mock.getLoginStatus).toHaveBeenCalledTimes(1);
  });

  it('uses the default timeout when the given one is invalid', async () => {
    jest.useFakeTimers();
    const mock = createSdk();
    window.FB = mock.sdk;
    const service = createService();

    const pending = service.getLoginStatus({ timeoutMs: -5 });
    await flushMicrotasks();
    await jest.advanceTimersByTimeAsync(FACEBOOK_LOGIN_STATUS_TIMEOUT_MS);

    await expect(pending).resolves.toBeNull();
  });

  it('resolves null when the SDK script never loads', async () => {
    jest.useFakeTimers();
    const service = createService();

    const pending = service.getLoginStatus();
    expect(document.querySelector(`script#${FACEBOOK_SDK_SCRIPT_ID}`)).not.toBeNull();

    await flushMicrotasks();
    await jest.advanceTimersByTimeAsync(FACEBOOK_SDK_TIMEOUT_MS);
    await expect(pending).resolves.toBeNull();
  });
});
