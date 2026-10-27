import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  HostListener,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { forkJoin } from 'rxjs';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { DeliveryRouteService } from '../../core/services/delivery-route.service';
import { OrderService } from '../../core/services/order.service';
import { ToastService } from '../../core/services/toast.service';
import { formatSaoPauloDateTime } from '../../core/utils/sao-paulo-date.helper';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { SellerDeliveryDriver } from '../../shared/models/delivery-driver.model';
import {
  DeliveryRoute,
  DeliveryRouteStatus,
} from '../../shared/models/delivery-route.model';
import { OrderSummary } from '../../shared/models/order.model';

@Component({
  selector: 'app-seller-delivery-routes-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-delivery-routes-page.component.html',
  styleUrl: './seller-delivery-routes-page.component.scss',
})
export class SellerDeliveryRoutesPageComponent implements OnInit {
  private readonly routesApi = inject(DeliveryRouteService);
  private readonly driversApi = inject(DeliveryDriverService);
  private readonly ordersApi = inject(OrderService);
  private readonly toast = inject(ToastService);

  @ViewChild('routeDialog') private routeDialogRef?: ElementRef<HTMLElement>;
  @ViewChild('revokeDialog') private revokeDialogRef?: ElementRef<HTMLElement>;

  private dialogTrigger: HTMLElement | null = null;
  private revokeTrigger: HTMLElement | null = null;

  readonly DeliveryRouteStatus = DeliveryRouteStatus;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly submitting = signal(false);
  readonly dialogOpen = signal(false);
  readonly editingRoute = signal<DeliveryRoute | null>(null);
  readonly selectedDriverId = signal('');
  readonly selectedOrderIds = signal<string[]>([]);
  readonly routes = signal<DeliveryRoute[]>([]);
  readonly drivers = signal<SellerDeliveryDriver[]>([]);
  readonly orders = signal<OrderSummary[]>([]);
  readonly actionRouteId = signal<string | null>(null);
  readonly revokeTarget = signal<DeliveryRoute | null>(null);

  readonly activeDrivers = computed(() => this.drivers().filter((driver) => driver.isActive));
  readonly activeRoutes = computed(() =>
    this.routes().filter((route) => route.status === DeliveryRouteStatus.Active),
  );
  readonly pendingStops = computed(() =>
    this.activeRoutes().reduce((total, route) => total + (route.totalStops - route.deliveredStops), 0),
  );
  readonly selectedDriver = computed(() => {
    const driverId = this.selectedDriverId();
    return this.drivers().find((driver) => driver.id === driverId) ?? null;
  });
  readonly eligibleOrders = computed(() => {
    const driver = this.selectedDriver();
    if (!driver) return [];
    return this.orders().filter((order) =>
      order.status === OrderStatus.OnDelivery
      && order.deliveryDriverId === driver.deliveryDriverId);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    forkJoin({
      routes: this.routesApi.listStoreRoutes(),
      drivers: this.driversApi.listStoreDrivers(),
      orders: this.ordersApi.getStoreOrders({ pageSize: 100, status: OrderStatus.OnDelivery }),
    }).subscribe({
      next: ({ routes, drivers, orders }) => {
        this.routes.set(routes);
        this.drivers.set(drivers);
        this.orders.set(orders.items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  openCreateDialog(event?: Event): void {
    this.dialogTrigger = this.resolveTrigger(event);
    this.editingRoute.set(null);
    this.selectedDriverId.set('');
    this.selectedOrderIds.set([]);
    this.dialogOpen.set(true);
    this.focusDialog('route');
  }

  openEditDialog(route: DeliveryRoute, event?: Event): void {
    this.dialogTrigger = this.resolveTrigger(event);
    this.editingRoute.set(route);
    this.selectedDriverId.set(route.deliveryDriverId);
    this.selectedOrderIds.set(route.orders.map((order) => order.orderId));
    this.dialogOpen.set(true);
    this.focusDialog('route');
  }

  closeDialog(): void {
    const trigger = this.dialogTrigger;
    this.dialogTrigger = null;
    this.dialogOpen.set(false);
    this.editingRoute.set(null);
    this.submitting.set(false);
    this.restoreFocus(trigger);
  }

  selectDriver(driverId: string): void {
    if (driverId === this.selectedDriverId()) return;
    this.selectedDriverId.set(driverId);
    this.selectedOrderIds.set([]);
  }

  isOrderSelected(orderId: string): boolean {
    return this.selectedOrderIds().includes(orderId);
  }

  toggleOrder(orderId: string): void {
    this.selectedOrderIds.update((ids) =>
      ids.includes(orderId) ? ids.filter((id) => id !== orderId) : [...ids, orderId],
    );
  }

  orderLabel(order: OrderSummary): string {
    return `#${order.code}${order.customerName ? ` · ${order.customerName}` : ''}`;
  }

  submitRoute(): void {
    const driverId = this.selectedDriverId();
    const orderIds = this.selectedOrderIds();
    if (!driverId || orderIds.length === 0 || this.submitting()) {
      void this.toast.showError('Selecione o motoboy e ao menos um pedido em entrega.');
      return;
    }

    const editing = this.editingRoute();
    this.submitting.set(true);
    const request = { deliveryDriverId: driverId, orderIds };
    const operation = editing
      ? this.routesApi.updateRoute(editing.id, request)
      : this.routesApi.createRoute(request);

    operation.subscribe({
      next: (result) => {
        this.submitting.set(false);
        if (!result.route) {
          void this.toast.showError(result.error ?? 'Não foi possível salvar a rota.');
          return;
        }

        this.routes.update((items) => [
          result.route!,
          ...items.filter((item) => item.id !== result.route!.id),
        ]);
        this.closeDialog();
        void this.toast.showSuccess(editing ? 'Rota atualizada.' : 'Rota criada.');
      },
      error: (err) => {
        this.submitting.set(false);
        void this.toast.showError(err?.error?.error ?? 'Não foi possível salvar a rota.');
      },
    });
  }

  shareRoute(route: DeliveryRoute): void {
    if (this.actionRouteId()) return;
    this.actionRouteId.set(route.id);

    this.routesApi.shareRoute(route.id).subscribe({
      next: (result) => {
        this.actionRouteId.set(null);
        if (!result.shareUrl) {
          void this.toast.showError('Não foi possível gerar o link da rota.');
          return;
        }

        const phone = (route.driverWhatsAppPhoneNumber ?? '').replace(/\D/g, '');
        if (!phone) {
          void this.toast.showError('Cadastre o WhatsApp do motoboy antes de compartilhar.');
          return;
        }

        const withCountry = phone.startsWith('55') ? phone : `55${phone}`;
        const message = encodeURIComponent(`Rota de entregas: ${result.shareUrl}`);
        window.open(`https://wa.me/${withCountry}?text=${message}`, '_blank', 'noopener,noreferrer');
        if (result.route) {
          this.routes.update((items) =>
            items.map((item) => (item.id === result.route!.id ? result.route! : item)),
          );
        }
      },
      error: (err) => {
        this.actionRouteId.set(null);
        void this.toast.showError(err?.error?.error ?? 'Não foi possível compartilhar a rota.');
      },
    });
  }

  requestRevoke(route: DeliveryRoute, event?: Event): void {
    if (this.actionRouteId()) return;
    this.revokeTrigger = this.resolveTrigger(event);
    this.revokeTarget.set(route);
    this.focusDialog('revoke');
  }

  cancelRevoke(): void {
    const trigger = this.revokeTrigger;
    this.revokeTrigger = null;
    this.revokeTarget.set(null);
    this.restoreFocus(trigger);
  }

  confirmRevoke(): void {
    const route = this.revokeTarget();
    if (!route || this.actionRouteId()) return;

    this.actionRouteId.set(route.id);
    this.routesApi.revokeRoute(route.id).subscribe({
      next: (result) => {
        this.actionRouteId.set(null);
        this.cancelRevoke();
        if (result.route) {
          this.routes.update((items) =>
            items.map((item) => (item.id === result.route!.id ? result.route! : item)),
          );
        }
        void this.toast.showSuccess('Rota revogada. O link antigo deixou de funcionar.');
      },
      error: (err) => {
        this.actionRouteId.set(null);
        this.cancelRevoke();
        void this.toast.showError(err?.error?.error ?? 'Não foi possível revogar a rota.');
      },
    });
  }

  statusLabel(route: DeliveryRoute): string {
    switch (route.status) {
      case DeliveryRouteStatus.Active: return 'Ativa';
      case DeliveryRouteStatus.Completed: return 'Concluída';
      case DeliveryRouteStatus.Revoked: return 'Revogada';
      case DeliveryRouteStatus.Expired: return 'Expirada';
      default: return '';
    }
  }

  formatDateTime(value?: string | null): string {
    return value ? formatSaoPauloDateTime(value) : '-';
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event?: Event): void {
    if (this.revokeTarget()) {
      event?.preventDefault();
      this.cancelRevoke();
      return;
    }

    if (this.dialogOpen()) {
      event?.preventDefault();
      this.closeDialog();
    }
  }

  onDialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.onEscape(event);
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

  private resolveTrigger(event?: Event): HTMLElement | null {
    const target = event?.currentTarget;
    return target instanceof HTMLElement ? target : null;
  }

  private focusDialog(kind: 'route' | 'revoke'): void {
    setTimeout(() => {
      const dialog = kind === 'route'
        ? this.routeDialogRef?.nativeElement
        : this.revokeDialogRef?.nativeElement;
      dialog?.focus();
    });
  }

  private restoreFocus(trigger: HTMLElement | null): void {
    if (!trigger) return;
    setTimeout(() => {
      if (trigger.isConnected) trigger.focus();
    });
  }
}
