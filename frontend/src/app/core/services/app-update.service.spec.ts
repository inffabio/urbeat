import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import {
  ACTIVATION_RETRY_MS,
  APP_STABILITY,
  AppUpdateService,
  INITIAL_UPDATE_CHECK_FALLBACK_MS,
  PERIODIC_UPDATE_CHECK_MS,
  SERVICE_WORKER_READY_TIMEOUT_MS,
  UPDATE_CHECK_RETRY_MS,
  UPDATE_CHECK_TIMEOUT_MS,
} from './app-update.service';

interface SwUpdateMock {
  isEnabled: boolean;
  versionUpdates: Subject<unknown>;
  unrecoverable: Subject<unknown>;
  activateUpdate: jest.Mock;
  checkForUpdate: jest.Mock;
}

type DocumentListener = () => void;

interface DocumentMock {
  defaultView: unknown;
  visibilityState: string;
  addEventListener: (type: string, listener: DocumentListener) => void;
  removeEventListener: (type: string, listener: DocumentListener) => void;
}

function createMemoryStorage(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key: string) => (store.has(key) ? store.get(key)! : null),
    key: (index: number) => Array.from(store.keys())[index] ?? null,
    removeItem: (key: string) => {
      store.delete(key);
    },
    setItem: (key: string, value: string) => {
      store.set(key, String(value));
    },
  } as Storage;
}

async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 6; i += 1) await Promise.resolve();
}

const VERSION_READY = {
  type: 'VERSION_READY',
  currentVersion: { hash: 'old-hash' },
  latestVersion: { hash: 'new-hash' },
};

describe('AppUpdateService', () => {
  let swUpdate: SwUpdateMock;
  let isStable: Subject<boolean>;
  let reload: jest.Mock;
  let sessionStorageMock: Storage;
  let documentListeners: Map<string, Set<DocumentListener>>;
  let documentMock: DocumentMock;
  let service: AppUpdateService;

  function emitDocumentEvent(type: string): void {
    documentListeners.get(type)?.forEach((listener) => listener());
  }

  function setup(isEnabled = true, serviceWorkerReady: Promise<unknown> = Promise.resolve()): void {
    swUpdate = {
      isEnabled,
      versionUpdates: new Subject<unknown>(),
      unrecoverable: new Subject<unknown>(),
      activateUpdate: jest.fn().mockResolvedValue(true),
      checkForUpdate: jest.fn().mockResolvedValue(false),
    };
    isStable = new Subject<boolean>();
    reload = jest.fn();
    sessionStorageMock = createMemoryStorage();
    documentListeners = new Map();

    const view = {
      location: { reload },
      sessionStorage: sessionStorageMock,
      navigator: { serviceWorker: { ready: serviceWorkerReady } },
    };
    documentMock = {
      defaultView: view,
      visibilityState: 'hidden',
      addEventListener: (type: string, listener: DocumentListener) => {
        const listeners = documentListeners.get(type) ?? new Set<DocumentListener>();
        listeners.add(listener);
        documentListeners.set(type, listeners);
      },
      removeEventListener: (type: string, listener: DocumentListener) => {
        documentListeners.get(type)?.delete(listener);
      },
    };

    TestBed.configureTestingModule({
      providers: [
        AppUpdateService,
        { provide: SwUpdate, useValue: swUpdate },
        { provide: APP_STABILITY, useValue: isStable.asObservable() },
        { provide: DOCUMENT, useValue: documentMock },
      ],
    });

    service = TestBed.inject(AppUpdateService);
    service.init();
  }

  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.clearAllTimers();
    jest.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('activates the new version and reloads the page once it is ready', async () => {
    setup();

    swUpdate.versionUpdates.next(VERSION_READY);
    await flushMicrotasks();

    expect(swUpdate.activateUpdate).toHaveBeenCalledTimes(1);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('reloads the page when the service worker reports an unrecoverable state', () => {
    setup();

    swUpdate.unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing asset' });

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does not activate or reload twice for repeated version-ready events', async () => {
    setup();

    swUpdate.versionUpdates.next(VERSION_READY);
    swUpdate.versionUpdates.next(VERSION_READY);
    await flushMicrotasks();

    expect(swUpdate.activateUpdate).toHaveBeenCalledTimes(1);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does nothing when the service worker is disabled (dev mode)', () => {
    setup(false);

    swUpdate.versionUpdates.next(VERSION_READY);
    swUpdate.unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing asset' });
    isStable.next(true);

    expect(swUpdate.activateUpdate).not.toHaveBeenCalled();
    expect(swUpdate.checkForUpdate).not.toHaveBeenCalled();
    expect(reload).not.toHaveBeenCalled();
  });

  it('attaches the visibility listener to the document, not the window', () => {
    setup();

    expect(documentListeners.get('visibilitychange')?.size).toBe(1);
  });

  it('removes the document visibility listener when destroyed', () => {
    setup();

    service.ngOnDestroy();

    expect(documentListeners.get('visibilitychange')?.size ?? 0).toBe(0);
  });

  it('waits for the service worker to be ready before the first check', async () => {
    let resolveReady!: () => void;
    const ready = new Promise<void>((resolve) => {
      resolveReady = resolve;
    });
    setup(true, ready);

    isStable.next(true);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).not.toHaveBeenCalled();

    resolveReady();
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('runs the first check anyway when the service worker never becomes ready', async () => {
    setup(true, new Promise<void>(() => undefined));

    isStable.next(true);
    await flushMicrotasks();
    expect(swUpdate.checkForUpdate).not.toHaveBeenCalled();

    jest.advanceTimersByTime(SERVICE_WORKER_READY_TIMEOUT_MS);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('checks for updates once the application becomes stable', async () => {
    setup();

    isStable.next(true);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('checks for updates after the fallback delay when the application never stabilizes', async () => {
    setup();

    isStable.next(false);
    jest.advanceTimersByTime(INITIAL_UPDATE_CHECK_FALLBACK_MS);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('runs the initial update check at most once', async () => {
    setup();

    isStable.next(true);
    isStable.next(true);
    jest.advanceTimersByTime(INITIAL_UPDATE_CHECK_FALLBACK_MS);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('does not issue a second initial check when stability arrives after the fallback', async () => {
    setup();

    jest.advanceTimersByTime(INITIAL_UPDATE_CHECK_FALLBACK_MS);
    await flushMicrotasks();
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);

    isStable.next(true);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('still reloads when activating the update rejects', async () => {
    setup();
    swUpdate.activateUpdate.mockRejectedValueOnce(new Error('activation failed'));

    swUpdate.versionUpdates.next(VERSION_READY);
    await flushMicrotasks();

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('suppresses a reload that would loop right after a previous one', async () => {
    setup();
    sessionStorageMock.setItem('urbeat:sw-last-reload', String(Date.now()));

    swUpdate.versionUpdates.next(VERSION_READY);
    await flushMicrotasks();

    expect(reload).not.toHaveBeenCalled();
  });

  it('reloads again once the loop guard window has elapsed', async () => {
    setup();
    sessionStorageMock.setItem('urbeat:sw-last-reload', String(Date.now() - 60000));

    swUpdate.versionUpdates.next(VERSION_READY);
    await flushMicrotasks();

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('retries the update check after a rejected check', async () => {
    setup();
    swUpdate.checkForUpdate.mockRejectedValueOnce(new Error('offline'));

    isStable.next(true);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(UPDATE_CHECK_RETRY_MS);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(2);
  });

  it('recovers a hung, deduplicated check with a guarded reload instead of a retry', async () => {
    setup();
    const deduplicated = new Promise<boolean>(() => undefined);
    swUpdate.checkForUpdate.mockReturnValue(deduplicated);

    isStable.next(true);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(UPDATE_CHECK_TIMEOUT_MS);
    await flushMicrotasks();

    // Angular returns the same pending promise on repeated calls, so a retry
    // could never settle. The service re-syncs with a guarded full reload.
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('suppresses the hung-check recovery reload when a reload just happened', async () => {
    setup();
    swUpdate.checkForUpdate.mockReturnValue(new Promise<boolean>(() => undefined));

    isStable.next(true);
    await flushMicrotasks();
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(UPDATE_CHECK_TIMEOUT_MS - 5_000);
    swUpdate.unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing asset' });
    expect(reload).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(5_000);
    await flushMicrotasks();

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does not start another check while one is already in flight', async () => {
    setup();
    swUpdate.checkForUpdate.mockReturnValue(new Promise(() => undefined));

    isStable.next(true);
    await flushMicrotasks();
    isStable.next(true);
    jest.advanceTimersByTime(INITIAL_UPDATE_CHECK_FALLBACK_MS);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('checks for updates periodically in long-lived tabs', async () => {
    setup();

    isStable.next(true);
    await flushMicrotasks();
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(PERIODIC_UPDATE_CHECK_MS);
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(2);
  });

  it('checks for updates when a hidden tab becomes visible again', async () => {
    setup();

    isStable.next(true);
    await flushMicrotasks();
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);

    documentMock.visibilityState = 'visible';
    emitDocumentEvent('visibilitychange');
    await flushMicrotasks();

    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(2);
  });

  it('eventually retries activation when the loop guard suppresses the first reload', async () => {
    setup();
    sessionStorageMock.setItem('urbeat:sw-last-reload', String(Date.now()));
    swUpdate.activateUpdate.mockRejectedValue(new Error('activation failed'));

    swUpdate.versionUpdates.next(VERSION_READY);
    await flushMicrotasks();

    expect(swUpdate.activateUpdate).toHaveBeenCalledTimes(1);
    expect(reload).not.toHaveBeenCalled();

    jest.advanceTimersByTime(ACTIVATION_RETRY_MS);
    await flushMicrotasks();

    expect(swUpdate.activateUpdate).toHaveBeenCalledTimes(2);
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
