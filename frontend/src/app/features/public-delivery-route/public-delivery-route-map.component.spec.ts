import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import L from 'leaflet';
import { environment } from '../../../environments/environment';
import { GeoPoint } from '../../core/utils/geo.helper';
import {
  PublicDeliveryRouteMapComponent,
  PublicRouteMapStop,
} from './public-delivery-route-map.component';

const store: GeoPoint = { lat: -23.55052, lng: -46.633308 };
const point1: GeoPoint = { lat: -23.561414, lng: -46.655881 };
const point2: GeoPoint = { lat: -23.570001, lng: -46.640002 };
const point3: GeoPoint = { lat: -23.580003, lng: -46.650004 };
const point4: GeoPoint = { lat: -23.590005, lng: -46.660006 };
const point5: GeoPoint = { lat: -23.600007, lng: -46.670008 };
const current: GeoPoint = { lat: -23.555, lng: -46.64 };
const mid: GeoPoint = { lat: -23.556, lng: -46.64 };

const buildStop = (
  sequence: number,
  point: GeoPoint | null,
  overrides: Partial<PublicRouteMapStop> = {},
): PublicRouteMapStop => ({
  sequence,
  point,
  delivered: false,
  isNext: false,
  customerName: `Cliente ${sequence}`,
  ...overrides,
});

const osrmResponse = (
  coordinates: ReadonlyArray<readonly [number, number]>,
  distanceMeters = 6_400,
) => ({
  code: 'Ok',
  routes: [
    {
      distance: distanceMeters,
      geometry: { type: 'LineString', coordinates },
    },
  ],
});

const toLatLng = (geoPoint: GeoPoint): [number, number] => [geoPoint.lat, geoPoint.lng];
/** OSRM/GeoJSON uses [longitude, latitude]. */
const toOsrm = (geoPoint: GeoPoint): [number, number] => [geoPoint.lng, geoPoint.lat];

describe('PublicDeliveryRouteMapComponent', () => {
  const leaflet = L as unknown as {
    map: jest.Mock;
    marker: jest.Mock;
    polyline: jest.Mock;
    tileLayer: jest.Mock;
    divIcon: jest.Mock;
    latLngBounds: jest.Mock;
  };
  let geolocationMock: { getCurrentPosition: jest.Mock };
  let httpMock: HttpTestingController;

  const createFixture = (
    stops: readonly PublicRouteMapStop[],
    origin: GeoPoint | null = store,
  ): ComponentFixture<PublicDeliveryRouteMapComponent> => {
    const fixture = TestBed.createComponent(PublicDeliveryRouteMapComponent);
    fixture.componentRef.setInput('stops', stops);
    fixture.componentRef.setInput('originLatitude', origin?.lat ?? null);
    fixture.componentRef.setInput('originLongitude', origin?.lng ?? null);
    fixture.componentRef.setInput(
      'externalMapUrl',
      'https://www.google.com/maps/dir/?api=1&destination=1,2',
    );
    fixture.detectChanges();
    return fixture;
  };

  const routeRequest = () =>
    httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));

  beforeEach(async () => {
    TestBed.resetTestingModule();
    jest.clearAllMocks();
    geolocationMock = { getCurrentPosition: jest.fn() };
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: geolocationMock,
    });

    await TestBed.configureTestingModule({
      imports: [PublicDeliveryRouteMapComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify({ ignoreCancelled: true });
  });

  it('requests the whole route in one multi-waypoint call, in sequence, skipping delivered stops', () => {
    const fixture = createFixture([
      buildStop(1, point1, { isNext: true }),
      buildStop(2, point2),
      buildStop(3, point3),
      buildStop(4, point4),
      buildStop(5, point5, { delivered: true }),
    ]);

    expect(leaflet.map).toHaveBeenCalledTimes(1);

    const request = routeRequest();
    expect(request.request.method).toBe('GET');
    expect(request.request.url).toBe(
      `${environment.routingUrl}/${store.lng},${store.lat}`
      + `;${point1.lng},${point1.lat}`
      + `;${point2.lng},${point2.lat}`
      + `;${point3.lng},${point3.lat}`
      + `;${point4.lng},${point4.lat}`,
    );
    expect(request.request.params.get('overview')).toBe('full');
    expect(request.request.params.get('geometries')).toBe('geojson');
    expect(request.request.urlWithParams).not.toContain('Cliente');
    expect(request.request.urlWithParams).not.toContain('ABC12345');
    expect(request.request.headers.has('Authorization')).toBe(false);

    request.flush(osrmResponse([
      toOsrm(store),
      toOsrm(mid),
      toOsrm(point1),
      toOsrm(point2),
      toOsrm(point3),
      toOsrm(point4),
    ], 6_400));
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.route()!.kind).toBe('road');
    expect(component.route()!.stops.map((stop) => stop.sequence)).toEqual([1, 2, 3, 4]);
    expect(leaflet.polyline).toHaveBeenCalledTimes(1);
    expect(leaflet.polyline.mock.calls[0][0]).toEqual([
      toLatLng(store),
      toLatLng(mid),
      toLatLng(point1),
      toLatLng(point2),
      toLatLng(point3),
      toLatLng(point4),
    ]);
    expect(leaflet.polyline.mock.calls[0][1].dashArray).toBeUndefined();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Rota por ruas com 4 parada(s)');
    expect(text).not.toContain('Rota aproximada em linha reta');
    expect(text).toContain('Rota completa calculada por ruas');

    fixture.destroy();
  });

  it('numbers the customer markers and distinguishes next, delivered and store', () => {
    const fixture = createFixture([
      buildStop(1, point1, { isNext: true }),
      buildStop(2, point2),
      buildStop(3, point3, { delivered: true }),
    ]);

    routeRequest().flush(osrmResponse([
      toOsrm(store),
      toOsrm(point1),
      toOsrm(point2),
    ]));
    fixture.detectChanges();

    expect(leaflet.marker).toHaveBeenCalledTimes(4);

    const iconClasses = leaflet.divIcon.mock.calls.map(([options]) => options.className as string);
    expect(iconClasses).toEqual([
      'public-route-pin is-store',
      'public-route-pin is-next',
      'public-route-pin is-pending',
      'public-route-pin is-delivered',
    ]);

    const icons = leaflet.divIcon.mock.calls.map(([options]) => options);
    expect(icons[1].html).toContain('>1<');
    expect(icons[2].html).toContain('>2<');
    expect(icons[3].html).toContain('>3<');
    expect(icons[1].iconSize).toEqual([34, 34]);
    expect(icons[3].iconSize).toEqual([26, 26]);

    const markerTitles = leaflet.marker.mock.calls.map(([, options]) => options.title as string);
    expect(markerTitles).toContain('Próxima parada 1 - Cliente 1');
    expect(markerTitles).toContain('Parada 3 - Cliente 3 (entregue)');
    expect(markerTitles).toContain('Loja / origem');

    // Bounds include the store and every stop, delivered included.
    const boundsCall = leaflet.latLngBounds.mock.calls.at(-1)?.[0] as [number, number][];
    expect(boundsCall).toEqual(expect.arrayContaining([
      toLatLng(store),
      toLatLng(point1),
      toLatLng(point2),
      toLatLng(point3),
    ]));

    fixture.destroy();
  });

  it('shows an explicit approximate route with a dashed line when OSRM fails', () => {
    const fixture = createFixture([
      buildStop(1, point1, { isNext: true }),
      buildStop(2, point2),
    ]);

    routeRequest().flush('routing unavailable', { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.route()!.kind).toBe('straight-line');
    expect(leaflet.polyline).toHaveBeenCalledTimes(1);
    expect(leaflet.polyline.mock.calls[0][0]).toEqual([
      toLatLng(store),
      toLatLng(point1),
      toLatLng(point2),
    ]);
    expect(leaflet.polyline.mock.calls[0][1].dashArray).toBe('9 7');

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Rota aproximada');
    expect(text).toContain('linha reta');
    expect(text).toContain('roteador por ruas está indisponível');
    expect(text).not.toContain('Rota por ruas com');
    expect(text).not.toContain('Rota completa calculada por ruas');

    fixture.destroy();
  });

  it('recalculates the route and highlights the next stop after a delivery', () => {
    const fixture = createFixture([
      buildStop(1, point1, { isNext: true }),
      buildStop(2, point2),
    ]);

    routeRequest().flush(osrmResponse([toOsrm(store), toOsrm(point1), toOsrm(point2)]));
    fixture.detectChanges();

    fixture.componentRef.setInput('stops', [
      buildStop(1, point1, { delivered: true }),
      buildStop(2, point2, { isNext: true }),
    ] satisfies readonly PublicRouteMapStop[]);
    fixture.detectChanges();

    const secondRequest = routeRequest();
    expect(secondRequest.request.url).toBe(
      `${environment.routingUrl}/${store.lng},${store.lat};${point2.lng},${point2.lat}`,
    );
    secondRequest.flush(osrmResponse([toOsrm(store), toOsrm(point2)], 2_800));
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.route()!.stops.map((stop) => stop.sequence)).toEqual([2]);

    const lastIconClasses = leaflet.divIcon.mock.calls
      .slice(-3)
      .map(([options]) => options.className as string);
    expect(lastIconClasses).toEqual([
      'public-route-pin is-store',
      'public-route-pin is-delivered',
      'public-route-pin is-next',
    ]);

    expect(fixture.nativeElement.textContent).toContain('Rota por ruas com 1 parada(s)');

    fixture.destroy();
  });

  it('starts the route from the driver location when permission is granted', () => {
    let successCallback:
      | ((position: { coords: { latitude: number; longitude: number } }) => void)
      | undefined;
    geolocationMock.getCurrentPosition.mockImplementation((success) => {
      successCallback = success;
    });

    const fixture = createFixture([
      buildStop(1, point1, { isNext: true }),
      buildStop(2, point2),
    ]);

    routeRequest().flush(osrmResponse([toOsrm(store), toOsrm(point1), toOsrm(point2)]));
    fixture.detectChanges();

    expect(successCallback).toBeDefined();
    successCallback!({ coords: { latitude: current.lat, longitude: current.lng } });
    fixture.detectChanges();

    const currentRequest = httpMock.expectOne(
      (req) =>
        req.url.startsWith(environment.routingUrl)
        && req.url.includes(`${current.lng},${current.lat}`),
    );
    expect(currentRequest.request.url).toBe(
      `${environment.routingUrl}/${current.lng},${current.lat}`
      + `;${point1.lng},${point1.lat};${point2.lng},${point2.lat}`,
    );
    currentRequest.flush(osrmResponse([
      toOsrm(current),
      toOsrm(point1),
      toOsrm(point2),
    ], 5_100));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Rota por ruas com 2 parada(s)');
    expect(fixture.nativeElement.textContent).toContain('Você');

    fixture.destroy();
  });

  it('keeps an actionable state when there are not enough coordinates', () => {
    const fixture = createFixture(
      [
        buildStop(1, point1, { isNext: true }),
        buildStop(2, null),
      ],
      null,
    );

    expect(leaflet.map).toHaveBeenCalledTimes(1);
    expect(leaflet.marker).toHaveBeenCalledTimes(1);
    expect(leaflet.polyline).not.toHaveBeenCalled();
    httpMock.expectNone((req) => req.url.startsWith(environment.routingUrl));

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Só há uma parada pendente com coordenadas');
    expect(text).toContain('1 parada(s) sem coordenadas');
    expect(text).toContain('navegação externa');
    expect(fixture.nativeElement.querySelector('a[href^="https://www.google.com/maps"]')).not.toBeNull();

    fixture.destroy();
  });

  it('shows the fallback without Leaflet when no point has coordinates', () => {
    const fixture = createFixture([buildStop(1, null, { isNext: true })], null);

    expect(leaflet.map).not.toHaveBeenCalled();
    expect(geolocationMock.getCurrentPosition).not.toHaveBeenCalled();
    httpMock.expectNone((req) => req.url.startsWith(environment.routingUrl));

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Mapa indisponível para esta rota');
    expect(text).toContain('navegação externa');
    expect(fixture.nativeElement.querySelector('a[href^="https://www.google.com/maps"]')).not.toBeNull();

    fixture.destroy();
  });

  it('surfaces a tile loading failure with the external fallback', () => {
    const fixture = createFixture([buildStop(1, point1, { isNext: true })]);
    routeRequest().flush(osrmResponse([toOsrm(store), toOsrm(point1)]));

    const tileLayerInstance = leaflet.tileLayer.mock.results[0].value as { on: jest.Mock };
    const tileErrorHandler = tileLayerInstance.on.mock.calls.find(
      ([event]: [string]) => event === 'tileerror',
    )?.[1] as (() => void) | undefined;

    expect(tileErrorHandler).toBeDefined();
    tileErrorHandler!();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar as imagens do mapa');
    expect(fixture.nativeElement.textContent).toContain('As imagens do mapa não carregaram');
    expect(fixture.nativeElement.querySelector('a[href^="https://www.google.com/maps"]')).not.toBeNull();

    fixture.destroy();
  });
});
