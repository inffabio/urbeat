import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SellerNotificationService } from '../../core/services/seller-notification.service';
import { SignalRService } from '../../core/services/signalr.service';
import { StoreService } from '../../core/services/store.service';
import { NotificationType, SellerNotification } from '../../shared/models/seller-notification.model';
import { StoreResponse } from '../../shared/models/store.model';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { OrderSoundAlertService } from './order-sound-alert.service';

export interface OrderActivityPulse {
  id: string;
  orderId?: string;
  source: 'new-order' | 'manual-status-change';
}

@Injectable({ providedIn: 'root' })
export class SellerShellFacade {
  private readonly storeService = inject(StoreService);
  private readonly signalR = inject(SignalRService);
  private readonly notificationsApi = inject(SellerNotificationService);
    private readonly sound = inject(OrderSoundAlertService);
    private readonly printing = inject(SellerPrintingService);

  readonly store = signal<StoreResponse | null>(null);
  readonly notifications = signal<SellerNotification[]>([]);
  readonly unreadCount = signal(0);
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

  async init(): Promise<void> {
    if (this.initialized) return;
    this.initialized = true;
    const currentVersion = ++this.initVersion;
    this.loading.set(true);
    this.error.set(null);

    try {
      const [store, notifications] = await Promise.all([
        firstValueFrom(this.storeService.getMyStore()),
        firstValueFrom(this.notificationsApi.list()),
      ]);
      if (currentVersion !== this.initVersion) return;
      this.store.set(store);
      if (store.logoUrl) this.printing.setLogoUrl(store.logoUrl);
      this.notifications.set(notifications.items);
      this.unreadCount.set(notifications.unreadCount);
      await this.signalR.startSellerHub();
      if (currentVersion !== this.initVersion) return;
      this.signalR.onSellerEvent('ReceiveSellerNotification', (notification: SellerNotification) => {
        this.handleSellerNotification(notification);
      });
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
    this.signalR.stopSellerHub();
    this.initVersion++;
    this.initialized = false;
    this.store.set(null);
    this.notifications.set([]);
    this.unreadCount.set(0);
    this.loading.set(false);
    this.error.set(null);
    this.newOrderPulse.set(null);
    this.orderActivityPulse.set(null);
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
      if (notification.orderId) void this.printing.autoPrintOrder(notification.orderId);
    }
  }
}
