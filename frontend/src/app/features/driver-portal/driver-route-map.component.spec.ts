import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import L from 'leaflet';
import { environment } from '../../../environments/environment';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { DriverOrder } from '../../shared/models/delivery-driver.model';
import { DriverRouteMapComponent } from './driver-route-map.component';

const storeLat = -23.55052;
const storeLng = -46.633308;
const customerLat = -23.561414;
const customerLng = -46.655881;
const midLat = -23.556;
const midLng = -46.64;
const currentLat = -23.555;
const currentLng = -46.64;

const buildOrder = (overrides: Partial<DriverOrder> = {}): DriverOrder => ({
  id: 'order-1',
  code: 'ABC12345',
  storeId: 'store-1',
  status: OrderStatus.OnDelivery,
  fulfillmentType: FulfillmentType.Delivery,
  paymentMethod: PaymentMethod.CashOnDelivery,
  customerName: 'Cliente Teste',
  addressStreet: 'Rua Teste',
  addressNumber: '10',
  addressNeighborhood: 'Centro',
  addressCity: 'Sao Paulo',
  addressState: 'SP',
  addressCep: '01001000',
  originLatitude: storeLat,
  originLongitude: storeLng,
  destinationLatitude: customerLat,
  destinationLongitude: customerLng,
  subtotal: 20,
  deliveryFee: 4,
  total: 24,
  createdAtUtc: '2026-10-24T12:00:00Z',
  items: [],
  canStartDelivery: false,
  canConfirmPin: true,
  ...overrides,
});

const osrmResponse = (
  coordinates: ReadonlyArray<readonly [number, number]>,
  distanceMeters = 2_500,
) => ({
  code: 'Ok',
  routes: [
    {
      distance: distanceMeters,
      geometry: { type: 'LineString', coordinates },
    },
  ],
});

describe('DriverRouteMapComponent', () => {
  const leaflet = L as unknown as {
    map: jest.Mock;
    marker: jest.Mock;
    polyline: jest.Mock;
    tileLayer: jest.Mock;
  };
  let geolocationMock: { getCurrentPosition: jest.Mock };
  let httpMock: HttpTestingController;

  const createFixture = (order: DriverOrder): ComponentFixture<DriverRouteMapComponent> => {
    const fixture = TestBed.createComponent(DriverRouteMapComponent);
    fixture.componentRef.setInput('order', order);
    fixture.componentRef.setInput('externalMapUrl', 'https://www.google.com/maps/dir/?api=1&destination=1,2');
    fixture.detectChanges();
    return fixture;
  };

  const routeRequest = () => httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));

  const flushRoadGeometry = (
    coordinates: ReadonlyArray<readonly [number, number]>,
    distanceMeters = 2_500,
  ): void => {
    routeRequest().flush(osrmResponse(coordinates, distanceMeters));
  };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    jest.clearAllMocks();
    geolocationMock = { getCurrentPosition: jest.fn() };
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: geolocationMock,
    });

    await TestBed.configureTestingModule({
      imports: [DriverRouteMapComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify({ ignoreCancelled: true });
  });

  it('renders store and customer markers, OSM attribution and the road geometry', () => {
    const fixture = createFixture(buildOrder());

    expect(leaflet.map).toHaveBeenCalledTimes(1);
    expect(leaflet.tileLayer).toHaveBeenCalledWith(
      expect.stringContaining('tile.openstreetmap.org'),
      expect.objectContaining({ attribution: expect.stringContaining('OpenStreetMap') }),
    );
    expect(leaflet.marker).toHaveBeenCalledTimes(2);

    flushRoadGeometry([
      [storeLng, storeLat],
      [midLng, midLat],
      [customerLng, customerLat],
    ], 3_200);
    fixture.detectChanges();

    expect(leaflet.polyline).toHaveBeenCalledTimes(1);
    expect(leaflet.polyline.mock.calls[0][0]).toEqual([
      [storeLat, storeLng],
      [midLat, midLng],
      [customerLat, customerLng],
    ]);
    expect(fixture.nativeElement.textContent).toContain('Rota por ruas loja → cliente');
    expect(fixture.nativeElement.textContent).toContain('Rota calculada por ruas');
    expect(fixture.nativeElement.textContent).not.toContain('Distância aproximada em linha reta');
    expect(fixture.nativeElement.textContent).toContain('Abrir rota no Google Maps');
    expect(fixture.nativeElement.querySelector('a[href^="https://www.google.com/maps"]')).not.toBeNull();

    fixture.destroy();
  });

  it('sends only the map coordinates to the routing provider', () => {
    createFixture(buildOrder());

    const request = routeRequest();
    expect(request.request.url).toBe(
      `${environment.routingUrl}/${storeLng},${storeLat};${customerLng},${customerLat}`,
    );
    expect(request.request.urlWithParams).not.toContain('ABC12345');
    expect(request.request.urlWithParams).not.toContain('Cliente');
    expect(request.request.urlWithParams).not.toContain('pin');
    request.flush(osrmResponse([[storeLng, storeLat], [customerLng, customerLat]]));
  });

  it('shows the explicit straight-line fallback when road routing fails', () => {
    const fixture = createFixture(buildOrder());

    routeRequest().flush('routing unavailable', { status: 503, statusText: 'Service Unavailable' });
    fixture.detectChanges();

    expect(leaflet.polyline).toHaveBeenCalledTimes(1);
    expect(leaflet.polyline.mock.calls[0][0]).toEqual([
      [storeLat, storeLng],
      [customerLat, customerLng],
    ]);
    expect(fixture.nativeElement.textContent).toContain('Distância aproximada em linha reta');
    expect(fixture.nativeElement.textContent).toContain('Rota por ruas indisponível agora');
    expect(fixture.nativeElement.textContent).not.toContain('Rota por ruas loja → cliente');

    fixture.destroy();
  });

  it('shows an actionable fallback and skips Leaflet when the order has no coordinates', () => {
    const fixture = createFixture(buildOrder({
      originLatitude: null,
      originLongitude: null,
      destinationLatitude: null,
      destinationLongitude: null,
    }));

    expect(leaflet.map).not.toHaveBeenCalled();
    expect(geolocationMock.getCurrentPosition).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Mapa indisponível para este pedido');
    expect(fixture.nativeElement.textContent).toContain('navegação externa');
    expect(fixture.nativeElement.querySelector('a[href^="https://www.google.com/maps"]')).not.toBeNull();
    httpMock.expectNone((req) => req.url.startsWith(environment.routingUrl));

    fixture.destroy();
  });

  it('renders only the store when the customer address has no coordinates', () => {
    const fixture = createFixture(buildOrder({
      destinationLatitude: null,
      destinationLongitude: null,
    }));

    expect(leaflet.marker).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('O endereço do cliente não tem coordenadas');
    expect(geolocationMock.getCurrentPosition).not.toHaveBeenCalled();
    httpMock.expectNone((req) => req.url.startsWith(environment.routingUrl));

    fixture.destroy();
  });

  it('keeps the store route and explains when geolocation is denied', () => {
    geolocationMock.getCurrentPosition.mockImplementation((_success, error) => {
      error({ code: 1, message: 'denied' });
    });

    const fixture = createFixture(buildOrder());
    flushRoadGeometry([[storeLng, storeLat], [customerLng, customerLat]]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Localização não autorizada');
    expect(fixture.nativeElement.textContent).toContain('Rota por ruas loja → cliente');
    expect(leaflet.polyline).toHaveBeenCalledTimes(1);
    expect(leaflet.marker).toHaveBeenCalledTimes(2);

    fixture.destroy();
  });

  it('recalculates from the current location when permission is granted', () => {
    let successCallback:
      | ((position: { coords: { latitude: number; longitude: number } }) => void)
      | undefined;
    geolocationMock.getCurrentPosition.mockImplementation((success) => {
      successCallback = success;
    });

    const fixture = createFixture(buildOrder());
    flushRoadGeometry([[storeLng, storeLat], [customerLng, customerLat]]);
    fixture.detectChanges();

    expect(successCallback).toBeDefined();
    successCallback!({ coords: { latitude: currentLat, longitude: currentLng } });

    const currentRequest = httpMock.expectOne(
      (req) =>
        req.url.startsWith(environment.routingUrl)
        && req.url.includes(`${currentLng},${currentLat}`),
    );
    currentRequest.flush(osrmResponse([[currentLng, currentLat], [customerLng, customerLat]], 1_900));
    fixture.detectChanges();

    expect(leaflet.marker).toHaveBeenCalledTimes(3);
    expect(leaflet.polyline).toHaveBeenCalledTimes(2);
    expect(leaflet.polyline.mock.calls[1][0]).toEqual([
      [currentLat, currentLng],
      [customerLat, customerLng],
    ]);
    expect(fixture.nativeElement.textContent).toContain('Rota por ruas a partir de você');
    expect(fixture.nativeElement.textContent).toContain('Você');

    fixture.destroy();
  });

  it('cancels the in-flight store route when the current position arrives', () => {
    let successCallback:
      | ((position: { coords: { latitude: number; longitude: number } }) => void)
      | undefined;
    geolocationMock.getCurrentPosition.mockImplementation((success) => {
      successCallback = success;
    });

    createFixture(buildOrder());
    const storeRequest = routeRequest();

    successCallback!({ coords: { latitude: currentLat, longitude: currentLng } });

    expect(storeRequest.cancelled).toBe(true);
    const currentRequest = httpMock.expectOne(
      (req) =>
        req.url.startsWith(environment.routingUrl)
        && req.url.includes(`${currentLng},${currentLat}`),
    );
    currentRequest.flush(osrmResponse([[currentLng, currentLat], [customerLng, customerLat]], 1_900));

    expect(leaflet.polyline).toHaveBeenCalledTimes(1);
  });

  it('ignores a geolocation callback and cancels the route request after destroy', () => {
    let successCallback:
      | ((position: { coords: { latitude: number; longitude: number } }) => void)
      | undefined;
    geolocationMock.getCurrentPosition.mockImplementation((success) => {
      successCallback = success;
    });

    const fixture = createFixture(buildOrder());
    const component = fixture.componentInstance;
    const storeRequest = routeRequest();
    const mapInstance = leaflet.map.mock.results[0].value as { remove: jest.Mock };

    fixture.destroy();

    expect(mapInstance.remove).toHaveBeenCalledTimes(1);
    expect(storeRequest.cancelled).toBe(true);

    successCallback?.({ coords: { latitude: currentLat, longitude: currentLng } });

    expect(component.locationStatus()).toBe('requesting');
    expect(leaflet.marker).toHaveBeenCalledTimes(2);
    expect(component.route()).toBeNull();
  });

  it('surfaces a tile loading failure with the external fallback', () => {
    const fixture = createFixture(buildOrder());
    flushRoadGeometry([[storeLng, storeLat], [customerLng, customerLat]]);

    const tileLayerInstance = leaflet.tileLayer.mock.results[0].value as {
      on: jest.Mock;
    };
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
