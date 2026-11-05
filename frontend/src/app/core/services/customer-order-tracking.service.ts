import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { OrderService } from './order.service';
import { SignalRService } from './signalr.service';
import { AuthService } from './auth.service';
import { OrderDetails } from '../../shared/models/order.model';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';

export const TRACKED_ORDER_IDS_STORAGE_KEY = 'urbeat:tracked-order-ids';

export interface OrderStatusUpdateEvent {
  orderId: string;
  orderCode: string;
  status: OrderStatus;
  changedAtUtc: string;
}

const POLL_INTERVAL_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class CustomerOrderTrackingService {
  private readonly orders = inject(OrderService);
  private readonly signalR = inject(SignalRService);
  private readonly auth = inject(AuthService);

  readonly trackedOrders = signal<OrderDetails[]>([]);
  readonly unavailableOrderIds = signal<string[]>([]);
  readonly hasTrackedOrders = computed(() => this.trackedIds().length > 0);
  readonly activeOrders = computed(() =>
    this.trackedOrders().filter(
      (order) => order.status !== OrderStatus.Delivered && order.status !== OrderStatus.Cancelled,
    ),
  );
  readonly finishedOrders = computed(() =>
    this.trackedOrders().filter(
      (order) => order.status === OrderStatus.Delivered || order.status === OrderStatus.Cancelled,
    ),
  );

  private readonly trackedIds = signal<string[]>([]);
  private readonly requestSequence = new Map<string, number>();
  private readonly requestGeneration = new Map<string, number>();
  private readonly inFlight = new Set<string>();
  private readonly statusVersion = new Map<string, number>();
  private readonly eventVersion = new Map<string, number>();
  private readonly eventStatus = new Map<string, OrderStatus>();
  private readonly lastEventTimestamp = new Map<string, number>();
  private readonly recoveringOrders = new Set<string>();
  private pollingTimer: ReturnType<typeof setInterval> | null = null;
  private recoveryTimer: ReturnType<typeof setInterval> | null = null;
  private shouldPoll = false;
  private started = false;
  private lifecycleGeneration = 0;
  private hubConnected = false;
  private awaitingReconnect = false;
  private orderStatusListener?: (data: OrderStatusUpdateEvent) => void;
  private customerStateUnsubscribe?: () => void;
  private tokenSubscription?: Subscription;

  trackOrder(orderId: string): void {
    if (!orderId) return;
    if (this.trackedIds().includes(orderId)) {
      // Reopening an order that is already tracked must not reload terminal orders,
      // but a delivered order still awaiting the confirmation can change server-side
      // (for example when the motoboy confirms the PIN). Refresh only that case.
      this.refreshPendingDeliveryConfirmation(orderId);
      return;
    }

    this.setTrackedIds([...this.trackedIds(), orderId]);
    this.refresh(orderId);
    if (this.shouldPoll) this.ensurePollingTimer();
  }

  private refreshPendingDeliveryConfirmation(orderId: string): void {
    const order = this.trackedOrders().find((item) => item.id === orderId);
    if (!order || !this.awaitsDeliveryConfirmation(order)) return;
    if (this.inFlight.has(orderId)) return;
    this.refresh(orderId, { background: true });
  }

  /**
   * A delivered delivery order that has no confirmation yet is not terminal for the
   * customer: the motoboy PIN confirmation changes its details without a status change,
   * so it still deserves a retry and a refresh on reopen.
   */
  private awaitsDeliveryConfirmation(order: OrderDetails): boolean {
    return order.status === OrderStatus.Delivered
      && order.fulfillmentType === FulfillmentType.Delivery
      && !order.deliveryConfirmedAtUtc;
  }

  isOrderUnavailable(orderId: string): boolean {
    return this.unavailableOrderIds().includes(orderId);
  }

  untrackOrder(orderId: string): void {
    this.setTrackedIds(this.trackedIds().filter((id) => id !== orderId));
    this.trackedOrders.set(this.trackedOrders().filter((order) => order.id !== orderId));
    this.requestSequence.delete(orderId);
    this.requestGeneration.set(orderId, (this.requestGeneration.get(orderId) ?? 0) + 1);
    this.eventVersion.delete(orderId);
    this.eventStatus.delete(orderId);
    this.lastEventTimestamp.delete(orderId);
    this.recoveringOrders.delete(orderId);
    this.clearUnavailable(orderId);
  }

  refresh(orderId: string, options?: { background?: boolean }): void {
    if (!this.auth.token()) return;
    const sequence = (this.requestSequence.get(orderId) ?? 0) + 1;
    this.requestSequence.set(orderId, sequence);
    const generation = this.requestGeneration.get(orderId) ?? 0;
    const lifecycle = this.lifecycleGeneration;

    this.inFlight.add(orderId);
    const request$ = options?.background
      ? this.orders.getOrder(orderId, { background: true })
      : this.orders.getOrder(orderId);
    request$.subscribe({
      next: (order) => {
        this.inFlight.delete(orderId);
        if (this.lifecycleGeneration !== lifecycle) return;
        if ((this.requestGeneration.get(orderId) ?? 0) !== generation) return;
        if (this.requestSequence.get(orderId) !== sequence) return;
        this.recoveringOrders.delete(orderId);
        this.clearUnavailable(orderId);
        const responseVersion = this.orderVersion(order);
        const currentVersion = this.statusVersion.get(orderId) ?? 0;
        if (responseVersion > 0 && responseVersion < currentVersion) return;
        if (responseVersion > currentVersion) {
          this.statusVersion.set(orderId, responseVersion);
          this.eventVersion.delete(orderId);
          this.eventStatus.delete(orderId);
          this.upsertOrder(order);
          return;
        }
        const eventVersion = this.eventVersion.get(orderId) ?? 0;
        if (eventVersion > 0 && eventVersion >= responseVersion) {
          this.upsertOrder({ ...order, status: this.eventStatus.get(orderId)! });
          return;
        }
        if (responseVersion === 0 && currentVersion > 0) {
          const existing = this.trackedOrders().find((o) => o.id === orderId);
          if (existing) {
            this.upsertOrder({ ...order, status: existing.status });
            return;
          }
        }
        this.upsertOrder(order);
      },
      error: (error: unknown) => {
        this.inFlight.delete(orderId);
        if (this.lifecycleGeneration !== lifecycle) return;
        if ((this.requestGeneration.get(orderId) ?? 0) !== generation) return;
        if (this.requestSequence.get(orderId) !== sequence) return;
        if (this.isNotFoundError(error)) {
          this.recoveringOrders.delete(orderId);
          this.markUnavailable(orderId);
          return;
        }
        this.startRecoveryPolling(orderId);
      },
    });
  }

  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;

    this.registerListener();
    this.subscribeToToken();

    if (this.auth.token()) {
      await this.activate();
    }
  }

  private subscribeToToken(): void {
    if (this.tokenSubscription) return;
    this.tokenSubscription = this.auth.token$.subscribe((token) => {
      if (!this.started) return;
      if (token) {
        void this.activate();
      } else {
        this.clearForLogout();
      }
    });
  }

  private async activate(): Promise<void> {
    const generation = ++this.lifecycleGeneration;
    this.awaitingReconnect = false;

    const storedIds = this.readStoredIds();
    this.setTrackedIds(storedIds);
    storedIds.forEach((id) => this.refresh(id));

    try {
      await this.signalR.startCustomerHub();
      if (generation !== this.lifecycleGeneration) return;
      this.hubConnected = true;
      this.clearPolling();
    } catch {
      if (generation !== this.lifecycleGeneration) return;
      console.warn('CustomerOrderTrackingService: customer hub unavailable, falling back to polling.');
      this.startPolling();
    }
  }

  private clearForLogout(): void {
    this.trackedOrders.set([]);
    this.setTrackedIds([]);
    this.unavailableOrderIds.set([]);
    this.requestSequence.clear();
    this.requestGeneration.clear();
    this.inFlight.clear();
    this.statusVersion.clear();
    this.eventVersion.clear();
    this.eventStatus.clear();
    this.lastEventTimestamp.clear();
    this.recoveringOrders.clear();
    this.clearPolling();
    this.clearRecoveryTimer();
    this.hubConnected = false;
    this.awaitingReconnect = false;
    this.lifecycleGeneration++;
    this.signalR.stopCustomerHub();
  }

  stop(): void {
    this.started = false;
    this.hubConnected = false;
    this.awaitingReconnect = false;
    this.lifecycleGeneration++;
    this.clearPolling();
    this.clearRecoveryTimer();
    this.recoveringOrders.clear();
    this.statusVersion.clear();
    this.eventVersion.clear();
    this.eventStatus.clear();
    this.lastEventTimestamp.clear();

    if (this.tokenSubscription) {
      this.tokenSubscription.unsubscribe();
      this.tokenSubscription = undefined;
    }

    if (this.customerStateUnsubscribe) {
      this.customerStateUnsubscribe();
      this.customerStateUnsubscribe = undefined;
    }

    if (this.orderStatusListener) {
      this.signalR.removeCustomerListener('OrderStatusUpdated', this.orderStatusListener);
      this.orderStatusListener = undefined;
    }

    this.signalR.stopCustomerHub();
  }

  reset(): void {
    this.trackedOrders.set([]);
    this.setTrackedIds([]);
    this.unavailableOrderIds.set([]);
    this.requestSequence.clear();
    this.requestGeneration.clear();
    this.inFlight.clear();
    this.stop();
  }

  private registerListener(): void {
    if (!this.orderStatusListener) {
      this.orderStatusListener = (data: OrderStatusUpdateEvent) => {
        if (!data || !this.trackedIds().includes(data.orderId)) return;
        if (!this.applyEventStatus(data)) return;
        // The event already updated the visible status; the follow-up detail
        // refresh is automatic and must not surface a transient error toast.
        this.refresh(data.orderId, { background: true });
      };
      this.signalR.onCustomerEvent('OrderStatusUpdated', this.orderStatusListener);
    }

    if (!this.customerStateUnsubscribe) {
      this.customerStateUnsubscribe = this.signalR.onCustomerStateChange((connected) =>
        this.handleConnectionChange(connected),
      );
    }
  }

  private handleConnectionChange(connected: boolean): void {
    if (!this.started) return;
    this.hubConnected = connected;
    if (connected) {
      this.clearPolling();
      // Events are at-least-once and anything published while the socket was down
      // is lost, so a single authoritative pass per reconnect recovers the state.
      if (this.awaitingReconnect) {
        this.awaitingReconnect = false;
        this.reconcileAfterReconnect();
      }
      return;
    }

    this.awaitingReconnect = true;
    this.startPolling();
  }

  /**
   * Re-reads every order that can still change server-side. Terminal orders are
   * skipped (only a delivery confirmation can still touch a delivered order), and
   * requests already in flight are already authoritative for that order.
   */
  private reconcileAfterReconnect(): void {
    if (!this.auth.token()) return;
    this.reconcilableIds().forEach((orderId) => {
      if (this.inFlight.has(orderId)) return;
      this.refresh(orderId, { background: true });
    });
  }

  private reconcilableIds(): string[] {
    const orders = this.trackedOrders();
    return this.trackedIds().filter((id) => {
      if (this.isOrderUnavailable(id)) return false;
      const order = orders.find((item) => item.id === id);
      if (!order) return true;
      if (order.status === OrderStatus.Cancelled) return false;
      if (order.status === OrderStatus.Delivered) return this.awaitsDeliveryConfirmation(order);
      return true;
    });
  }

  private startPolling(): void {
    this.shouldPoll = true;
    this.ensurePollingTimer();
  }

  private ensurePollingTimer(): void {
    if (this.pollingTimer) return;
    if (this.activeTrackedIds().length === 0) return;
    this.pollingTimer = setInterval(() => {
      if (this.activeTrackedIds().length === 0) {
        this.clearPollingTimer();
        return;
      }
      this.activeTrackedIds().forEach((id) => this.pollRefresh(id));
    }, POLL_INTERVAL_MS);
  }

  private pollRefresh(orderId: string): void {
    if (this.inFlight.has(orderId)) return;
    this.refresh(orderId, { background: true });
  }

  private startRecoveryPolling(orderId: string): void {
    if (!this.started || this.shouldPoll) return;
    this.recoveringOrders.add(orderId);
    this.ensureRecoveryTimer();
  }

  private ensureRecoveryTimer(): void {
    if (this.recoveryTimer) return;
    this.recoveryTimer = setInterval(() => {
      if (this.recoveringOrders.size === 0) {
        this.clearRecoveryTimer();
        return;
      }
      [...this.recoveringOrders].forEach((id) => this.recoveryRefresh(id));
    }, POLL_INTERVAL_MS);
  }

  private recoveryRefresh(orderId: string): void {
    if (this.inFlight.has(orderId)) return;
    if (!this.trackedIds().includes(orderId)) {
      this.recoveringOrders.delete(orderId);
      return;
    }
    const order = this.trackedOrders().find((o) => o.id === orderId);
    if (
      order &&
      (order.status === OrderStatus.Cancelled
        || (order.status === OrderStatus.Delivered && !this.awaitsDeliveryConfirmation(order)))
    ) {
      this.recoveringOrders.delete(orderId);
      return;
    }
    this.refresh(orderId, { background: true });
  }

  private clearRecoveryTimer(): void {
    if (this.recoveryTimer) {
      clearInterval(this.recoveryTimer);
      this.recoveryTimer = null;
    }
  }

  /**
   * Applies an `OrderStatusUpdated` notification. Returns `false` when the event is a
   * duplicate or arrives out of order, in which case no HTTP request is needed.
   */
  private applyEventStatus(data: OrderStatusUpdateEvent): boolean {
    const currentStatus = this.trackedOrders().find((order) => order.id === data.orderId)?.status;
    const version = this.toTimestamp(data.changedAtUtc);
    const lastEvent = this.lastEventTimestamp.get(data.orderId) ?? 0;
    const currentVersion = this.statusVersion.get(data.orderId) ?? 0;

    // A repeated or older notification for the same change carries no new information.
    if (version > 0 && version <= lastEvent) return false;
    if (version > 0 && version <= currentVersion) return false;
    if (version > 0) this.lastEventTimestamp.set(data.orderId, version);

    // A repeated status (for example a delivery-confirmation hint) must not advance the
    // status version: it carries fresh details that the follow-up refresh has to apply,
    // and claiming a newer version would discard that response as stale.
    if (currentStatus === data.status) return true;

    const nextVersion = version > 0 ? version : currentVersion + 1;
    this.statusVersion.set(data.orderId, nextVersion);
    this.eventVersion.set(data.orderId, nextVersion);
    this.eventStatus.set(data.orderId, data.status);
    this.applyStatusOnly(data.orderId, data.status);
    return true;
  }

  private applyStatusOnly(orderId: string, status: OrderStatus): void {
    const orders = this.trackedOrders();
    const index = orders.findIndex((o) => o.id === orderId);
    if (index < 0) return;
    const next = [...orders];
    next[index] = { ...next[index], status };
    this.trackedOrders.set(next);
  }

  private orderVersion(order: OrderDetails): number {
    const history = order.history;
    if (!history || history.length === 0) return 0;
    const last = history[history.length - 1];
    return this.toTimestamp(last.createdAtUtc);
  }

  private toTimestamp(value: string | null | undefined): number {
    if (!value) return 0;
    const time = Date.parse(value);
    return Number.isNaN(time) ? 0 : time;
  }

  private activeTrackedIds(): string[] {
    const orders = this.trackedOrders();
    return this.trackedIds().filter((id) => {
      const order = orders.find((o) => o.id === id);
      return (
        !this.isOrderUnavailable(id) &&
        (!order || (order.status !== OrderStatus.Delivered && order.status !== OrderStatus.Cancelled))
      );
    });
  }

  private clearPolling(): void {
    this.shouldPoll = false;
    this.clearPollingTimer();
  }

  private clearPollingTimer(): void {
    if (this.pollingTimer) {
      clearInterval(this.pollingTimer);
      this.pollingTimer = null;
    }
  }

  private upsertOrder(order: OrderDetails): void {
    const current = this.trackedOrders();
    const index = current.findIndex((o) => o.id === order.id);
    if (index >= 0) {
      const next = [...current];
      next[index] = order;
      this.trackedOrders.set(next);
    } else {
      this.trackedOrders.set([...current, order]);
    }
  }

  private isNotFoundError(error: unknown): boolean {
    return typeof error === 'object' && error !== null && (error as { status?: number }).status === 404;
  }

  private markUnavailable(orderId: string): void {
    this.trackedOrders.set(this.trackedOrders().filter((order) => order.id !== orderId));
    const ids = this.unavailableOrderIds();
    if (!ids.includes(orderId)) {
      this.unavailableOrderIds.set([...ids, orderId]);
    }
  }

  private clearUnavailable(orderId: string): void {
    const ids = this.unavailableOrderIds();
    if (ids.includes(orderId)) {
      this.unavailableOrderIds.set(ids.filter((id) => id !== orderId));
    }
  }

  private setTrackedIds(ids: string[]): void {
    this.trackedIds.set(ids);
    try {
      sessionStorage.setItem(TRACKED_ORDER_IDS_STORAGE_KEY, JSON.stringify(ids));
    } catch {
      // Storage unavailable; tracking still works in-memory.
    }
  }

  private readStoredIds(): string[] {
    this.removeLegacyStorage();
    try {
      const raw = sessionStorage.getItem(TRACKED_ORDER_IDS_STORAGE_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return [...new Set(parsed.filter((id): id is string => typeof id === 'string' && id.length > 0))];
    } catch {
      return [];
    }
  }

  // Tracked ids are tab-scoped so two storefront tabs cannot mix their orders.
  // The legacy global localStorage entry is discarded instead of migrated to
  // avoid copying another tab's tracking list.
  private removeLegacyStorage(): void {
    try {
      localStorage.removeItem(TRACKED_ORDER_IDS_STORAGE_KEY);
    } catch {
      // Storage unavailable; nothing to clean up.
    }
  }
}
