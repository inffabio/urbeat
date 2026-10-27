import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { DeliveryRouteService } from '../../core/services/delivery-route.service';
import { OrderService } from '../../core/services/order.service';
import { ToastService } from '../../core/services/toast.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { DeliveryRouteStatus } from '../../shared/models/delivery-route.model';
import { SellerDeliveryRoutesPageComponent } from './seller-delivery-routes-page.component';

const driver = {
  id: 'link-1',
  deliveryDriverId: 'driver-1',
  name: 'Ana Motoboy',
  email: '',
  phoneNumber: '11999999999',
  whatsAppPhoneNumber: '11988887777',
  isActive: true,
  hasAccount: false,
  status: 2,
  canResendInvite: false,
};

const order = {
  id: 'order-1',
  code: 'ABC',
  storeId: 'store-1',
  status: OrderStatus.OnDelivery,
  fulfillmentType: FulfillmentType.Delivery,
  paymentMethod: PaymentMethod.CashOnDelivery,
  total: 50,
  createdAtUtc: '2026-10-27T10:00:00Z',
  deliveryDriverId: 'driver-1',
  customerName: 'Ana Cliente',
  addressSummary: 'Rua A, 10',
};

const route = {
  id: 'route-1',
  deliveryDriverId: 'link-1',
  driverName: 'Ana Motoboy',
  driverWhatsAppPhoneNumber: '11988887777',
  status: DeliveryRouteStatus.Active,
  createdAtUtc: '2026-10-27T11:00:00Z',
  expiresAtUtc: '2026-10-27T23:00:00Z',
  totalStops: 1,
  deliveredStops: 0,
  orders: [{
    orderId: 'order-1',
    sequence: 1,
    code: 'ABC',
    customerName: 'Ana Cliente',
    addressSummary: 'Rua A, 10',
    status: OrderStatus.OnDelivery,
    isDelivered: false,
    canDeliver: true,
  }],
};

describe('SellerDeliveryRoutesPageComponent', () => {
  let routeServiceMock: {
    listStoreRoutes: jest.Mock;
    createRoute: jest.Mock;
    updateRoute: jest.Mock;
    shareRoute: jest.Mock;
    revokeRoute: jest.Mock;
  };
  let driverServiceMock: { listStoreDrivers: jest.Mock };
  let orderServiceMock: { getStoreOrders: jest.Mock };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock };

  beforeEach(async () => {
    routeServiceMock = {
      listStoreRoutes: jest.fn().mockReturnValue(of([route])),
      createRoute: jest.fn().mockReturnValue(of({ route, shareUrl: 'https://www.urbeat.com.br/rota/token' })),
      updateRoute: jest.fn().mockReturnValue(of({ route })),
      shareRoute: jest.fn().mockReturnValue(of({
        route,
        shareUrl: 'https://www.urbeat.com.br/rota/new-token',
      })),
      revokeRoute: jest.fn().mockReturnValue(of({
        route: { ...route, status: DeliveryRouteStatus.Revoked, revokedAtUtc: '2026-10-27T12:00:00Z' },
      })),
    };
    driverServiceMock = { listStoreDrivers: jest.fn().mockReturnValue(of([driver])) };
    orderServiceMock = { getStoreOrders: jest.fn().mockReturnValue(of({ items: [order], totalItems: 1 })) };
    toastMock = {
      showSuccess: jest.fn().mockResolvedValue(undefined),
      showError: jest.fn().mockResolvedValue(undefined),
    };

    await TestBed.configureTestingModule({
      imports: [SellerDeliveryRoutesPageComponent],
      providers: [
        { provide: DeliveryRouteService, useValue: routeServiceMock },
        { provide: DeliveryDriverService, useValue: driverServiceMock },
        { provide: OrderService, useValue: orderServiceMock },
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();
  });

  it('lists routes with driver, progress and stops', () => {
    const fixture = TestBed.createComponent(SellerDeliveryRoutesPageComponent);
    fixture.detectChanges();

    expect(routeServiceMock.listStoreRoutes).toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ana Motoboy');
    expect(text).toContain('0/1 entregue');
    expect(text).toContain('#ABC');
    expect(text).toContain('Compartilhar no WhatsApp');
  });

  it('creates a route with the selected driver and orders', () => {
    const fixture = TestBed.createComponent(SellerDeliveryRoutesPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openCreateDialog();
    component.selectDriver('link-1');
    component.toggleOrder('order-1');
    component.submitRoute();

    expect(routeServiceMock.createRoute).toHaveBeenCalledWith({
      deliveryDriverId: 'link-1',
      orderIds: ['order-1'],
    });
    expect(toastMock.showSuccess).toHaveBeenCalled();
  });

  it('opens WhatsApp with the shared absolute route URL', () => {
    const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);
    const fixture = TestBed.createComponent(SellerDeliveryRoutesPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.shareRoute(route as any);

    expect(routeServiceMock.shareRoute).toHaveBeenCalledWith('route-1');
    expect(openSpy).toHaveBeenCalledWith(
      expect.stringContaining('https://wa.me/5511988887777?text='),
      '_blank',
      'noopener,noreferrer',
    );
    expect(decodeURIComponent(openSpy.mock.calls[0][0] as string)).toContain('/rota/new-token');
    openSpy.mockRestore();
  });

  it('revokes a route after confirmation', () => {
    const fixture = TestBed.createComponent(SellerDeliveryRoutesPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.requestRevoke(route as any);
    component.confirmRevoke();

    expect(routeServiceMock.revokeRoute).toHaveBeenCalledWith('route-1');
    expect(component.revokeTarget()).toBeNull();
  });
});
