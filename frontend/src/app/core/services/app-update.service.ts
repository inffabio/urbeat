import { DOCUMENT } from '@angular/common';
import {
  ApplicationRef,
  Injectable,
  InjectionToken,
  NgZone,
  OnDestroy,
  inject,
} from '@angular/core';
import { NavigationError, Router } from '@angular/router';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { Observable, Subscription, filter, map, take } from 'rxjs';

export const INITIAL_UPDATE_CHECK_FALLBACK_MS = 10_000;
export const PERIODIC_UPDATE_CHECK_MS = 60 * 60 * 1000;
export const UPDATE_CHECK_TIMEOUT_MS = 30_000;
export const UPDATE_CHECK_RETRY_MS = 60_000;
export const ACTIVATION_RETRY_MS = 15_000;
export const SERVICE_WORKER_READY_TIMEOUT_MS = 5_000;
/** Activation is a local postMessage; a silent service worker must not block the recovery. */
export const ACTIVATION_TIMEOUT_MS = 5_000;
/** A guarded reload can be suppressed right after another one, so it is retried later. */
export const RECOVERY_RETRY_MS = 15_000;
/** A broken deployment must never become a reload loop: at most two guarded attempts. */
export const MAX_RECOVERY_RELOADS = 2;
const RELOAD_LOOP_GUARD_MS = 10_000;
const LAST_RELOAD_KEY = 'urbeat:sw-last-reload';

/**
 * Lazy route chunks are hash-named per deployment. A client that keeps an older
 * document (service worker cache, back/forward cache or a deploy window) asks
 * for a chunk that no longer exists; the router aborts the navigation and the
 * route stays empty, which looks like a blank page. Only a new document can
 * load the new bundle, so these failures share the guarded reload.
 */
const CHUNK_LOAD_FAILURE_PATTERNS: readonly RegExp[] = [
  /Loading chunk [\w-]+ failed/i,
  /Loading CSS chunk/i,
  /Failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /Importing a module script failed/i,
  /Expected a JavaScript module script but the server responded with a MIME type of "?text\/html/i,
  /is not a valid JavaScript MIME type/i,
];

/** True when the browser could not load a hash-named bundle from a previous deployment. */
export function isChunkLoadFailure(error: unknown): boolean {
  if (error === null || error === undefined) return false;

  if (typeof error === 'object') {
    const name = (error as { name?: unknown }).name;
    if (typeof name === 'string' && name === 'ChunkLoadError') return true;
  }

  const message =
    typeof error === 'string'
      ? error
      : typeof error === 'object' && typeof (error as { message?: unknown }).message === 'string'
        ? String((error as { message: string }).message)
        : '';

  return CHUNK_LOAD_FAILURE_PATTERNS.some((pattern) => pattern.test(message));
}

/**
 * Resolves with the promise value, or with `null` when it rejects or does not
 * settle in time, so a pending service worker operation never blocks recovery.
 */
function settleWithin<T>(promise: Promise<T>, timeoutMs: number): Promise<T | null> {
  return new Promise((resolve) => {
    const handle = setTimeout(() => resolve(null), timeoutMs);
    const settle = (value: T | null): void => {
      clearTimeout(handle);
      resolve(value);
    };
    promise.then(settle, () => settle(null));
  });
}

export const APP_STABILITY = new InjectionToken<Observable<boolean>>('APP_STABILITY', {
  providedIn: 'root',
  factory: () => inject(ApplicationRef).isStable,
});

/**
 * Errors that aborted a router navigation. Exposed as a token so the service
 * stays testable without a real router, mirroring `APP_STABILITY`.
 */
export const APP_NAVIGATION_FAILURES = new InjectionToken<Observable<unknown>>(
  'APP_NAVIGATION_FAILURES',
  {
    providedIn: 'root',
    factory: () =>
      inject(Router).events.pipe(
        filter((event): event is NavigationError => event instanceof NavigationError),
        map((event) => event.error),
      ),
  },
);

@Injectable({ providedIn: 'root' })
export class AppUpdateService implements OnDestroy {
  private readonly swUpdate = inject(SwUpdate);
  private readonly stability = inject(APP_STABILITY);
  private readonly navigationFailures = inject(APP_NAVIGATION_FAILURES);
  private readonly ngZone = inject(NgZone);
  private readonly document = inject(DOCUMENT);

  private started = false;
  private initialCheckStarted = false;
  private reloadRequested = false;
  private chunkRecoveryStarted = false;
  private recoveryReloads = 0;
  private checkInFlight = false;
  private initialCheckFallbackHandle: ReturnType<typeof setTimeout> | null = null;
  private retryHandle: ReturnType<typeof setTimeout> | null = null;
  private activationRetryHandle: ReturnType<typeof setTimeout> | null = null;
  private recoveryRetryHandle: ReturnType<typeof setTimeout> | null = null;
  private periodicHandle: ReturnType<typeof setInterval> | null = null;
  private readonly subscriptions = new Subscription();

  private readonly onVisibilityChange = (): void => {
    if (this.document.visibilityState === 'visible') this.checkForUpdate();
  };

  init(): void {
    if (this.started || !this.swUpdate.isEnabled) return;
    this.started = true;

    this.ngZone.runOutsideAngular(() => {
      this.subscriptions.add(
        this.swUpdate.versionUpdates
          .pipe(filter((event): event is VersionReadyEvent => event.type === 'VERSION_READY'))
          .subscribe(() => void this.activateAndReload()),
      );

      this.subscriptions.add(
        this.swUpdate.unrecoverable.subscribe(() => this.requestRecoveryReload()),
      );

      this.subscriptions.add(
        this.navigationFailures.subscribe((error) => this.recoverFromStaleChunk(error)),
      );

      this.scheduleInitialUpdateCheck();
      this.schedulePeriodicUpdateCheck();
      this.listenForVisibilityChanges();
    });
  }

  ngOnDestroy(): void {
    this.document.removeEventListener('visibilitychange', this.onVisibilityChange);
    this.subscriptions.unsubscribe();
    this.clearInitialCheckFallback();
    this.clearRetry();
    this.clearActivationRetry();
    this.clearRecoveryRetry();
    if (this.periodicHandle !== null) {
      clearInterval(this.periodicHandle);
      this.periodicHandle = null;
    }
  }

  private scheduleInitialUpdateCheck(): void {
    this.initialCheckFallbackHandle = setTimeout(
      () => void this.runInitialCheck(),
      INITIAL_UPDATE_CHECK_FALLBACK_MS,
    );
    this.subscriptions.add(
      this.stability
        .pipe(
          filter((stable) => stable),
          take(1),
        )
        .subscribe(() => void this.runInitialCheck()),
    );
  }

  private async runInitialCheck(): Promise<void> {
    if (this.initialCheckStarted) return;
    this.initialCheckStarted = true;
    this.clearInitialCheckFallback();
    await this.waitForServiceWorkerReady();
    this.checkForUpdate();
  }

  private clearInitialCheckFallback(): void {
    if (this.initialCheckFallbackHandle === null) return;
    clearTimeout(this.initialCheckFallbackHandle);
    this.initialCheckFallbackHandle = null;
  }

  private waitForServiceWorkerReady(): Promise<void> {
    const container = this.document.defaultView?.navigator?.serviceWorker;
    if (!container || typeof container.ready?.then !== 'function') return Promise.resolve();

    return new Promise<void>((resolve) => {
      const settle = (): void => {
        clearTimeout(handle);
        resolve();
      };
      const handle = setTimeout(settle, SERVICE_WORKER_READY_TIMEOUT_MS);
      container.ready.then(settle, settle);
    });
  }

  private schedulePeriodicUpdateCheck(): void {
    this.periodicHandle = setInterval(() => this.checkForUpdate(), PERIODIC_UPDATE_CHECK_MS);
  }

  private listenForVisibilityChanges(): void {
    this.document.addEventListener('visibilitychange', this.onVisibilityChange);
  }

  private checkForUpdate(): void {
    if (this.checkInFlight) return;
    this.clearRetry();
    this.checkInFlight = true;

    let settled = false;
    const timeout = setTimeout(() => {
      if (settled) return;
      settled = true;
      this.checkInFlight = false;
      this.recoverFromHungCheck();
    }, UPDATE_CHECK_TIMEOUT_MS);

    const settle = (retry: boolean): void => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      this.checkInFlight = false;
      if (retry) this.scheduleRetry();
    };

    this.swUpdate.checkForUpdate().then(
      () => settle(false),
      () => settle(true),
    );
  }

  private recoverFromHungCheck(): void {
    // Angular's SwUpdate deduplicates concurrent checkForUpdate calls and
    // returns the same pending promise, so retrying cannot recover a hung
    // check. A guarded full reload re-syncs the client with the active version
    // while the loop guard prevents a reload storm.
    this.tryReloadPage();
  }

  private scheduleRetry(): void {
    if (this.retryHandle !== null) return;
    this.retryHandle = setTimeout(() => {
      this.retryHandle = null;
      this.checkForUpdate();
    }, UPDATE_CHECK_RETRY_MS);
  }

  private clearRetry(): void {
    if (this.retryHandle === null) return;
    clearTimeout(this.retryHandle);
    this.retryHandle = null;
  }

  /**
   * A lazy chunk from a previous deployment no longer exists on the server.
   * Activating the pending version first makes the reload land on the deployed
   * bundle instead of the stale document, and the whole recovery runs at most
   * once per document.
   */
  private recoverFromStaleChunk(error: unknown): void {
    if (this.chunkRecoveryStarted || !isChunkLoadFailure(error)) return;
    this.chunkRecoveryStarted = true;
    void this.activateAndReloadStaleChunk();
  }

  private async activateAndReloadStaleChunk(): Promise<void> {
    // Activating only helps a document the worker actually controls; otherwise
    // it would wait on `navigator.serviceWorker.ready` for nothing.
    if (this.hasServiceWorkerController()) {
      await settleWithin(this.swUpdate.activateUpdate(), ACTIVATION_TIMEOUT_MS);
    }
    this.requestRecoveryReload();
  }

  private hasServiceWorkerController(): boolean {
    return Boolean(this.document.defaultView?.navigator?.serviceWorker?.controller);
  }

  /**
   * Guarded reload shared by the unrecoverable state and stale chunk paths.
   * A reload that the loop guard suppresses is retried once, so an app that
   * broke right after another reload still recovers instead of staying blank.
   */
  private requestRecoveryReload(): void {
    if (this.recoveryReloads >= MAX_RECOVERY_RELOADS) return;
    this.recoveryReloads += 1;

    if (this.tryReloadPage()) {
      this.clearRecoveryRetry();
      return;
    }
    this.scheduleRecoveryRetry();
  }

  private scheduleRecoveryRetry(): void {
    if (this.recoveryRetryHandle !== null) return;
    this.recoveryRetryHandle = setTimeout(() => {
      this.recoveryRetryHandle = null;
      this.requestRecoveryReload();
    }, RECOVERY_RETRY_MS);
  }

  private clearRecoveryRetry(): void {
    if (this.recoveryRetryHandle === null) return;
    clearTimeout(this.recoveryRetryHandle);
    this.recoveryRetryHandle = null;
  }

  private async activateAndReload(): Promise<void> {
    if (this.reloadRequested) return;
    this.reloadRequested = true;
    this.clearActivationRetry();

    try {
      await this.swUpdate.activateUpdate();
    } catch {
      /* A full reload still re-syncs the client with the active version. */
    }

    if (!this.tryReloadPage()) {
      this.reloadRequested = false;
      this.scheduleActivationRetry();
    }
  }

  private scheduleActivationRetry(): void {
    if (this.activationRetryHandle !== null) return;
    this.activationRetryHandle = setTimeout(() => {
      this.activationRetryHandle = null;
      void this.activateAndReload();
    }, ACTIVATION_RETRY_MS);
  }

  private clearActivationRetry(): void {
    if (this.activationRetryHandle === null) return;
    clearTimeout(this.activationRetryHandle);
    this.activationRetryHandle = null;
  }

  private tryReloadPage(): boolean {
    if (this.reloadedRecently()) return false;
    this.rememberReload();
    this.document.defaultView?.location.reload();
    return true;
  }

  private reloadedRecently(): boolean {
    try {
      const last = Number(this.document.defaultView?.sessionStorage?.getItem(LAST_RELOAD_KEY) ?? 0);
      return Date.now() - last < RELOAD_LOOP_GUARD_MS;
    } catch {
      return false;
    }
  }

  private rememberReload(): void {
    try {
      this.document.defaultView?.sessionStorage?.setItem(LAST_RELOAD_KEY, String(Date.now()));
    } catch {
      /* Storage unavailable; the in-memory guard still prevents duplicates. */
    }
  }
}
