import { Injectable, inject } from '@angular/core';
import { Observable, map, of } from 'rxjs';
import { DriverRoutingProvider } from './driver-routing.provider';
import { GeoPoint, haversineDistanceKm, toGeoPoint } from '../utils/geo.helper';

/**
 * `first-stop` is only used for multi-stop routes when neither the driver location nor the
 * store coordinates are known: the route then starts at the first pending customer.
 */
export type DriverRouteSource = 'current-location' | 'store' | 'first-stop';

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

/** One still-pending delivery that must be part of the drawn multi-stop route. */
export interface DriverRouteStopPoint {
  readonly sequence: number;
  readonly point: GeoPoint;
  readonly isNext: boolean;
}

/**
 * Plan for the whole remaining route (store/driver -> stop 1 -> stop 2 -> ...).
 *
 * `path` is a single continuous geometry covering every pending stop in sequence. Delivered
 * stops are deliberately not part of it; the caller may still draw them as historical markers.
 */
export interface DriverRouteStopsPlan {
  readonly source: DriverRouteSource;
  readonly origin: GeoPoint;
  readonly path: readonly GeoPoint[];
  readonly distanceKm: number;
  readonly kind: DriverRouteKind;
  readonly stops: readonly DriverRouteStopPoint[];
}

/**
 * Builds route plans from the persisted store/customer coordinates.
 *
 * `plan` covers a single destination (legacy driver portal). `stopsPlan` covers the whole
 * remaining delivery sequence in one multi-waypoint request. The actual road geometry comes
 * from `DriverRoutingProvider` (configurable HTTP endpoint). When the provider is unavailable
 * the plan degrades to a straight reference line with `kind: 'straight-line'`, which the
 * component labels as approximate. Only coordinates are sent, never the PIN or personal data.
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
   * Builds the whole remaining route in a single routing request.
   *
   * Waypoints are `origin` + every pending stop in `sequence` order, so the returned geometry is
   * one continuous street path visiting all customers, not one segment per stop. Delivered stops
   * must be filtered out by the caller; they only remain as map markers.
   */
  stopsPlan(
    store: GeoPoint | null,
    stops: readonly DriverRouteStopPoint[],
    currentLocation: GeoPoint | null = null,
  ): Observable<DriverRouteStopsPlan | null> {
    const reference = this.referenceStopsPlan(store, stops, currentLocation);
    if (!reference) return of(null);

    return this.routing.fetchRouteThrough(reference.path).pipe(
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
   * Straight reference for a multi-stop route: origin followed by the pending stops, with the
   * summed haversine distance. `kind` is always `straight-line` so the UI must label it as an
   * approximation and never as a road route.
   */
  referenceStopsPlan(
    store: GeoPoint | null,
    stops: readonly DriverRouteStopPoint[],
    currentLocation: GeoPoint | null = null,
  ): DriverRouteStopsPlan | null {
    const pending = [...stops]
      .filter((stop) => toGeoPoint(stop.point.lat, stop.point.lng) !== null)
      .sort((left, right) => left.sequence - right.sequence);

    const origin = currentLocation ?? store ?? pending[0]?.point ?? null;
    if (!origin) return null;

    const points = dedupeConsecutive([origin, ...pending.map((stop) => stop.point)]);
    if (points.length < 2) return null;

    const source: DriverRouteSource = currentLocation
      ? 'current-location'
      : store
        ? 'store'
        : 'first-stop';

    let distanceKm = 0;
    for (let index = 1; index < points.length; index += 1) {
      distanceKm += haversineDistanceKm(points[index - 1], points[index]);
    }

    return {
      source,
      origin,
      path: points,
      distanceKm,
      kind: 'straight-line',
      stops: pending,
    };
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

/** Collapses repeated points so OSRM never receives a zero-length leg. */
function dedupeConsecutive(points: readonly GeoPoint[]): GeoPoint[] {
  const result: GeoPoint[] = [];
  for (const point of points) {
    const previous = result[result.length - 1];
    if (previous && previous.lat === point.lat && previous.lng === point.lng) continue;
    result.push(point);
  }
  return result;
}
