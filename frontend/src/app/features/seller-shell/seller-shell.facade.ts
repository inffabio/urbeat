import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';
import { SellerNotificationService } from '../../core/services/seller-notification.service';
import { SignalRService } from '../../core/services/signalr.service';
import { StoreService } from '../../core/services/store.service';
import { AdminStoreSessionService } from '../../core/services/admin-store-session.service';
import { NotificationType, SellerNotification } from '../../shared/models/seller-notification.model';
import { StoreResponse } from '../../shared/models/store.model';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { OrderSoundAlertService } from './order-sound-alert.service';

export interface OrderActivityPulse {
  id: string;
  orderId?: string;
  source: 'new-order' | 'manual-status-change';
}

/**
 * The shell must never keep its loading state forever because a bootstrap read never
 * settles. The real-time channel is started independently, so a timed-out read can still
 * be retried by reloading the panel.
 */
const PANEL_BOOTSTRAP_TIMEOUT_MS = 15_000;

@Injectable({ providedIn: 'root' })
export class SellerShellFacade {
  private readonly storeService = inject(StoreService);
  private readonly signalR = inject(SignalRService);
  private readonly notificationsApi = inject(SellerNotificationService);
    private readonly sound = inject(OrderSoundAlertService);
    private readonly printing = inject(SellerPrintingService);
  private readonly adminStoreSession = inject(AdminStoreSessionService);

  /**
   * Delegated administrator mode. Notifications, the seller hub and printing belong to the seller
   * identity and must never be started for the administrator (the server rejects them). Orders are
   * refreshed by navigation/reload while the context is active.
   */
  readonly isAdminDelegated = computed(() => this.adminStoreSession.isAdminMode());

  readonly store = signal<StoreResponse | null>(null);
  readonly notifications = signal<SellerNotification[]>([]);
  readonly unreadCount = signal(0);
  readonly ordersCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly newOrderPulse = signal<SellerNotification | null>(null);
  readonly orderActivityPulse = signal<OrderActivityPulse | null>(null);
  readonly soundEnabled = this.sound.enabled;
  readonly soundNeedsActivation = this.sound.needsActivation;
  readonly realtimeConnected = this.signalR.isSellerConnected;
  readonly storeName = computed(() => this.store()?.name ?? 'Minha loja');

  readonly printerWarning = computed(() => {
    const cfg = this.printing.config();
    if (!cfg.autoPrint) return null;
    if (cfg.connectionType === 'android-bluetooth' && this.printing.bluetoothState().status !== 'connected') {
      return 'Impressora desconectada';
    }
    return null;
  });

  private initialized = false;
  private initVersion = 0;
  private statusRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  private realtimeRetryTimer: ReturnType<typeof setTimeout> | null = null;
  private sellerNotificationListener?: (notification: SellerNotification) => void;
  private sellerStateUnsubscribe?: () => void;

  async init(): Promise<void> {
    if (this.initialized) return;
    this.initialized = true;
    const currentVersion = ++this.initVersion;
    this.loading.set(true);
    this.error.set(null);

    try {
      // Delegated administrator: `my-store` already resolved the context store. Seller
      // notifications, SignalR and printing are never started, so the seller path below is not
      // entered at all (calling them would produce 403 and break the loading).
      if (this.isAdminDelegated()) {
        const delegatedStore = await firstValueFrom(
          this.storeService.getMyStore().pipe(timeout(PANEL_BOOTSTRAP_TIMEOUT_MS)),
        );
        if (currentVersion !== this.initVersion) return;
        this.store.set(delegatedStore);
        this.scheduleStoreStatusRefresh(delegatedStore);
        return;
      }

      // The notification list is an optimization; a failure there must never disable the
      // real-time channel that actually delivers new orders.
      const [storeResult, notificationsResult] = await Promise.allSettled([
        firstValueFrom(this.storeService.getMyStore().pipe(timeout(PANEL_BOOTSTRAP_TIMEOUT_MS))),
        firstValueFrom(this.notificationsApi.list().pipe(timeout(PANEL_BOOTSTRAP_TIMEOUT_MS))),
      ]);
      if (currentVersion !== this.initVersion) return;

      if (storeResult.status === 'fulfilled') {
        this.store.set(storeResult.value);
        this.scheduleStoreStatusRefresh(storeResult.value);
        if (storeResult.value.logoUrl) this.printing.setLogoUrl(storeResult.value.logoUrl);
      } else {
        this.error.set('Nao foi possivel carregar o painel do lojista.');
      }

      if (notificationsResult.status === 'fulfilled') {
        this.notifications.set(notificationsResult.value.items);
        this.unreadCount.set(notificationsResult.value.unreadCount);
      }

      // Register the listener before starting the connection so events delivered during the
      // handshake are never lost, and start the hub without blocking the shell loading state.
      this.registerSellerNotificationListener();
      this.subscribeToSellerState(currentVersion);
      void this.startRealtime(currentVersion);
    } catch {
      if (currentVersion === this.initVersion) {
        this.error.set('Nao foi possivel carregar o painel do lojista.');
      }
    } finally {
      if (currentVersion === this.initVersion) {
        this.loading.set(false);
      }
    }
  }

  private registerSellerNotificationListener(): void {
    if (this.sellerNotificationListener) return;
    this.sellerNotificationListener = (notification: SellerNotification) => {
      this.handleSellerNotification(notification);
    };
    this.signalR.onSellerEvent('ReceiveSellerNotification', this.sellerNotificationListener);
  }

  private subscribeToSellerState(version: number): void {
    if (this.sellerStateUnsubscribe) return;
    this.sellerStateUnsubscribe = this.signalR.onSellerStateChange((connected) => {
      if (!this.initialized || version !== this.initVersion || this.isAdminDelegated()) return;
      if (connected) {
        this.clearRealtimeRetry();
        return;
      }
      this.scheduleRealtimeRetry(version);
    });
  }

  private async startRealtime(version: number): Promise<void> {
    if (!this.initialized || version !== this.initVersion || this.isAdminDelegated()) return;
    this.clearRealtimeRetry();
    try {
      await this.signalR.startSellerHub();
    } catch {
      if (!this.initialized || version !== this.initVersion || this.isAdminDelegated()) return;
      this.scheduleRealtimeRetry(version);
    }
  }

  private scheduleRealtimeRetry(version: number): void {
    if (this.realtimeRetryTimer) return;
    this.realtimeRetryTimer = setTimeout(() => {
      this.realtimeRetryTimer = null;
      if (!this.initialized || version !== this.initVersion || this.isAdminDelegated()) return;
      if (this.signalR.isSellerConnected()) return;
      void this.startRealtime(version);
    }, 10_000);
  }

  private clearRealtimeRetry(): void {
    if (this.realtimeRetryTimer) {
      clearTimeout(this.realtimeRetryTimer);
      this.realtimeRetryTimer = null;
    }
  }

  mergeStore(store: StoreResponse): void {
    this.store.set(store);
    this.scheduleStoreStatusRefresh(store);
    if (store.logoUrl && !this.isAdminDelegated()) this.printing.setLogoUrl(store.logoUrl);
  }

  async enableSound(): Promise<void> {
    await this.sound.enable();
  }

  disableSound(): void {
    this.sound.disable();
  }

  notifyOrderChanged(orderId: string): void {
    this.orderActivityPulse.set({
      id: `manual-${orderId}-${Date.now()}`,
      orderId,
      source: 'manual-status-change',
    });
  }

  reset(): void {
    // Unsubscribe before stopping the hub so the teardown notification cannot schedule a
    // retry against the session that is being discarded.
    if (this.sellerStateUnsubscribe) {
      this.sellerStateUnsubscribe();
      this.sellerStateUnsubscribe = undefined;
    }
    this.clearRealtimeRetry();
    if (this.sellerNotificationListener) {
      this.signalR.removeSellerListener('ReceiveSellerNotification', this.sellerNotificationListener);
      this.sellerNotificationListener = undefined;
    }
    this.signalR.stopSellerHub();
    this.clearStatusRefreshTimer();
    this.initVersion++;
    this.initialized = false;
    this.store.set(null);
    this.notifications.set([]);
    this.unreadCount.set(0);
    this.ordersCount.set(0);
    this.loading.set(false);
    this.error.set(null);
    this.newOrderPulse.set(null);
    this.orderActivityPulse.set(null);
  }

  private scheduleStoreStatusRefresh(store: StoreResponse): void {
    this.clearStatusRefreshTimer();
    if (!store.nextStatusChangeAt) return;

    const changeAt = new Date(store.nextStatusChangeAt).getTime();
    if (Number.isNaN(changeAt)) return;

    const delay = Math.min(Math.max(changeAt - Date.now() + 1000, 1000), 30_000);
    this.statusRefreshTimer = setTimeout(() => this.refreshStoreStatus(), delay);
  }

  private refreshStoreStatus(): void {
    this.storeService.getMyStore().subscribe({
      next: (store) => {
        this.store.set(store);
        this.scheduleStoreStatusRefresh(store);
      },
      error: () => {
        this.statusRefreshTimer = setTimeout(() => this.refreshStoreStatus(), 30_000);
      },
    });
  }

  private clearStatusRefreshTimer(): void {
    if (this.statusRefreshTimer) {
      clearTimeout(this.statusRefreshTimer);
      this.statusRefreshTimer = null;
    }
  }

  private handleSellerNotification(notification: SellerNotification): void {
    const existing = this.notifications().find((item) => item.id === notification.id);
    this.notifications.update((items) => [notification, ...items.filter((item) => item.id !== notification.id)]);
    if (!notification.isRead && !existing) this.unreadCount.update((count) => count + 1);

    if (notification.type === NotificationType.NewOrder) {
      this.newOrderPulse.set(notification);
      this.orderActivityPulse.set({
        id: `new-${notification.id}`,
        orderId: notification.orderId ?? undefined,
        source: 'new-order',
      });
      void this.sound.playNewOrder();
    }
  }
}
