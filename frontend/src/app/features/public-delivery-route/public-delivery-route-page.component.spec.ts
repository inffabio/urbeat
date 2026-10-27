import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DeliveryRouteService } from '../../core/services/delivery-route.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import {
  DeliveryRouteStatus,
  PublicDeliveryRoute,
} from '../../shared/models/delivery-route.model';
import { PublicDeliveryRoutePageComponent } from './public-delivery-route-page.component';

const buildRoute = (overrides: Partial<PublicDeliveryRoute> = {}): PublicDeliveryRoute => ({
  storeName: 'Loja Teste',
  driverName: 'Motoboy Teste',
  status: DeliveryRouteStatus.Active,
  expiresAtUtc: '2026-10-27T23:00:00Z',
  originLatitude: null,
  originLongitude: null,
  nextSequence: 1,
  totalStops: 2,
  deliveredStops: 0,
  stops: [
    {
      sequence: 1,
      orderCode: 'ABC',
      status: OrderStatus.OnDelivery,
      isNext: true,
      canDeliver: true,
      customerName: 'Ana',
      customerPhoneNumber: '11999999999',
      addressStreet: 'Rua A',
      addressNumber: '10',
      addressNeighborhood: 'Centro',
      destinationLatitude: null,
      destinationLongitude: null,
    },
    {
      sequence: 2,
      orderCode: 'DEF',
      status: OrderStatus.OnDelivery,
      isNext: false,
      canDeliver: false,
      customerName: 'Bia',
      addressStreet: 'Rua B',
      addressNumber: '20',
      addressNeighborhood: 'Centro',
      destinationLatitude: null,
      destinationLongitude: null,
    },
  ],
  ...overrides,
});

describe('PublicDeliveryRoutePageComponent', () => {
  let routeServiceMock: {
    getPublicRoute: jest.Mock;
    getPublicIndividualDelivery: jest.Mock;
    confirmPublicDelivery: jest.Mock;
    confirmPublicIndividualDelivery: jest.Mock;
  };
  let httpMock: HttpTestingController;
  let routeData: Record<string, unknown>;

  beforeEach(async () => {
    routeData = {};
    routeServiceMock = {
      getPublicRoute: jest.fn().mockReturnValue(of(buildRoute())),
      getPublicIndividualDelivery: jest.fn().mockReturnValue(of(buildRoute({
        totalStops: 1,
        nextSequence: 1,
        stops: [buildRoute().stops[0]],
      }))),
      confirmPublicDelivery: jest.fn().mockReturnValue(of({
        succeeded: true,
        notFound: false,
        gone: false,
        invalidState: false,
        pinInvalid: false,
        pinLocked: false,
        pinUnavailable: false,
        concurrentUpdate: false,
        remainingAttempts: 5,
        orderStatus: OrderStatus.Delivered,
        route: buildRoute({
          nextSequence: 2,
          deliveredStops: 1,
          stops: buildRoute().stops.map((stop, index) => index === 0
            ? { ...stop, isNext: false, canDeliver: false, status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-10-27T18:00:00Z' }
            : { ...stop, isNext: true, canDeliver: true }),
        }),
      })),
      confirmPublicIndividualDelivery: jest.fn().mockReturnValue(of({
        succeeded: true,
        notFound: false,
        gone: false,
        invalidState: false,
        pinInvalid: false,
        pinLocked: false,
        pinUnavailable: false,
        concurrentUpdate: false,
        remainingAttempts: 5,
        orderStatus: OrderStatus.Delivered,
        route: buildRoute({
          status: DeliveryRouteStatus.Completed,
          totalStops: 1,
          nextSequence: null,
          deliveredStops: 1,
          stops: [{
            ...buildRoute().stops[0],
            isNext: false,
            canDeliver: false,
            status: OrderStatus.Delivered,
            deliveryConfirmedAtUtc: '2026-10-27T18:00:00Z',
          }],
        }),
      })),
    };

    await TestBed.configureTestingModule({
      imports: [PublicDeliveryRoutePageComponent],
      providers: [
        { provide: DeliveryRouteService, useValue: routeServiceMock },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ token: 'public-token' }),
              data: routeData,
            },
          },
        },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    jest.useRealTimers();
    httpMock.verify({ ignoreCancelled: true });
  });

  it('loads the public route without authentication and highlights the next customer', () => {
    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();

    expect(routeServiceMock.getPublicRoute).toHaveBeenCalledWith('public-token');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Loja Teste');
    expect(text).toContain('Motoboy Teste');
    expect(text).toContain('Próximo cliente');
    expect(text).toContain('Ana');
    expect(text).toContain('Rua A, 10');
    expect(text).toContain('Sequência de entregas');
    expect(fixture.nativeElement.querySelector('.next-stop-card')).not.toBeNull();
  });

  it('rejects a wrong PIN locally before calling the API', () => {
    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openPinDialog(component.routeData()!.stops[0]);
    component.updatePin('123');
    component.submitPin();

    expect(routeServiceMock.confirmPublicDelivery).not.toHaveBeenCalled();
    expect(component.pinError()).toContain('4 dígitos');
  });

  it('shows the remaining attempts when the backend rejects the PIN', () => {
    routeServiceMock.confirmPublicDelivery.mockReturnValue(throwError(() => ({
      status: 400,
      error: { error: 'PIN incorreto.', remainingAttempts: 3 },
    })));

    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openPinDialog(component.routeData()!.stops[0]);
    component.updatePin('0000');
    component.submitPin();

    expect(component.pinError()).toContain('3');
  });

  it('confirms the next stop and advances to the following customer', () => {
    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openPinDialog(component.routeData()!.stops[0]);
    component.updatePin('1234');
    component.submitPin();

    expect(routeServiceMock.confirmPublicDelivery).toHaveBeenCalledWith('public-token', 1, '1234');
    expect(component.routeData()!.nextSequence).toBe(2);
    expect(component.successMessage()).toContain('ABC');
    expect(component.pinTarget()).toBeNull();
    expect(component.mapStops()[0].delivered).toBe(true);
    expect(component.mapStops()[1].isNext).toBe(true);
  });

  it('renders one full-route map for all stops instead of a map per stop', () => {
    routeServiceMock.getPublicRoute.mockReturnValue(of(buildRoute({
      originLatitude: -23.55052,
      originLongitude: -46.633308,
      stops: [
        {
          ...buildRoute().stops[0],
          destinationLatitude: -23.561414,
          destinationLongitude: -46.655881,
        },
        {
          ...buildRoute().stops[1],
          destinationLatitude: -23.570001,
          destinationLongitude: -46.640002,
        },
      ],
    })));

    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('app-public-delivery-route-map').length).toBe(1);
    expect(
      fixture.nativeElement.querySelectorAll('.route-stop-card app-public-delivery-route-map').length,
    ).toBe(0);
    expect(fixture.componentInstance.mapStops().map((stop) => stop.sequence)).toEqual([1, 2]);

    const request = httpMock.expectOne((req) => req.url.startsWith(environment.routingUrl));
    expect(request.request.url).toBe(
      `${environment.routingUrl}/-46.633308,-23.55052;-46.655881,-23.561414;-46.640002,-23.570001`,
    );
    request.flush({
      code: 'Ok',
      routes: [{
        distance: 4_200,
        geometry: {
          type: 'LineString',
          coordinates: [
            [-46.633308, -23.55052],
            [-46.655881, -23.561414],
            [-46.640002, -23.570001],
          ],
        },
      }],
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Rota completa');
    expect(fixture.nativeElement.textContent).toContain('Rota por ruas com 2 parada(s)');
  });

  it('shows the expired state for an invalidated link', () => {
    routeServiceMock.getPublicRoute.mockReturnValue(throwError(() => ({ status: 410 })));

    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Link expirado ou revogado');
  });

  it('shows the invalid state for an unknown token', () => {
    routeServiceMock.getPublicRoute.mockReturnValue(throwError(() => ({ status: 404 })));

    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Link inválido');
  });

  it('uses the individual delivery endpoint, hides the route sections and confirms with 4 digits', () => {
    routeData['individual'] = true;

    const fixture = TestBed.createComponent(PublicDeliveryRoutePageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    expect(routeServiceMock.getPublicRoute).not.toHaveBeenCalled();
    expect(routeServiceMock.getPublicIndividualDelivery).toHaveBeenCalledWith('public-token');
    expect(fixture.nativeElement.textContent).toContain('Entrega ativa');
    expect(fixture.nativeElement.textContent).not.toContain('Sequência de entregas');
    expect(fixture.nativeElement.textContent).toContain('últimos 4 dígitos do celular');

    component.openPinDialog(component.routeData()!.stops[0]);
    component.updatePin('1234');
    component.submitPin();

    expect(routeServiceMock.confirmPublicIndividualDelivery).toHaveBeenCalledWith('public-token', '1234');
    expect(routeServiceMock.confirmPublicDelivery).not.toHaveBeenCalled();
  });
});
