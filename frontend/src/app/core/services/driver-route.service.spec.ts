import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { GeoPoint, haversineDistanceKm } from '../utils/geo.helper';
import {
  DriverRoutePlan,
  DriverRouteService,
  DriverRouteStopsPlan,
} from './driver-route.service';

const osrmResponse = (
  coordinates: ReadonlyArray<readonly [number, number]>,
  distanceMeters: number,
  code = 'Ok',
) => ({
  code,
  routes: [
    {
      distance: distanceMeters,
      geometry: { type: 'LineString', coordinates },
    },
  ],
});

describe('DriverRouteService', () => {
  const store = { lat: -23.55052, lng: -46.633308 };
  const customer = { lat: -23.561414, lng: -46.655881 };
  const mid = { lat: -23.556, lng: -46.64 };
  const current = { lat: -23.555, lng: -46.64 };

  let service: DriverRouteService;
  let httpMock: HttpTestingController;

  const plan = (
    storePoint: GeoPoint | null,
    destination: GeoPoint | null,
    currentLocation: GeoPoint | null = null,
  ): DriverRoutePlan | null | undefined => {
    let result: DriverRoutePlan | null | undefined;
    service.plan(storePoint, destination, currentLocation).subscribe((value) => (result = value));
    return result;
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DriverRouteService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('returns the road geometry when the configured provider succeeds', () => {
    let result: DriverRoutePlan | null | undefined;
    service.plan(store, customer).subscribe((value) => (result = value));

    const request = httpMock.expectOne(
      (req) => req.url.startsWith(environment.routingUrl),
    );
    expect(request.request.method).toBe('GET');
    expect(request.request.url).toBe(
      `${environment.routingUrl}/${store.lng},${store.lat};${customer.lng},${customer.lat}`,
    );
    expect(request.request.params.get('overview')).toBe('full');
    expect(request.request.params.get('geometries')).toBe('geojson');
    expect(request.request.params.get('steps')).toBe('false');
    expect(request.request.params.get('alternatives')).toBe('false');
    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush(osrmResponse([
      [store.lng, store.lat],
      [mid.lng, mid.lat],
      [customer.lng, customer.lat],
    ], 3_250));

    expect(result).not.toBeUndefined();
    expect(result!.kind).toBe('road');
    expect(result!.source).toBe('store');
    expect(result!.origin).toEqual(store);
    expect(result!.destination).toEqual(customer);
    expect(result!.path).toEqual([store, mid, customer]);
    expect(result!.distanceKm).toBeCloseTo(3.25, 5);
  });

  it('sends only the two coordinate pairs in the request', () => {
    service.plan(store, customer).subscribe();

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    expect(request.request.urlWithParams).not.toContain('pin');
    expect(request.request.urlWithParams).not.toContain('Cliente');
    expect(request.request.urlWithParams).not.toContain('ABC12345');
    expect(request.request.body).toBeNull();
    request.flush(osrmResponse([[store.lng, store.lat], [customer.lng, customer.lat]], 1_000));
  });

  it('falls back to the straight reference line when the provider fails', () => {
    let result: DriverRoutePlan | null | undefined;
    service.plan(store, customer).subscribe((value) => (result = value));

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    request.flush('routing unavailable', { status: 503, statusText: 'Service Unavailable' });

    expect(result).not.toBeUndefined();
    expect(result!.kind).toBe('straight-line');
    expect(result!.path).toEqual([store, customer]);
    expect(result!.distanceKm).toBeCloseTo(haversineDistanceKm(store, customer), 6);
  });

  it('falls back when the provider answers without a usable geometry', () => {
    let result: DriverRoutePlan | null | undefined;
    service.plan(store, customer).subscribe((value) => (result = value));

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    request.flush(osrmResponse([[store.lng, store.lat]], 2_000));

    expect(result).not.toBeUndefined();
    expect(result!.kind).toBe('straight-line');
    expect(result!.path).toEqual([store, customer]);
  });

  it('falls back when the provider returns an error code', () => {
    let result: DriverRoutePlan | null | undefined;
    service.plan(store, customer).subscribe((value) => (result = value));

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    request.flush({ code: 'NoRoute', routes: [] });

    expect(result).not.toBeUndefined();
    expect(result!.kind).toBe('straight-line');
  });

  it('prefers the current location as origin and asks for a route from it', () => {
    let result: DriverRoutePlan | null | undefined;
    service.plan(store, customer, current).subscribe((value) => (result = value));

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    expect(request.request.url).toBe(
      `${environment.routingUrl}/${current.lng},${current.lat};${customer.lng},${customer.lat}`,
    );
    request.flush(osrmResponse([[current.lng, current.lat], [customer.lng, customer.lat]], 2_100));

    expect(result).not.toBeUndefined();
    expect(result!.kind).toBe('road');
    expect(result!.source).toBe('current-location');
    expect(result!.origin).toEqual(current);
    expect(result!.path).toEqual([current, customer]);
  });

  it('builds a route from the current location even when the store has no coordinates', () => {
    let result: DriverRoutePlan | null | undefined;
    service.plan(null, customer, current).subscribe((value) => (result = value));

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    request.flush(osrmResponse([[current.lng, current.lat], [customer.lng, customer.lat]], 2_100));

    expect(result!.source).toBe('current-location');
    expect(result!.kind).toBe('road');
  });

  it('returns null without calling the provider when the destination or every origin is missing', () => {
    const missingDestination = plan(store, null);
    const missingOrigin = plan(null, customer);
    const everythingMissing = plan(null, null, null);

    expect(missingDestination).toBeNull();
    expect(missingOrigin).toBeNull();
    expect(everythingMissing).toBeNull();
    httpMock.expectNone((req) => req.url.startsWith(environment.routingUrl));
  });

  it('requests the whole remaining route in a single ordered multi-waypoint call', () => {
    const stops = [
      { sequence: 3, point: { lat: -23.57, lng: -46.64 }, isNext: false },
      { sequence: 1, point: { lat: -23.561414, lng: -46.655881 }, isNext: true },
      { sequence: 2, point: { lat: -23.566, lng: -46.645 }, isNext: false },
    ];

    let result: DriverRouteStopsPlan | null | undefined;
    service.stopsPlan(store, stops).subscribe((value) => (result = value));

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    expect(request.request.url).toBe(
      `${environment.routingUrl}/${store.lng},${store.lat}`
      + `;${stops[1].point.lng},${stops[1].point.lat}`
      + `;${stops[2].point.lng},${stops[2].point.lat}`
      + `;${stops[0].point.lng},${stops[0].point.lat}`,
    );
    expect(request.request.urlWithParams).not.toContain('pin');
    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush(osrmResponse([
      [store.lng, store.lat],
      [stops[1].point.lng, stops[1].point.lat],
      [stops[2].point.lng, stops[2].point.lat],
      [stops[0].point.lng, stops[0].point.lat],
    ], 8_700));

    expect(result).not.toBeUndefined();
    expect(result!.kind).toBe('road');
    expect(result!.source).toBe('store');
    expect(result!.stops.map((stop) => stop.sequence)).toEqual([1, 2, 3]);
    expect(result!.path).toEqual([
      store,
      stops[1].point,
      stops[2].point,
      stops[0].point,
    ]);
    expect(result!.distanceKm).toBeCloseTo(8.7, 5);
  });

  it('starts the multi-stop route from the current location and falls back to the first stop', () => {
    const stops = [
      { sequence: 1, point: customer, isNext: true },
      { sequence: 2, point: mid, isNext: false },
    ];

    let fromCurrent: DriverRouteStopsPlan | null | undefined;
    service.stopsPlan(store, stops, current).subscribe((value) => (fromCurrent = value));
    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    expect(request.request.url).toBe(
      `${environment.routingUrl}/${current.lng},${current.lat};${customer.lng},${customer.lat};${mid.lng},${mid.lat}`,
    );
    request.flush(osrmResponse([
      [current.lng, current.lat],
      [customer.lng, customer.lat],
      [mid.lng, mid.lat],
    ], 4_000));
    expect(fromCurrent!.source).toBe('current-location');

    const withoutOrigin = service.referenceStopsPlan(null, stops, null);
    expect(withoutOrigin).not.toBeNull();
    expect(withoutOrigin!.source).toBe('first-stop');
    expect(withoutOrigin!.path).toEqual([customer, mid]);
    expect(withoutOrigin!.kind).toBe('straight-line');
    expect(withoutOrigin!.distanceKm).toBeCloseTo(haversineDistanceKm(customer, mid), 6);
  });

  it('builds a reference plan only when two distinct points exist', () => {
    const plan = service.referenceStopsPlan(store, [
      { sequence: 1, point: customer, isNext: true },
    ]);

    expect(plan).not.toBeNull();
    expect(plan!.path).toEqual([store, customer]);
    expect(service.referenceStopsPlan(null, [])).toBeNull();
    expect(service.referenceStopsPlan(null, [
      { sequence: 1, point: customer, isNext: true },
    ])).toBeNull();
  });

  it('exposes a synchronous straight-line reference used by unexpected error paths', () => {
    const reference = service.referencePlan(store, customer);

    expect(reference).not.toBeNull();
    expect(reference!.kind).toBe('straight-line');
    expect(reference!.path).toEqual([store, customer]);
    expect(reference!.distanceKm).toBeCloseTo(haversineDistanceKm(store, customer), 6);
    httpMock.expectNone((req) => req.url.startsWith(environment.routingUrl));
  });
});
