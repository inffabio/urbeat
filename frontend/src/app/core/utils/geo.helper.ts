/** Geographic helpers shared by the driver route map. Pure functions, no DOM/Leaflet access. */

export interface GeoPoint {
  readonly lat: number;
  readonly lng: number;
}

const EARTH_RADIUS_KM = 6371;

/** Rejects nullish, non-finite and out-of-range values before they reach Leaflet / the DOM. */
export function toGeoPoint(
  latitude: number | null | undefined,
  longitude: number | null | undefined,
): GeoPoint | null {
  if (typeof latitude !== 'number' || typeof longitude !== 'number') return null;
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;
  if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180) return null;
  return { lat: latitude, lng: longitude };
}

/** Great-circle distance in kilometres (Haversine). */
export function haversineDistanceKm(from: GeoPoint, to: GeoPoint): number {
  const latDelta = (to.lat - from.lat) * (Math.PI / 180);
  const lngDelta = (to.lng - from.lng) * (Math.PI / 180);
  const fromLat = from.lat * (Math.PI / 180);
  const toLat = to.lat * (Math.PI / 180);

  const a =
    Math.sin(latDelta / 2) ** 2
    + Math.cos(fromLat) * Math.cos(toLat) * Math.sin(lngDelta / 2) ** 2;

  return 2 * EARTH_RADIUS_KM * Math.asin(Math.min(1, Math.sqrt(a)));
}

/** Human readable distance for the map status line, always in pt-BR units. */
export function formatDistanceKm(distanceKm: number): string {
  if (!Number.isFinite(distanceKm) || distanceKm < 0) return '';
  if (distanceKm < 1) {
    return `${Math.max(1, Math.round(distanceKm * 1000))} m`;
  }

  return `${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 }).format(distanceKm)} km`;
}
