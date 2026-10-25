import { HttpClient, HttpBackend, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { GeoPoint, toGeoPoint } from '../utils/geo.helper';

/** Road geometry returned by the configured routing provider. */
export interface RoutingGeometry {
  readonly path: readonly GeoPoint[];
  readonly distanceKm: number;
}

interface OsrmRoute {
  readonly distance?: number;
  readonly geometry?: {
    readonly coordinates?: readonly (readonly number[])[];
  };
}

interface OsrmResponse {
  readonly code?: string;
  readonly routes?: readonly OsrmRoute[];
}

const REQUEST_TIMEOUT_MS = 8_000;
const MIN_GEOMETRY_POINTS = 2;

/**
 * Isolated HTTP integration for road routing.
 *
 * MVP provider: public OSRM demo (`environment.routingUrl`). It is intentionally a plain
 * HTTP endpoint, not a hidden SDK: no API key is bundled in the browser and the endpoint is
 * configurable per environment. The public demo has no SLA and strict fair-use limits, so
 * production must point `routingUrl` to an owned/proxied service with an adequate quota.
 *
 * Only the two coordinate pairs travel in the URL. No PIN, order code, customer name,
 * phone or address text is sent. Failures (HTTP error, timeout, invalid/empty geometry)
 * resolve to `null` so the caller can explicitly fall back to a straight reference line.
 */
@Injectable({ providedIn: 'root' })
export class DriverRoutingProvider {
  // Dedicated HttpClient without the application interceptors: a third-party routing call must
  // never receive the session token, block the router outlet with the global loading overlay,
  // trigger the generic error toast, or try to refresh auth. Errors are handled here.
  private readonly http = new HttpClient(inject(HttpBackend));

  fetchRoute(origin: GeoPoint, destination: GeoPoint): Observable<RoutingGeometry | null> {
    const endpoint = environment.routingUrl?.trim();
    if (!endpoint) return of(null);

    const from = toGeoPoint(origin.lat, origin.lng);
    const to = toGeoPoint(destination.lat, destination.lng);
    if (!from || !to) return of(null);

    // OSRM expects `{longitude},{latitude};{longitude},{latitude}`.
    const url = `${endpoint.replace(/\/+$/, '')}/${from.lng},${from.lat};${to.lng},${to.lat}`;
    const params = new HttpParams()
      .set('overview', 'full')
      .set('geometries', 'geojson')
      .set('steps', 'false')
      .set('alternatives', 'false');

    return this.http
      .get<OsrmResponse>(url, { params })
      .pipe(
        timeout(REQUEST_TIMEOUT_MS),
        map((response) => this.parseRoadGeometry(response)),
        catchError(() => of(null)),
      );
  }

  private parseRoadGeometry(response: OsrmResponse | null): RoutingGeometry | null {
    if (!response || response.code !== 'Ok') return null;

    const route = response.routes?.[0];
    if (!route) return null;

    const distanceMeters = route.distance;
    if (typeof distanceMeters !== 'number' || !Number.isFinite(distanceMeters) || distanceMeters < 0) {
      return null;
    }

    const coordinates = route.geometry?.coordinates;
    if (!Array.isArray(coordinates) || coordinates.length < MIN_GEOMETRY_POINTS) return null;

    const path: GeoPoint[] = [];
    for (const coordinate of coordinates) {
      if (!Array.isArray(coordinate) || coordinate.length < 2) return null;

      // GeoJSON order is [longitude, latitude].
      const point = toGeoPoint(coordinate[1], coordinate[0]);
      if (!point) return null;
      path.push(point);
    }

    return { path, distanceKm: distanceMeters / 1000 };
  }
}
