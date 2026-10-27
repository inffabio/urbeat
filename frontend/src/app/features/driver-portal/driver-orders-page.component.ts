import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { ToastService } from '../../core/services/toast.service';
import { formatSaoPauloDateTime } from '../../core/utils/sao-paulo-date.helper';
import { toGeoPoint } from '../../core/utils/geo.helper';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import {
  DeliveryDriverStore,
  DriverOrder,
} from '../../shared/models/delivery-driver.model';
import { DriverRouteMapComponent } from './driver-route-map.component';

const SELECTED_STORE_KEY = 'urbeat_driver_store_id';
const REFRESH_INTERVAL_MS = 30_000;

@Component({
  selector: 'app-driver-orders-page',
  standalone: true,
  imports: [CommonModule, FormsModule, DriverRouteMapComponent],
  templateUrl: './driver-orders-page.component.html',
  styleUrl: './driver-orders-page.component.scss',
})
export class DriverOrdersPageComponent implements OnInit, OnDestroy {
  private readonly deliveryDrivers = inject(DeliveryDriverService);
  private readonly toast = inject(ToastService);
  private refreshTimer: ReturnType<typeof setInterval> | null = null;

  @ViewChild('startDialog') private startDialogRef?: ElementRef<HTMLElement>;
  @ViewChild('pinDialog') private pinDialogRef?: ElementRef<HTMLElement>;

  private startTrigger: HTMLElement | null = null;
  private pinTrigger: HTMLElement | null = null;

  readonly OrderStatus = OrderStatus;
  readonly stores = signal<DeliveryDriverStore[]>([]);
  readonly selectedStoreId = signal<string | null>(null);
  readonly orders = signal<DriverOrder[]>([]);
  readonly storesLoading = signal(true);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly storesError = signal(false);
  readonly actionOrderId = signal<string | null>(null);
  readonly startTarget = signal<DriverOrder | null>(null);
  readonly pinTarget = signal<DriverOrder | null>(null);
  readonly pinValue = signal('');
  readonly pinError = signal<string | null>(null);
  readonly pinConflict = signal(false);
  readonly pinSubmitting = signal(false);
  /** Only one Leaflet map is kept alive at a time (tiles/geolocation cost on mobile). */
  readonly activeMapOrderId = signal<string | null>(null);

  readonly selectedStore = computed(
    () => this.stores().find((store) => store.storeId === this.selectedStoreId()) ?? null,
  );
  readonly hasStores = computed(() => this.stores().length > 0);
  readonly activeCount = computed(
    () => this.orders().filter((order) => order.status === OrderStatus.OnDelivery).length,
  );

  ngOnInit(): void {
    this.loadStores();
    this.refreshTimer = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) return;
      if (this.startTarget() || this.pinTarget()) return;
      if (this.selectedStoreId()) this.loadOrders({ silent: true });
    }, REFRESH_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  loadStores(): void {
    this.storesLoading.set(true);
    this.storesError.set(false);

    this.deliveryDrivers.getDriverStores().subscribe({
      next: (stores) => {
        this.stores.set(stores);
        this.storesLoading.set(false);

        if (stores.length === 0) {
          this.selectedStoreId.set(null);
          this.activeMapOrderId.set(null);
          this.orders.set([]);
          return;
        }

        const saved = this.readSavedStoreId();
        const initial = stores.find((store) => store.storeId === saved)?.storeId ?? stores[0].storeId;
        this.selectedStoreId.set(initial);
        this.activeMapOrderId.set(null);
        this.loadOrders();
      },
      error: () => {
        this.storesLoading.set(false);
        this.storesError.set(true);
      },
    });
  }

  onStoreChange(storeId: string): void {
    if (!storeId || storeId === this.selectedStoreId()) return;
    this.selectedStoreId.set(storeId);
    this.activeMapOrderId.set(null);
    this.persistStoreId(storeId);
    this.loadOrders();
  }

  /** Opens/closes the in-card map, keeping at most one map instance mounted. */
  toggleMap(order: DriverOrder): void {
    this.activeMapOrderId.update((current) => (current === order.id ? null : order.id));
  }

  loadOrders(options?: { silent?: boolean }): void {
    const storeId = this.selectedStoreId();
    if (!storeId) return;

    if (!options?.silent) {
      this.loading.set(true);
      this.error.set(false);
    }

    this.deliveryDrivers.getDriverOrders(storeId).subscribe({
      next: (response) => {
        this.orders.set(response.orders);
        const activeMapId = this.activeMapOrderId();
        if (activeMapId && !response.orders.some((order) => order.id === activeMapId)) {
          this.activeMapOrderId.set(null);
        }
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        if (!options?.silent) this.error.set(true);
      },
    });
  }

  requestStart(order: DriverOrder, event?: Event): void {
    this.startTrigger = this.resolveTrigger(event);
    this.startTarget.set(order);
    this.focusDialog('start');
  }

  cancelStart(): void {
    this.closeStart();
  }

  confirmStart(): void {
    const order = this.startTarget();
    if (!order || this.actionOrderId()) return;

    this.actionOrderId.set(order.id);
    this.deliveryDrivers.startDelivery(order.id).subscribe({
      next: () => {
        this.actionOrderId.set(null);
        this.closeStart();
        void this.toast.showSuccess('Entrega iniciada.');
        this.loadOrders({ silent: true });
      },
      error: (err) => {
        this.actionOrderId.set(null);
        this.closeStart();
        void this.toast.showError(
          err?.error?.error ?? 'Não foi possível iniciar a entrega.',
        );
      },
    });
  }

  openPinDialog(order: DriverOrder, event?: Event): void {
    this.pinTrigger = this.resolveTrigger(event);
    this.pinTarget.set(order);
    this.pinValue.set('');
    this.pinError.set(null);
    this.pinConflict.set(false);
    this.focusDialog('pin');
  }

  cancelPin(): void {
    this.closePin();
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event?: Event): void {
    if (this.pinTarget()) {
      event?.preventDefault();
      this.closePin();
      return;
    }

    if (this.startTarget()) {
      event?.preventDefault();
      this.closeStart();
    }
  }

  /** Keeps Tab/Shift+Tab inside the open dialog, matching the seller dialogs. */
  onDialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      if (this.pinTarget()) {
        this.closePin();
      } else if (this.startTarget()) {
        this.closeStart();
      }
      return;
    }

    if (event.key !== 'Tab') return;

    const dialog = event.currentTarget as HTMLElement | null;
    if (!dialog) return;

    const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    )).filter((element) => !element.hasAttribute('disabled'));

    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;
    const index = active ? focusable.indexOf(active) : -1;

    if (event.shiftKey) {
      if (index <= 0) {
        event.preventDefault();
        last.focus();
      }
      return;
    }

    if (index === -1 || index === focusable.length - 1) {
      event.preventDefault();
      first.focus();
    }
  }

  updatePin(value: string): void {
    const digits = value.replace(/\D/g, '').slice(0, 4);
    this.pinValue.set(digits);
  }

  submitPin(): void {
    const order = this.pinTarget();
    const pin = this.pinValue();
    if (!order || this.pinSubmitting()) return;

    if (!/^\d{4}$/.test(pin)) {
      this.pinError.set('Informe os 4 dígitos do PIN recebido do cliente.');
      return;
    }

    this.pinSubmitting.set(true);
    this.pinError.set(null);
    this.pinConflict.set(false);

    this.deliveryDrivers.confirmPin(order.id, pin).subscribe({
      next: () => {
        this.pinSubmitting.set(false);
        this.closePin();
        void this.toast.showSuccess('Entrega confirmada com PIN.');
        this.loadOrders({ silent: true });
      },
      error: (err) => {
        this.pinSubmitting.set(false);
        const status = err?.status as number | undefined;
        if (status === 409) {
          // Another PIN confirmation won the optimistic concurrency check on the server. The order
          // state changed underneath this dialog, so it must be reopened before any new attempt.
          this.pinConflict.set(true);
          this.pinError.set(
            'Outra confirmação de PIN aconteceu ao mesmo tempo. Atualize o pedido e confira o status antes de tentar novamente.',
          );
          return;
        }
        if (status === 423) {
          this.pinError.set('Limite de tentativas atingido. Peça ajuda à loja.');
          return;
        }
        if (status === 400) {
          this.pinError.set(err?.error?.error ?? 'PIN incorreto. Confira com o cliente.');
          return;
        }
        this.pinError.set(err?.error?.error ?? 'Não foi possível confirmar o PIN.');
      },
    });
  }

  /** Drops the stale dialog and reloads the order after a concurrent PIN conflict. */
  refreshPinOrder(): void {
    this.closePin();
    this.loadOrders();
  }

  statusLabel(status: OrderStatus): string {
    switch (status) {
      case OrderStatus.Ready:
        return 'Pronto para retirada';
      case OrderStatus.OnDelivery:
        return 'Em rota';
      case OrderStatus.Delivered:
        return 'Entregue';
      default:
        return 'Pedido';
    }
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  formatDateTime(value?: string | null): string {
    return value ? formatSaoPauloDateTime(value) : '';
  }

  addressLine(order: DriverOrder): string {
    const street = [order.addressStreet, order.addressNumber].filter(Boolean).join(', ');
    const cityState = [order.addressCity, order.addressState].filter(Boolean).join(' - ');
    return [
      street,
      order.addressNeighborhood,
      cityState,
      order.addressCep ? `CEP ${order.addressCep}` : '',
      order.addressComplement,
      order.addressReference,
    ].filter(Boolean).join(', ');
  }

  mapUrl(order: DriverOrder): string {
    const destination = toGeoPoint(order.destinationLatitude, order.destinationLongitude);
    if (destination) {
      const coordinates = `${destination.lat},${destination.lng}`;
      return `https://www.google.com/maps/dir/?api=1&destination=${coordinates}&travelmode=driving`;
    }

    const query = [
      order.addressStreet,
      order.addressNumber,
      order.addressNeighborhood,
      order.addressCity,
      order.addressState,
      order.addressCep,
    ].filter(Boolean).join(', ');
    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(query)}`;
  }

  private closeStart(): void {
    const trigger = this.startTrigger;
    this.startTrigger = null;
    this.startTarget.set(null);
    this.restoreFocus(trigger);
  }

  private closePin(): void {
    const trigger = this.pinTrigger;
    this.pinTrigger = null;
    this.pinTarget.set(null);
    this.pinValue.set('');
    this.pinError.set(null);
    this.pinConflict.set(false);
    this.restoreFocus(trigger);
  }

  private resolveTrigger(event?: Event): HTMLElement | null {
    const target = event?.currentTarget;
    return target instanceof HTMLElement ? target : null;
  }

  private focusDialog(kind: 'start' | 'pin'): void {
    // The dialog is rendered by the signal update, so focus must wait for change detection.
    setTimeout(() => {
      const dialog = kind === 'start'
        ? this.startDialogRef?.nativeElement
        : this.pinDialogRef?.nativeElement;
      dialog?.focus();
    });
  }

  private restoreFocus(trigger: HTMLElement | null): void {
    if (!trigger) return;

    setTimeout(() => {
      if (trigger.isConnected) trigger.focus();
    });
  }

  private readSavedStoreId(): string | null {
    try {
      return localStorage.getItem(SELECTED_STORE_KEY);
    } catch {
      return null;
    }
  }

  private persistStoreId(storeId: string): void {
    try {
      localStorage.setItem(SELECTED_STORE_KEY, storeId);
    } catch {
      // Storage can be unavailable (private mode); the in-memory selection still works.
    }
  }
}
