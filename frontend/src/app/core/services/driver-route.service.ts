import { Injectable, inject } from '@angular/core';
import { Observable, map, of } from 'rxjs';
import { DriverRoutingProvider } from './driver-routing.provider';
import { GeoPoint, haversineDistanceKm } from '../utils/geo.helper';

export type DriverRouteSource = 'current-location' | 'store';

/**
 * `road` means the routing provider returned a street geometry. `straight-line` means the
 * provider was unavailable and the UI is showing a reference line; it must never be
 * presented as a road route.
 */
export type DriverRouteKind = 'road' | 'straight-line';

export interface DriverRoutePlan {
  /** Where the drawn route starts: the driver's current position when available, else the store. */
  readonly source: DriverRouteSource;
  readonly origin: GeoPoint;
  readonly destination: GeoPoint;
  readonly path: readonly GeoPoint[];
  readonly distanceKm: number;
  readonly kind: DriverRouteKind;
}

/**
 * Builds the driver route plan from the persisted store/customer coordinates.
 *
 * The actual road geometry comes from `DriverRoutingProvider` (configurable HTTP endpoint).
 * When the provider is unavailable the plan degrades to a two-point reference line with
 * `kind: 'straight-line'`, which the component labels as "Distância aproximada em linha
 * reta". Only coordinates are sent, never the PIN or personal/order data.
 */
@Injectable({ providedIn: 'root' })
export class DriverRouteService {
  private readonly routing = inject(DriverRoutingProvider);

  plan(
    store: GeoPoint | null,
    destination: GeoPoint | null,
    currentLocation: GeoPoint | null = null,
  ): Observable<DriverRoutePlan | null> {
    const reference = this.referencePlan(store, destination, currentLocation);
    if (!reference) return of(null);

    return this.routing.fetchRoute(reference.origin, reference.destination).pipe(
      map((geometry) =>
        geometry
          ? {
              ...reference,
              path: geometry.path,
              distanceKm: geometry.distanceKm,
              kind: 'road' as const,
            }
          : reference,
      ),
    );
  }

  /**
   * Straight reference plan used both as the provider fallback and when a caller needs a
   * synchronous fallback (for example an unexpected error in the stream). `kind` is always
   * `straight-line`, so the UI never mistakes it for a road route.
   */
  referencePlan(
    store: GeoPoint | null,
    destination: GeoPoint | null,
    currentLocation: GeoPoint | null = null,
  ): DriverRoutePlan | null {
    if (!destination) return null;

    const origin = currentLocation ?? store;
    if (!origin) return null;

    return {
      source: currentLocation ? 'current-location' : 'store',
      origin,
      destination,
      path: [origin, destination],
      distanceKm: haversineDistanceKm(origin, destination),
      kind: 'straight-line',
    };
  }
}
