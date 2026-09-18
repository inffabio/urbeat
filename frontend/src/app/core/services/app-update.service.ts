import { DOCUMENT } from '@angular/common';
import {
  ApplicationRef,
  Injectable,
  InjectionToken,
  NgZone,
  OnDestroy,
  inject,
} from '@angular/core';
import { SwUpdate, VersionReadyEvent } from '@angular/service-worker';
import { Observable, filter, take } from 'rxjs';

export const INITIAL_UPDATE_CHECK_FALLBACK_MS = 10_000;
export const PERIODIC_UPDATE_CHECK_MS = 60 * 60 * 1000;
export const UPDATE_CHECK_TIMEOUT_MS = 30_000;
export const UPDATE_CHECK_RETRY_MS = 60_000;
export const ACTIVATION_RETRY_MS = 15_000;
export const SERVICE_WORKER_READY_TIMEOUT_MS = 5_000;
const RELOAD_LOOP_GUARD_MS = 10_000;
const LAST_RELOAD_KEY = 'urbeat:sw-last-reload';

export const APP_STABILITY = new InjectionToken<Observable<boolean>>('APP_STABILITY', {
  providedIn: 'root',
  factory: () => inject(ApplicationRef).isStable,
});

@Injectable({ providedIn: 'root' })
export class AppUpdateService implements OnDestroy {
  private readonly swUpdate = inject(SwUpdate);
  private readonly stability = inject(APP_STABILITY);
  private readonly ngZone = inject(NgZone);
  private readonly document = inject(DOCUMENT);

  private started = false;
  private initialCheckStarted = false;
  private reloadRequested = false;
  private checkInFlight = false;
  private initialCheckFallbackHandle: ReturnType<typeof setTimeout> | null = null;
  private retryHandle: ReturnType<typeof setTimeout> | null = null;
  private activationRetryHandle: ReturnType<typeof setTimeout> | null = null;

  private readonly onVisibilityChange = (): void => {
    if (this.document.visibilityState === 'visible') this.checkForUpdate();
  };

  init(): void {
    if (this.started || !this.swUpdate.isEnabled) return;
    this.started = true;

    this.ngZone.runOutsideAngular(() => {
      this.swUpdate.versionUpdates
        .pipe(filter((event): event is VersionReadyEvent => event.type === 'VERSION_READY'))
        .subscribe(() => void this.activateAndReload());

      this.swUpdate.unrecoverable.subscribe(() => this.requestReload());

      this.scheduleInitialUpdateCheck();
      this.schedulePeriodicUpdateCheck();
      this.listenForVisibilityChanges();
    });
  }

  ngOnDestroy(): void {
    this.document.removeEventListener('visibilitychange', this.onVisibilityChange);
  }

  private scheduleInitialUpdateCheck(): void {
    this.initialCheckFallbackHandle = setTimeout(
      () => void this.runInitialCheck(),
      INITIAL_UPDATE_CHECK_FALLBACK_MS,
    );
    this.stability.pipe(filter((stable) => stable), take(1)).subscribe(() => void this.runInitialCheck());
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
    setInterval(() => this.checkForUpdate(), PERIODIC_UPDATE_CHECK_MS);
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

  private requestReload(): void {
    if (this.reloadRequested) return;
    this.reloadRequested = true;

    if (!this.tryReloadPage()) {
      this.reloadRequested = false;
      this.scheduleActivationRetry();
    }
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
