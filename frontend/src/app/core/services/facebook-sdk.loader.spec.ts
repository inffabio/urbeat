import {
  DEFAULT_FACEBOOK_API_VERSION,
  FACEBOOK_SDK_SCRIPT_ID,
  FACEBOOK_SDK_SCRIPT_SRC,
  FACEBOOK_SDK_TIMEOUT_MS,
  FacebookSdkLoader,
  type FacebookSdkLoadOptions,
} from './facebook-sdk.loader';
import type { FacebookSdk } from './facebook-sdk.types';

const OPTIONS: FacebookSdkLoadOptions = { appId: '1234567890', apiVersion: 'v21.0' };

interface SdkMock {
  readonly sdk: FacebookSdk;
  readonly init: jest.Mock;
  readonly logPageView: jest.Mock;
}

function createSdk(options: { withAppEvents?: boolean } = {}): SdkMock {
  const init = jest.fn();
  const logPageView = jest.fn();
  const sdk: FacebookSdk = options.withAppEvents === false
    ? { init }
    : { init, AppEvents: { logPageView } };
  return { sdk, init, logPageView };
}

function scriptTags(): HTMLScriptElement[] {
  return Array.from(document.querySelectorAll<HTMLScriptElement>(`script#${FACEBOOK_SDK_SCRIPT_ID}`));
}

function removeScriptTags(): void {
  scriptTags().forEach((script) => script.remove());
}

describe('FacebookSdkLoader', () => {
  afterEach(() => {
    removeScriptTags();
    delete window.FB;
    delete window.fbAsyncInit;
    jest.useRealTimers();
  });

  it('does not touch the DOM when the app id is empty', async () => {
    const loader = new FacebookSdkLoader(document);

    await expect(loader.load({ appId: '   ' })).resolves.toBeNull();

    expect(scriptTags()).toHaveLength(0);
    expect(window.fbAsyncInit).toBeUndefined();
  });

  it('does not touch the DOM without a document (SSR/non-browser)', async () => {
    const loader = new FacebookSdkLoader(document);

    await expect(loader.load({ ...OPTIONS, document: null })).resolves.toBeNull();
    await expect(new FacebookSdkLoader(null).load(OPTIONS)).resolves.toBeNull();

    expect(scriptTags()).toHaveLength(0);
  });

  it('inserts the SDK script once and reuses the pending promise', async () => {
    jest.useFakeTimers();
    const loader = new FacebookSdkLoader(document);

    const first = loader.load(OPTIONS);
    const second = loader.load(OPTIONS);

    expect(scriptTags()).toHaveLength(1);
    expect(scriptTags()[0].src).toBe(FACEBOOK_SDK_SCRIPT_SRC);
    expect(scriptTags()[0].async).toBe(true);
    expect(second).toBe(first);

    jest.advanceTimersByTime(FACEBOOK_SDK_TIMEOUT_MS);
    await expect(first).resolves.toBeNull();
  });

  it('reuses an existing facebook-jssdk script instead of duplicating it', async () => {
    jest.useFakeTimers();
    const existing = document.createElement('script');
    existing.id = FACEBOOK_SDK_SCRIPT_ID;
    document.head.appendChild(existing);

    const loader = new FacebookSdkLoader(document);
    const pending = loader.load(OPTIONS);

    expect(scriptTags()).toHaveLength(1);
    expect(scriptTags()[0]).toBe(existing);

    jest.advanceTimersByTime(FACEBOOK_SDK_TIMEOUT_MS);
    await expect(pending).resolves.toBeNull();
  });

  it('initializes FB with the configured app id and version, then logs a page view', async () => {
    const loader = new FacebookSdkLoader(document);
    const { sdk, init, logPageView } = createSdk();

    const pending = loader.load(OPTIONS);
    window.FB = sdk;
    window.fbAsyncInit?.();

    await expect(pending).resolves.toBe(sdk);
    expect(init).toHaveBeenCalledTimes(1);
    expect(init).toHaveBeenCalledWith({
      appId: '1234567890',
      cookie: true,
      xfbml: true,
      version: 'v21.0',
    });
    expect(logPageView).toHaveBeenCalledTimes(1);
    expect(scriptTags()).toHaveLength(1);
  });

  it('falls back to the pinned API version when none is configured', async () => {
    const loader = new FacebookSdkLoader(document);
    const { sdk, init } = createSdk();

    const pending = loader.load({ appId: '1234567890' });
    window.FB = sdk;
    window.fbAsyncInit?.();

    await pending;
    expect(init).toHaveBeenCalledWith(
      expect.objectContaining({ version: DEFAULT_FACEBOOK_API_VERSION }),
    );
  });

  it('works when FB.AppEvents is not exposed by the SDK', async () => {
    const loader = new FacebookSdkLoader(document);
    const { sdk, init } = createSdk({ withAppEvents: false });

    const pending = loader.load(OPTIONS);
    window.FB = sdk;
    expect(() => window.fbAsyncInit?.()).not.toThrow();

    await expect(pending).resolves.toBe(sdk);
    expect(init).toHaveBeenCalledTimes(1);
  });

  it('resolves null on timeout without rejecting boot', async () => {
    jest.useFakeTimers();
    const loader = new FacebookSdkLoader(document);

    const pending = loader.load(OPTIONS);
    jest.advanceTimersByTime(FACEBOOK_SDK_TIMEOUT_MS);

    await expect(pending).resolves.toBeNull();
  });

  it('resolves null when the script fails to load', async () => {
    const loader = new FacebookSdkLoader(document);

    const pending = loader.load(OPTIONS);
    scriptTags()[0].dispatchEvent(new Event('error'));

    await expect(pending).resolves.toBeNull();
  });

  it('reuses an already loaded SDK without injecting a new script', async () => {
    const loader = new FacebookSdkLoader(document);
    const { sdk, init } = createSdk();
    window.FB = sdk;

    await expect(loader.load(OPTIONS)).resolves.toBe(sdk);

    expect(init).toHaveBeenCalledTimes(1);
    expect(scriptTags()).toHaveLength(0);
  });

  it('retries after a failed attempt instead of caching the failure', async () => {
    const loader = new FacebookSdkLoader(document);

    const failed = loader.load(OPTIONS);
    scriptTags()[0].dispatchEvent(new Event('error'));
    await expect(failed).resolves.toBeNull();

    const retry = loader.load(OPTIONS);
    expect(scriptTags()).toHaveLength(1);

    const { sdk } = createSdk();
    window.FB = sdk;
    window.fbAsyncInit?.();
    await expect(retry).resolves.toBe(sdk);
  });
});
