import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Subscription } from 'rxjs';
import L from 'leaflet';
import {
  DriverRouteService,
  DriverRouteStopPoint,
  DriverRouteStopsPlan,
} from '../../core/services/driver-route.service';
import { GeoPoint, formatDistanceKm, toGeoPoint } from '../../core/utils/geo.helper';

/** Minimal, already-public projection of a route stop used by the map. */
export interface PublicRouteMapStop {
  readonly sequence: number;
  readonly point: GeoPoint | null;
  readonly delivered: boolean;
  readonly isNext: boolean;
  readonly customerName?: string | null;
}

export type PublicRouteMapLocationStatus = 'idle' | 'requesting' | 'granted' | 'denied' | 'unavailable';

type MarkerKind = 'store' | 'driver' | 'next' | 'pending' | 'delivered';

interface MarkerStyle {
  readonly background: string;
  readonly size: number;
  readonly shadow: string;
}

const OSM_TILE_URL = 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png';
const OSM_ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>';
const DEFAULT_PIN_SHADOW = '0 2px 8px rgba(0,0,0,.35)';

const MARKER_STYLES: Record<MarkerKind, MarkerStyle> = {
  store: { background: '#6c4634', size: 28, shadow: DEFAULT_PIN_SHADOW },
  driver: { background: '#1d4ed8', size: 28, shadow: DEFAULT_PIN_SHADOW },
  next: {
    background: '#B63A41',
    size: 34,
    shadow: `0 0 0 4px rgba(182,58,65,.32),${DEFAULT_PIN_SHADOW}`,
  },
  pending: { background: '#B63A41', size: 28, shadow: DEFAULT_PIN_SHADOW },
  delivered: { background: '#119441', size: 26, shadow: DEFAULT_PIN_SHADOW },
};

/**
 * Map of the whole remaining public route.
 *
 * Unlike the single-order driver map, this map receives every stop of the shared route, draws a
 * single continuous OSRM geometry through the pending stops in delivery sequence, and labels
 * delivered stops only as historical markers. The browser location is optional: the route starts
 * there when permission is granted, otherwise at the store, otherwise at the first pending stop.
 *
 * Only coordinates are sent to the routing provider (never the PIN, order code, customer name or
 * address text). When the provider is unavailable the map draws a dashed straight reference line
 * and every label explicitly says the route is approximate. The component renders exactly one
 * Leaflet instance per route, never one map per stop.
 */
@Component({
  selector: 'app-public-delivery-route-map',
  standalone: true,
  templateUrl: './public-delivery-route-map.component.html',
  styleUrl: './public-delivery-route-map.component.scss',
})
export class PublicDeliveryRouteMapComponent implements AfterViewInit, OnDestroy {
  private readonly routeService = inject(DriverRouteService);
  private readonly mapCanvas = viewChild<ElementRef<HTMLElement>>('mapCanvas');

  readonly stops = input.required<readonly PublicRouteMapStop[]>();
  readonly originLatitude = input<number | null | undefined>(null);
  readonly originLongitude = input<number | null | undefined>(null);
  /** External navigation for the next customer (or the first remaining stop). */
  readonly externalMapUrl = input<string>('');

  private map: L.Map | null = null;
  private routeLayer: L.Polyline | null = null;
  private readonly markers: L.Marker[] = [];
  private readonly markerPoints: GeoPoint[] = [];
  private routeSubscription: Subscription | null = null;
  private routeRequestId = 0;
  private destroyed = false;
  private appliedSignature = '';

  readonly initialized = signal(false);
  readonly tileError = signal(false);
  readonly routeLoading = signal(false);
  readonly locationStatus = signal<PublicRouteMapLocationStatus>('idle');
  readonly route = signal<DriverRouteStopsPlan | null>(null);
  readonly currentLocation = signal<GeoPoint | null>(null);

  readonly storePoint = computed(() =>
    toGeoPoint(this.originLatitude(), this.originLongitude()),
  );

  readonly locatedStops = computed(() =>
    this.stops().filter((stop) => stop.point !== null),
  );

  readonly remainingStops = computed(() =>
    this.stops().filter((stop) => !stop.delivered),
  );

  readonly missingStops = computed(() =>
    this.stops().filter((stop) => stop.point === null),
  );

  readonly deliveredStops = computed(() =>
    this.stops().filter((stop) => stop.delivered),
  );

  readonly routerStops = computed<readonly DriverRouteStopPoint[]>(() =>
    this.locatedStops()
      .filter((stop) => !stop.delivered)
      .map((stop) => ({
        sequence: stop.sequence,
        point: stop.point as GeoPoint,
        isNext: stop.isNext,
      })),
  );

  readonly hasMapData = computed(() =>
    this.storePoint() !== null || this.locatedStops().length > 0,
  );

  readonly loading = computed(() => this.hasMapData() && !this.initialized());

  readonly nextStop = computed(
    () => this.remainingStops().find((stop) => stop.isNext) ?? this.remainingStops()[0] ?? null,
  );

  readonly statusMessage = computed(() => {
    if (!this.hasMapData()) {
      return 'Este roteiro ainda não tem coordenadas de mapa. Use o endereço de cada parada e a navegação externa (Google Maps).';
    }

    if (this.loading()) return 'Carregando mapa...';

    if (this.tileError()) {
      return 'Não foi possível carregar as imagens do mapa. O endereço e a navegação externa continuam disponíveis.';
    }

    const missing = this.missingStops().length;
    const missingNote = missing > 0
      ? ` ${missing} parada(s) sem coordenadas: use o endereço e a navegação externa.`
      : '';

    if (this.routeLoading()) {
      return `Calculando a rota por ruas completa com ${this.routerStops().length} parada(s)...`;
    }

    const plan = this.route();
    if (plan) {
      const distance = formatDistanceKm(plan.distanceKm);
      if (plan.kind === 'road') {
        return `Rota por ruas com ${plan.stops.length} parada(s): ${distance}.${missingNote}`;
      }

      return `Rota aproximada em linha reta com ${plan.stops.length} parada(s): ${distance}. O roteador por ruas está indisponível agora.${missingNote}`;
    }

    if (this.remainingStops().length === 0) {
      return `Todas as entregas foram confirmadas.${missingNote}`;
    }

    if (this.routerStops().length === 0) {
      return `Nenhuma parada pendente tem coordenadas para desenhar a rota. Use o endereço e a navegação externa.${missingNote}`;
    }

    return `Só há uma parada pendente com coordenadas. Use o endereço e a navegação externa para seguir a sequência.${missingNote}`;
  });

  constructor() {
    // `stops` changes after every PIN confirmation, so markers and the route must be rebuilt.
    // The effect only reads input/internal signals and always finishes the previous request.
    effect(() => {
      const signature = this.stateSignature();
      if (!this.initialized() || signature === this.appliedSignature) return;

      this.appliedSignature = signature;
      this.applyState();
    });
  }

  ngAfterViewInit(): void {
    this.initMap();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.routeRequestId += 1;
    this.routeSubscription?.unsubscribe();
    this.routeSubscription = null;
    this.clearMarkers();
    this.routeLayer = null;
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  private initMap(): void {
    const canvas = this.hasMapData() ? this.mapCanvas()?.nativeElement : undefined;
    if (!canvas) {
      this.initialized.set(true);
      return;
    }

    const map = L.map(canvas, { zoomControl: true });
    this.map = map;

    const tiles = L.tileLayer(OSM_TILE_URL, {
      maxZoom: 19,
      attribution: OSM_ATTRIBUTION,
    });
    tiles.on('tileerror', () => {
      if (!this.destroyed) this.tileError.set(true);
    });
    tiles.addTo(map);

    this.appliedSignature = this.stateSignature();
    this.initialized.set(true);
    this.applyState();

    if (this.routerStops().length > 0) this.requestCurrentLocation();
  }

  private requestCurrentLocation(): void {
    if (typeof navigator === 'undefined' || !navigator.geolocation) {
      this.locationStatus.set('unavailable');
      return;
    }

    this.locationStatus.set('requesting');

    try {
      navigator.geolocation.getCurrentPosition(
        (position) => this.applyCurrentPosition(position),
        (error) => {
          if (this.destroyed) return;
          this.locationStatus.set(error?.code === 1 ? 'denied' : 'unavailable');
          // The store-origin route is already drawn; the status line explains the fallback.
        },
        { enableHighAccuracy: true, timeout: 10_000, maximumAge: 60_000 },
      );
    } catch {
      this.locationStatus.set('unavailable');
    }
  }

  private applyCurrentPosition(position: GeolocationPosition): void {
    if (this.destroyed) return;

    const current = toGeoPoint(position.coords.latitude, position.coords.longitude);
    if (!current) {
      this.locationStatus.set('unavailable');
      return;
    }

    this.locationStatus.set('granted');
    // The effect rebuilds the markers and recalculates the route from the driver position.
    this.currentLocation.set(current);
  }

  /** Rebuilds every marker, fits the bounds and requests a fresh whole-route geometry. */
  private applyState(): void {
    if (!this.map) return;

    this.clearMarkers();
    this.clearRoute();
    this.route.set(null);

    const store = this.storePoint();
    if (store) this.addMarker(store, 'store', 'Loja / origem');

    const current = this.currentLocation();
    if (current) this.addMarker(current, 'driver', 'Sua localização');

    for (const stop of this.locatedStops()) {
      const kind: MarkerKind = stop.delivered ? 'delivered' : stop.isNext ? 'next' : 'pending';
      const prefix = stop.delivered ? 'Parada' : stop.isNext ? 'Próxima parada' : 'Parada';
      const suffix = stop.delivered ? ' (entregue)' : '';
      const label = `${prefix} ${stop.sequence}${stop.customerName ? ` - ${stop.customerName}` : ''}${suffix}`;
      this.addMarker(stop.point as GeoPoint, kind, label, stop.sequence);
    }

    this.fitToPoints(this.markerPoints);
    this.loadRoute();
  }

  private loadRoute(): void {
    this.routeSubscription?.unsubscribe();
    this.routeSubscription = null;

    const store = this.storePoint();
    const current = this.currentLocation();
    const routerStops = this.routerStops();
    const reference = this.routeService.referenceStopsPlan(store, routerStops, current);

    if (!reference) {
      this.routeLoading.set(false);
      return;
    }

    const requestId = ++this.routeRequestId;
    this.routeLoading.set(true);

    this.routeSubscription = this.routeService.stopsPlan(store, routerStops, current).subscribe({
      next: (plan) => {
        if (this.destroyed || requestId !== this.routeRequestId) return;

        this.routeLoading.set(false);
        if (!plan) {
          this.route.set(null);
          return;
        }

        this.renderRoute(plan);
      },
      error: () => {
        if (this.destroyed || requestId !== this.routeRequestId) return;

        // The provider already degrades to a reference line; this covers unexpected stream
        // errors without leaving an empty map or a false "road route" label.
        this.routeLoading.set(false);
        if (!reference) {
          this.route.set(null);
          return;
        }

        this.renderRoute(reference);
      },
    });
  }

  private addMarker(
    point: GeoPoint,
    kind: MarkerKind,
    title: string,
    sequence?: number,
  ): void {
    if (!this.map) return;

    const marker = L.marker([point.lat, point.lng], {
      icon: this.markerIcon(kind, sequence),
      title,
      alt: title,
      zIndexOffset: kind === 'next' ? 1000 : 0,
    });
    marker.addTo(this.map);
    this.markers.push(marker);
    this.markerPoints.push(point);
  }

  private markerIcon(kind: MarkerKind, sequence?: number): L.DivIcon {
    const style = MARKER_STYLES[kind];
    const label = kind === 'store'
      ? 'L'
      : kind === 'driver'
        ? 'V'
        : String(sequence ?? '');

    return L.divIcon({
      className: `public-route-pin is-${kind}`,
      html:
        `<span style="display:flex;align-items:center;justify-content:center;width:100%;height:100%;`
        + `border-radius:50%;background:${style.background};border:3px solid #fff;color:#fff;`
        + `font-size:${style.size >= 34 ? 14 : 12}px;font-weight:800;`
        + `box-shadow:${style.shadow}">${label}</span>`,
      iconSize: [style.size, style.size],
      iconAnchor: [style.size / 2, style.size / 2],
    });
  }

  private renderRoute(plan: DriverRouteStopsPlan): void {
    if (!this.map) return;

    this.clearRoute();

    const polyline = L.polyline(
      plan.path.map((point) => [point.lat, point.lng] as [number, number]),
      {
        color: '#B63A41',
        weight: 5,
        opacity: 0.9,
        // Solid for real road geometry, dashed only for the explicit approximate fallback.
        dashArray: plan.kind === 'road' ? undefined : '9 7',
        lineCap: 'round',
      },
    );
    polyline.addTo(this.map);
    this.routeLayer = polyline;
    this.route.set(plan);
    this.fitToPoints(this.markerPoints);
  }

  private clearMarkers(): void {
    if (this.map) {
      for (const marker of this.markers) this.map.removeLayer(marker);
    }
    this.markers.length = 0;
    this.markerPoints.length = 0;
  }

  private clearRoute(): void {
    if (!this.map || !this.routeLayer) {
      this.routeLayer = null;
      return;
    }

    this.map.removeLayer(this.routeLayer);
    this.routeLayer = null;
  }

  private fitToPoints(points: readonly GeoPoint[]): void {
    if (!this.map || points.length === 0) return;

    if (points.length === 1) {
      this.map.setView([points[0].lat, points[0].lng], 15);
      return;
    }

    this.map.fitBounds(
      L.latLngBounds(points.map((point) => [point.lat, point.lng] as [number, number])),
      { padding: [28, 28], maxZoom: 16 },
    );
  }

  private stateSignature(): string {
    const stops = this.stops()
      .map((stop) => [
        stop.sequence,
        stop.delivered ? 1 : 0,
        stop.isNext ? 1 : 0,
        stop.point ? `${stop.point.lat}/${stop.point.lng}` : '-',
      ].join(':'))
      .join('|');
    const current = this.currentLocation();
    return `${this.originLatitude()}/${this.originLongitude()}#`
      + `${current ? `${current.lat}/${current.lng}` : '-'}#${stops}`;
  }
}
