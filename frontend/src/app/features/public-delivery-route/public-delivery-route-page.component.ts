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
import { ActivatedRoute } from '@angular/router';
import { DeliveryRouteService } from '../../core/services/delivery-route.service';
import { formatSaoPauloDateTime } from '../../core/utils/sao-paulo-date.helper';
import { toGeoPoint } from '../../core/utils/geo.helper';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import {
  DeliveryRouteStatus,
  PublicDeliveryRoute,
  PublicDeliveryRouteStop,
} from '../../shared/models/delivery-route.model';
import {
  PublicDeliveryRouteMapComponent,
  PublicRouteMapStop,
} from './public-delivery-route-map.component';

type PublicRouteError = 'invalid' | 'expired' | 'network';
const REFRESH_INTERVAL_MS = 30_000;

@Component({
  selector: 'app-public-delivery-route-page',
  standalone: true,
  imports: [CommonModule, FormsModule, PublicDeliveryRouteMapComponent],
  templateUrl: './public-delivery-route-page.component.html',
  styleUrl: './public-delivery-route-page.component.scss',
})
export class PublicDeliveryRoutePageComponent implements OnInit, OnDestroy {
  private readonly routeService = inject(DeliveryRouteService);
  private readonly route = inject(ActivatedRoute);

  @ViewChild('pinDialog') private pinDialogRef?: ElementRef<HTMLElement>;
  private pinTrigger: HTMLElement | null = null;
  private refreshTimer: ReturnType<typeof setInterval> | null = null;

  readonly token = signal('');
  readonly loading = signal(true);
  readonly error = signal<PublicRouteError | null>(null);
  readonly routeData = signal<PublicDeliveryRoute | null>(null);
  readonly pinTarget = signal<PublicDeliveryRouteStop | null>(null);
  readonly pinValue = signal('');
  readonly pinError = signal<string | null>(null);
  readonly pinSubmitting = signal(false);
  readonly pinConflict = signal(false);
  readonly successMessage = signal<string | null>(null);
  readonly DeliveryRouteStatus = DeliveryRouteStatus;

  readonly nextStop = computed(() =>
    this.routeData()?.stops.find((stop) => stop.isNext) ?? null,
  );
  readonly pendingStops = computed(() =>
    this.routeData()?.stops.filter((stop) => !stop.deliveryConfirmedAtUtc) ?? [],
  );
  /**
   * Projection handed to the single full-route map. Delivered stops stay as markers while the
   * drawn line only follows the remaining ones, in the sequence returned by the backend.
   */
  readonly mapStops = computed<readonly PublicRouteMapStop[]>(() =>
    (this.routeData()?.stops ?? []).map((stop) => ({
      sequence: stop.sequence,
      point: toGeoPoint(stop.destinationLatitude, stop.destinationLongitude),
      delivered: Boolean(stop.deliveryConfirmedAtUtc),
      isNext: stop.isNext,
      customerName: stop.customerName ?? null,
    })),
  );
  readonly mapExternalUrl = computed(() => {
    const stop = this.nextStop() ?? this.pendingStops()[0] ?? null;
    return stop ? this.mapUrl(stop) : '';
  });
  readonly progressPercent = computed(() => {
    const route = this.routeData();
    if (!route || route.totalStops === 0) return 0;
    return (route.deliveredStops / route.totalStops) * 100;
  });

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') ?? '';
    this.token.set(token);
    this.load();

    this.refreshTimer = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) return;
      if (this.pinTarget()) return;
      this.load({ silent: true });
    }, REFRESH_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  load(options?: { silent?: boolean }): void {
    if (!options?.silent) {
      this.loading.set(true);
      this.error.set(null);
    }

    this.routeService.getPublicRoute(this.token()).subscribe({
      next: (route) => {
        this.routeData.set(route);
        this.loading.set(false);
        this.error.set(null);
      },
      error: (err) => {
        this.loading.set(false);
        if (options?.silent) return;

        if (err?.status === 410) {
          this.error.set('expired');
          this.routeData.set(null);
          return;
        }
        if (err?.status === 404) {
          this.error.set('invalid');
          this.routeData.set(null);
          return;
        }
        this.error.set('network');
      },
    });
  }

  openPinDialog(stop: PublicDeliveryRouteStop, event?: Event): void {
    if (!stop.canDeliver || this.pinSubmitting()) return;
    const trigger = event?.currentTarget;
    this.pinTrigger = trigger instanceof HTMLElement ? trigger : null;
    this.pinTarget.set(stop);
    this.pinValue.set('');
    this.pinError.set(null);
    this.pinConflict.set(false);
    this.successMessage.set(null);
    setTimeout(() => this.pinDialogRef?.nativeElement.focus());
  }

  closePinDialog(): void {
    const trigger = this.pinTrigger;
    this.pinTrigger = null;
    this.pinTarget.set(null);
    this.pinValue.set('');
    this.pinError.set(null);
    this.pinConflict.set(false);
    setTimeout(() => {
      if (trigger?.isConnected) trigger.focus();
    });
  }

  updatePin(value: string): void {
    this.pinValue.set(value.replace(/\D/g, '').slice(0, 6));
    this.pinError.set(null);
  }

  submitPin(): void {
    const stop = this.pinTarget();
    const pin = this.pinValue();
    if (!stop || this.pinSubmitting()) return;

    if (!/^\d{6}$/.test(pin)) {
      this.pinError.set('Informe os 6 dígitos do PIN recebido do cliente.');
      return;
    }

    this.pinSubmitting.set(true);
    this.pinError.set(null);
    this.pinConflict.set(false);

    this.routeService.confirmPublicDelivery(this.token(), stop.sequence, pin).subscribe({
      next: (result) => {
        this.pinSubmitting.set(false);
        if (result.route) this.routeData.set(result.route);
        this.closePinDialog();
        this.successMessage.set(`Pedido #${stop.orderCode} entregue. Próximo cliente atualizado.`);
      },
      error: (err) => {
        this.pinSubmitting.set(false);
        const status = err?.status as number | undefined;

        if (status === 410) {
          this.closePinDialog();
          this.error.set('expired');
          this.routeData.set(null);
          return;
        }
        if (status === 404) {
          this.closePinDialog();
          this.error.set('invalid');
          this.routeData.set(null);
          return;
        }
        if (status === 400) {
          const remaining = err?.error?.remainingAttempts;
          this.pinError.set(
            typeof remaining === 'number'
              ? `PIN incorreto. Tentativas restantes: ${remaining}.`
              : (err?.error?.error ?? 'PIN incorreto. Confira com o cliente.'),
          );
          return;
        }
        if (status === 423) {
          this.pinError.set('Limite de tentativas atingido. Peça ajuda à loja.');
          return;
        }
        if (status === 409) {
          this.pinConflict.set(true);
          this.pinError.set(
            err?.error?.error
              ?? 'A rota ou o pedido mudou ao mesmo tempo. Atualize antes de tentar novamente.',
          );
          return;
        }
        this.pinError.set(err?.error?.error ?? 'Não foi possível confirmar a entrega.');
      },
    });
  }

  refreshAfterConflict(): void {
    this.closePinDialog();
    this.load();
  }

  mapUrl(stop: PublicDeliveryRouteStop): string {
    const destination = toGeoPoint(stop.destinationLatitude, stop.destinationLongitude);
    if (destination) {
      return `https://www.google.com/maps/dir/?api=1&destination=${destination.lat},${destination.lng}&travelmode=driving`;
    }

    const query = [
      stop.addressStreet,
      stop.addressNumber,
      stop.addressNeighborhood,
      stop.addressCity,
      stop.addressState,
      stop.addressCep,
    ].filter(Boolean).join(', ');
    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(query)}`;
  }

  addressLine(stop: PublicDeliveryRouteStop): string {
    const street = [stop.addressStreet, stop.addressNumber].filter(Boolean).join(', ');
    const cityState = [stop.addressCity, stop.addressState].filter(Boolean).join(' - ');
    return [
      street,
      stop.addressNeighborhood,
      cityState,
      stop.addressCep ? `CEP ${stop.addressCep}` : '',
      stop.addressComplement,
      stop.addressReference,
    ].filter(Boolean).join(', ');
  }

  formatDateTime(value?: string | null): string {
    return value ? formatSaoPauloDateTime(value) : '';
  }

  statusLabel(status: OrderStatus): string {
    switch (status) {
      case OrderStatus.OnDelivery:
        return 'Em rota';
      case OrderStatus.Delivered:
        return 'Entregue';
      case OrderStatus.Ready:
        return 'Aguardando saída';
      default:
        return 'Pedido';
    }
  }

  routeStatusLabel(status: DeliveryRouteStatus): string {
    switch (status) {
      case DeliveryRouteStatus.Active: return 'Rota ativa';
      case DeliveryRouteStatus.Completed: return 'Rota concluída';
      case DeliveryRouteStatus.Revoked: return 'Rota revogada';
      case DeliveryRouteStatus.Expired: return 'Rota expirada';
      default: return '';
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event?: Event): void {
    if (!this.pinTarget()) return;
    event?.preventDefault();
    this.closePinDialog();
  }

  onDialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closePinDialog();
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
}
