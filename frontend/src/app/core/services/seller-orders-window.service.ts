import { Injectable, computed, signal } from '@angular/core';

/** Namespaced channel/key shared by the dashboard and the detached orders window. */
export const SELLER_ORDERS_WINDOW_CHANNEL = 'urbeat-seller-orders-window';
export const SELLER_ORDERS_WINDOW_ROUTE = '/app/pedidos-separados';
export const SELLER_ORDERS_WINDOW_NAME = 'urbeat-orders-window';

export type SellerOrdersWindowMessageType =
  | 'detached-open'
  | 'detached-ready'
  | 'detached-close'
  | 'secondary-closed';

export type SellerOrdersWindowSource = 'host' | 'secondary';

export interface SellerOrdersWindowMessage {
  type: SellerOrdersWindowMessageType;
  source: SellerOrdersWindowSource;
  sentAt: number;
}

const CLOSED_WINDOW_POLL_MS = 1_000;

/**
 * Coordinates the optional second-monitor orders window.
 *
 * The dashboard instance owns `isDetached`: while it is true the host board is
 * grayed out and does not process new orders. The `/app/pedidos-separados`
 * instance only marks `isDetachedRoute` and keeps the live board/SignalR
 * pipeline. Both exchange lifecycle messages through `BroadcastChannel`, with
 * a `localStorage` storage-event fallback for environments without it.
 *
 * The detached window is a regular browser window so the seller can move it
 * with the native title bar before entering fullscreen; no faux HTML chrome is
 * used. Popup blockers and closed windows always restore the host mode.
 */
@Injectable({ providedIn: 'root' })
export class SellerOrdersWindowService {
  /** True on the dashboard instance while the seller works in the detached window. */
  readonly isDetached = signal(false);
  /** True on the dedicated `/app/pedidos-separados` instance. */
  readonly isDetachedRoute = signal(false);
  readonly secondaryWindowOpen = signal(false);

  /** Host board suppression: only the dashboard instance is grayed out. */
  readonly isDetachedHost = computed(() => this.isDetached() && !this.isDetachedRoute());

  private channel: BroadcastChannel | null = null;
  private secondaryWindow: Window | null = null;
  private closedWindowTimer: ReturnType<typeof setInterval> | null = null;
  private storageListener?: (event: StorageEvent) => void;
  private disposed = false;

  constructor() {
    this.attachChannel();
    this.attachStorageFallback();
  }

  /**
   * Opens (or focuses) the detached window. Must be called synchronously from
   * the seller click so the browser counts it as a user gesture.
   *
   * @returns false when the popup was blocked and the host must stay normal.
   */
  openDetachedWindow(): boolean {
    if (this.disposed || this.isDetachedRoute()) return false;

    const existing = this.secondaryWindow;
    if (existing && !existing.closed) {
      this.focusSecondary(existing);
      return true;
    }

    let opened: Window | null = null;
    try {
      opened = window.open(
        SELLER_ORDERS_WINDOW_ROUTE,
        SELLER_ORDERS_WINDOW_NAME,
        'popup=yes,width=1400,height=900',
      );
    } catch {
      opened = null;
    }

    if (!opened) {
      // Blocked popup: never leave the dashboard stuck in detached mode.
      return false;
    }

    this.secondaryWindow = opened;
    this.secondaryWindowOpen.set(true);
    this.isDetached.set(true);
    this.publish('detached-open');
    this.startClosedWindowMonitor();
    return true;
  }

  /**
   * Ends the detached mode. On the dashboard it also closes the secondary
   * window; on the secondary route it closes itself and the host restores.
   */
  closeDetachedWindow(): void {
    const target = this.secondaryWindow ?? (this.isDetachedRoute() ? window : null);
    this.publish('detached-close');
    this.resetDetachedState();
    this.closeWindow(target);
  }

  /**
   * Registers this instance as the `/app/pedidos-separados` window and asks
   * the dashboard to confirm the detached mode.
   */
  markDetachedRoute(): void {
    if (this.disposed || this.isDetachedRoute()) return;
    this.isDetachedRoute.set(true);
    this.publish('detached-ready');
  }

  /**
   * Releases the `/app/pedidos-separados` view when an in-app navigation
   * destroys it without "Fixar ao Dashboard".
   *
   * `secondary-closed` is published before the route state is reset so the
   * message is attributed to the secondary instance, which restores the grayed
   * out host board. The host never answers this message, so the handshake
   * cannot loop. The browser window is not closed and the channel stays alive,
   * so navigating back to the route can register it again; full teardown
   * remains in `dispose()`.
   */
  releaseDetachedRoute(): void {
    if (this.disposed || !this.isDetachedRoute()) return;
    this.publish('secondary-closed');
    this.isDetachedRoute.set(false);
    this.resetDetachedState();
  }

  /**
   * Detects a secondary window closed by the user (or by the browser) and
   * restores the dashboard, keeping the already loaded order state intact.
   */
  restoreFromClosedSecondary(): void {
    if (this.disposed || this.isDetachedRoute()) return;
    if (!this.isDetached() && !this.secondaryWindowOpen() && !this.secondaryWindow) return;

    this.secondaryWindow = null;
    this.clearClosedWindowMonitor();
    this.publish('secondary-closed');
    this.resetDetachedState();
  }

  /**
   * Releases the channel, storage listener and closed-window monitor. This is
   * the terminal teardown: it never publishes a lifecycle message nor closes
   * the secondary window. Route destruction uses `releaseDetachedRoute()`.
   */
  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.clearClosedWindowMonitor();
    this.secondaryWindow = null;

    if (this.channel) {
      this.channel.onmessage = null;
      try {
        this.channel.close();
      } catch {
        // Channel already closed by the browser during unload.
      }
      this.channel = null;
    }

    if (this.storageListener) {
      window.removeEventListener('storage', this.storageListener);
      this.storageListener = undefined;
    }

    this.isDetached.set(false);
    this.isDetachedRoute.set(false);
    this.secondaryWindowOpen.set(false);
  }

  private attachChannel(): void {
    if (typeof BroadcastChannel === 'undefined') return;
    try {
      this.channel = new BroadcastChannel(SELLER_ORDERS_WINDOW_CHANNEL);
      this.channel.onmessage = (event: MessageEvent<unknown>) => this.handleMessage(event.data);
    } catch {
      this.channel = null;
    }
  }

  private attachStorageFallback(): void {
    this.storageListener = (event: StorageEvent) => this.handleStorageEvent(event);
    try {
      window.addEventListener('storage', this.storageListener);
    } catch {
      this.storageListener = undefined;
    }
  }

  private handleStorageEvent(event: StorageEvent): void {
    // The storage event is only a fallback; with a live channel the message was
    // already delivered by BroadcastChannel and must not be handled twice.
    if (this.channel) return;
    if (event.key !== SELLER_ORDERS_WINDOW_CHANNEL || !event.newValue) return;

    let payload: unknown;
    try {
      payload = JSON.parse(event.newValue);
    } catch {
      return;
    }
    this.handleMessage(payload);
  }

  private handleMessage(payload: unknown): void {
    if (this.disposed || !payload || typeof payload !== 'object') return;
    const message = payload as Partial<SellerOrdersWindowMessage>;

    switch (message.type) {
      case 'detached-ready':
        // The secondary is alive: reassert open so a reloaded secondary keeps
        // the host board grayed out.
        if (!this.isDetachedRoute() && this.isDetached()) {
          this.publish('detached-open');
        }
        break;
      case 'detached-close':
        if (this.isDetachedRoute()) {
          // The dashboard fixed the window: close this secondary instance.
          this.resetDetachedState();
          this.closeWindow(window);
        } else {
          this.resetDetachedState();
          this.closeWindow(this.secondaryWindow);
        }
        break;
      case 'secondary-closed':
        if (!this.isDetachedRoute()) this.resetDetachedState();
        break;
      case 'detached-open':
      default:
        break;
    }
  }

  private publish(type: SellerOrdersWindowMessageType): void {
    if (this.disposed) return;
    const message: SellerOrdersWindowMessage = {
      type,
      source: this.isDetachedRoute() ? 'secondary' : 'host',
      sentAt: Date.now(),
    };

    if (this.channel) {
      try {
        this.channel.postMessage(message);
        return;
      } catch {
        // Fall through to the storage fallback.
      }
    }

    this.writeStorageMessage(message);
  }

  private writeStorageMessage(message: SellerOrdersWindowMessage): void {
    try {
      localStorage.setItem(SELLER_ORDERS_WINDOW_CHANNEL, JSON.stringify(message));
    } catch {
      // Storage unavailable; the in-memory state still restores the dashboard.
    }
  }

  private startClosedWindowMonitor(): void {
    this.clearClosedWindowMonitor();
    this.closedWindowTimer = setInterval(() => {
      if (this.secondaryWindow?.closed) {
        this.restoreFromClosedSecondary();
      }
    }, CLOSED_WINDOW_POLL_MS);
  }

  private clearClosedWindowMonitor(): void {
    if (this.closedWindowTimer !== null) {
      clearInterval(this.closedWindowTimer);
      this.closedWindowTimer = null;
    }
  }

  private resetDetachedState(): void {
    this.clearClosedWindowMonitor();
    this.secondaryWindow = null;
    this.secondaryWindowOpen.set(false);
    this.isDetached.set(false);
  }

  private focusSecondary(target: Window): void {
    try {
      target.focus();
    } catch {
      // Focus can be denied by the browser; mode stays consistent.
    }
  }

  private closeWindow(target: Window | null): void {
    if (!target || target.closed) return;
    try {
      target.close();
    } catch {
      // The browser may refuse to close a window it did not open.
    }
  }
}
