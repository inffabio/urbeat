import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Subscription } from 'rxjs';
import L from 'leaflet';
import { DriverRoutePlan, DriverRouteService } from '../../core/services/driver-route.service';
import { GeoPoint, formatDistanceKm, toGeoPoint } from '../../core/utils/geo.helper';
import { DriverOrder } from '../../shared/models/delivery-driver.model';

export type DriverRouteMapLocationStatus = 'idle' | 'requesting' | 'granted' | 'denied' | 'unavailable';
type MarkerKind = 'store' | 'customer' | 'driver';

const OSM_TILE_URL = 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png';
const OSM_ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>';

/**
 * In-card route map for a single driver delivery.
 *
 * Provider: Leaflet (already used by the store delivery configuration) with public OSM tiles.
 * No API key/secret is required and OSM attribution is always rendered. The route geometry
 * comes from the configurable routing provider (`environment.routingUrl`); when it is
 * unavailable the map draws a straight reference line explicitly labelled as an approximate
 * straight-line distance. The external Google Maps action stays available for turn-by-turn
 * navigation. The browser location is requested only when the driver expands this map
 * (opt-in) and is never sent to the backend.
 *
 * The host page renders at most one instance at a time, which keeps tile usage predictable.
 * Every route request is cancelled on destroy or when a newer origin supersedes it, so late
 * responses cannot update a closed/cloned map.
 */
@Component({
  selector: 'app-driver-route-map',
  standalone: true,
  templateUrl: './driver-route-map.component.html',
  styleUrl: './driver-route-map.component.scss',
})
export class DriverRouteMapComponent implements AfterViewInit, OnDestroy {
  private readonly routeService = inject(DriverRouteService);
  private readonly mapCanvas = viewChild<ElementRef<HTMLElement>>('mapCanvas');

  readonly order = input.required<DriverOrder>();
  readonly externalMapUrl = input.required<string>();

  private map: L.Map | null = null;
  private routeLayer: L.Polyline | null = null;
  private readonly markers: L.Marker[] = [];
  private readonly markerPoints: GeoPoint[] = [];
  private routeSubscription: Subscription | null = null;
  private routeRequestId = 0;
  private destroyed = false;

  readonly initialized = signal(false);
  readonly tileError = signal(false);
  readonly routeLoading = signal(false);
  readonly locationStatus = signal<DriverRouteMapLocationStatus>('idle');
  readonly route = signal<DriverRoutePlan | null>(null);

  readonly storePoint = computed(() => toGeoPoint(this.order().originLatitude, this.order().originLongitude));
  readonly destinationPoint = computed(() =>
    toGeoPoint(this.order().destinationLatitude, this.order().destinationLongitude),
  );
  readonly hasMapData = computed(() => this.storePoint() !== null || this.destinationPoint() !== null);
  readonly loading = computed(() => this.hasMapData() && !this.initialized());

  readonly statusMessage = computed(() => {
    if (!this.hasMapData()) {
      return 'Este pedido ainda não tem coordenadas de mapa. Use o endereço abaixo e a navegação externa.';
    }

    if (this.loading()) {
      return 'Carregando mapa...';
    }

    if (this.tileError()) {
      return 'Não foi possível carregar as imagens do mapa. Use a navegação externa para chegar ao cliente.';
    }

    const destination = this.destinationPoint();
    const store = this.storePoint();
    const location = this.locationStatus();

    if (!destination) {
      return 'O endereço do cliente não tem coordenadas. Exibimos a loja no mapa; use a navegação externa.';
    }

    if (!store && location !== 'granted') {
      return 'A loja não tem coordenadas cadastradas. Exibimos apenas o endereço do cliente.';
    }

    if (this.routeLoading()) {
      const notice =
        location === 'denied'
          ? 'Localização não autorizada. '
          : location === 'unavailable'
            ? 'Localização indisponível neste dispositivo. '
            : '';
      return `${notice}Calculando rota por ruas...`;
    }

    const route = this.route();
    if (route) {
      return this.describeRoute(route, location);
    }

    if (location === 'requesting') {
      return 'Obtendo sua localização para calcular a rota a partir de você...';
    }

    return 'Mapa da entrega.';
  });

  ngAfterViewInit(): void {
    this.initMap();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.routeRequestId += 1;
    this.routeSubscription?.unsubscribe();
    this.routeSubscription = null;
    this.routeLayer = null;
    this.markers.length = 0;
    this.markerPoints.length = 0;
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  private initMap(): void {
    if (!this.hasMapData()) {
      this.initialized.set(true);
      return;
    }

    const canvas = this.mapCanvas()?.nativeElement;
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

    const store = this.storePoint();
    const destination = this.destinationPoint();
    if (store) this.addMarker(store, 'store');
    if (destination) this.addMarker(destination, 'customer');

    this.fitToPoints(this.currentPoints());
    this.initialized.set(true);

    if (destination) {
      this.loadRoute(null);
      this.requestCurrentLocation();
    }
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
          // Keep the store-origin route already drawn; the status line explains the fallback.
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
    this.addMarker(current, 'driver');
    this.loadRoute(current);
  }

  /**
   * Replaces the current route request. The previous subscription is unsubscribed before the
   * new one starts, so a slow response from the store origin cannot overwrite the route that
   * starts at the driver's current position (and nothing runs after destroy).
   */
  private loadRoute(currentLocation: GeoPoint | null): void {
    const destination = this.destinationPoint();
    const store = this.storePoint();

    this.routeSubscription?.unsubscribe();
    this.routeSubscription = null;

    if (!destination || (!store && !currentLocation)) {
      this.clearRoute();
      this.route.set(null);
      this.routeLoading.set(false);
      return;
    }

    const requestId = ++this.routeRequestId;
    this.routeLoading.set(true);

    this.routeSubscription = this.routeService.plan(store, destination, currentLocation).subscribe({
      next: (plan) => {
        if (this.destroyed || requestId !== this.routeRequestId) return;

        this.routeLoading.set(false);

        if (!plan) {
          this.clearRoute();
          this.route.set(null);
          return;
        }

        this.renderRoute(plan);
        this.fitToPoints(this.currentPoints());
      },
      error: () => {
        if (this.destroyed || requestId !== this.routeRequestId) return;

        // The provider already degrades to a reference line; this guard covers unexpected
        // stream errors without leaving an empty map or a false "road route" label.
        this.routeLoading.set(false);
        const fallback = this.routeService.referencePlan(store, destination, currentLocation);
        if (!fallback) {
          this.clearRoute();
          this.route.set(null);
          return;
        }

        this.renderRoute(fallback);
        this.fitToPoints(this.currentPoints());
      },
    });
  }

  private addMarker(point: GeoPoint, kind: MarkerKind): void {
    if (!this.map) return;

    const color = kind === 'store' ? '#6c4634' : kind === 'driver' ? '#1d4ed8' : '#B63A41';
    const label = kind === 'store' ? 'L' : kind === 'driver' ? 'V' : 'C';
    const title =
      kind === 'store' ? 'Loja / origem' : kind === 'driver' ? 'Sua localização' : 'Cliente / destino';

    const icon = L.divIcon({
      className: 'driver-map-pin',
      html:
        `<span style="display:flex;align-items:center;justify-content:center;width:100%;height:100%;`
        + `border-radius:50%;background:${color};border:3px solid #fff;color:#fff;font-size:12px;`
        + `font-weight:800;box-shadow:0 2px 8px rgba(0,0,0,.35)">${label}</span>`,
      iconSize: [28, 28],
      iconAnchor: [14, 14],
    });

    const marker = L.marker([point.lat, point.lng], { icon, title, alt: title });
    marker.addTo(this.map);
    this.markers.push(marker);
    this.markerPoints.push(point);
  }

  private renderRoute(plan: DriverRoutePlan): void {
    if (!this.map) return;

    this.clearRoute();

    const polyline = L.polyline(
      plan.path.map((point) => [point.lat, point.lng] as [number, number]),
      {
        color: '#B63A41',
        weight: 5,
        opacity: 0.9,
        // Solid for real road geometry, dashed for the explicit straight-line fallback.
        dashArray: plan.kind === 'road' ? undefined : '9 7',
        lineCap: 'round',
      },
    );
    polyline.addTo(this.map);
    this.routeLayer = polyline;
    this.route.set(plan);
  }

  private clearRoute(): void {
    if (!this.map || !this.routeLayer) {
      this.routeLayer = null;
      return;
    }

    this.map.removeLayer(this.routeLayer);
    this.routeLayer = null;
  }

  private describeRoute(route: DriverRoutePlan, location: DriverRouteMapLocationStatus): string {
    const distance = formatDistanceKm(route.distanceKm);
    const originLabel = route.source === 'current-location' ? 'a partir de você' : 'loja → cliente';
    const locationNotice =
      location === 'denied'
        ? 'Localização não autorizada. '
        : location === 'unavailable'
          ? 'Localização indisponível neste dispositivo. '
          : '';

    if (route.kind === 'road') {
      return `${locationNotice}Rota por ruas ${originLabel}: ${distance}.`;
    }

    return `${locationNotice}Distância aproximada em linha reta ${originLabel}: ${distance}.`;
  }

  private currentPoints(): GeoPoint[] {
    return [...this.markerPoints];
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
}
