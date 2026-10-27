import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';
import { DeliveryDriverService } from './delivery-driver.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';

describe('DeliveryDriverService', () => {
  let service: DeliveryDriverService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DeliveryDriverService, ApiService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(DeliveryDriverService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists store delivery drivers for the authenticated seller', () => {
    service.listStoreDrivers().subscribe((drivers) => expect(drivers).toHaveLength(1));

    const req = httpMock.expectOne('/api/delivery-drivers/store');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'driver-1', name: 'Ana', email: 'ana@example.com', phoneNumber: '11999999999', status: 2, canResendInvite: false }]);
  });

  it('creates and updates a simple motoboy without e-mail', () => {
    service.createDriver({
      name: 'Ana',
      phoneNumber: '11999999999',
      whatsAppPhoneNumber: '11988887777',
      isActive: true,
    }).subscribe();
    const create = httpMock.expectOne('/api/delivery-drivers');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({
      name: 'Ana',
      phoneNumber: '11999999999',
      whatsAppPhoneNumber: '11988887777',
      isActive: true,
    });
    create.flush({ id: 'link-1', deliveryDriverId: 'driver-1', name: 'Ana', isActive: true });

    service.updateDriver('link-1', {
      name: 'Ana Atualizada',
      phoneNumber: '11999999999',
      whatsAppPhoneNumber: null,
      isActive: false,
    }).subscribe();
    const update = httpMock.expectOne('/api/delivery-drivers/link-1');
    expect(update.request.method).toBe('PUT');
    update.flush({ id: 'link-1', deliveryDriverId: 'driver-1', name: 'Ana Atualizada', isActive: false });

    service.setDriverActive('link-1', true).subscribe();
    const status = httpMock.expectOne('/api/delivery-drivers/link-1/status');
    expect(status.request.method).toBe('PATCH');
    expect(status.request.body).toEqual({ isActive: true });
    status.flush({ id: 'link-1', deliveryDriverId: 'driver-1', name: 'Ana', isActive: true });
  });

  it('deletes a store driver by its store-scoped id', () => {
    service.deleteDriver('link-1').subscribe();

    const req = httpMock.expectOne('/api/delivery-drivers/link-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('keeps the legacy invite endpoint for already sent links', () => {
    service.invite({ fullName: 'Ana', email: 'ana@example.com', phoneNumber: '11999999999' }).subscribe();

    const req = httpMock.expectOne('/api/delivery-drivers/store/invite');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      fullName: 'Ana',
      email: 'ana@example.com',
      phoneNumber: '11999999999',
    });
    req.flush({ id: 'driver-1', name: 'Ana', email: 'ana@example.com', phoneNumber: '11999999999', status: 1, canResendInvite: true });
  });

  it('resends and revokes invites by store driver id', () => {
    service.resendInvite('link-1').subscribe();
    const resend = httpMock.expectOne('/api/delivery-drivers/store/link-1/resend-invite');
    expect(resend.request.method).toBe('POST');
    resend.flush({});

    service.revoke('link-1').subscribe();
    const revoke = httpMock.expectOne('/api/delivery-drivers/store/link-1/revoke');
    expect(revoke.request.method).toBe('POST');
    revoke.flush({});
  });

  it('previews and accepts an invite token', () => {
    service.previewInvite('token with space').subscribe();
    const preview = httpMock.expectOne('/api/delivery-drivers/invites/token%20with%20space');
    expect(preview.request.method).toBe('GET');
    preview.flush({ valid: true, alreadyAccepted: false, storeName: 'Loja', driverName: 'Ana', maskedEmail: 'an***@example.com' });

    service.acceptInvite('token with space', { password: 'SenhaForte123' }).subscribe();
    const accept = httpMock.expectOne('/api/delivery-drivers/invites/token%20with%20space/accept');
    expect(accept.request.method).toBe('POST');
    expect(accept.request.body).toEqual({ password: 'SenhaForte123' });
    accept.flush({ succeeded: true });
  });

  it('assigns and removes a store driver for an order', () => {
    service.assignOrder('order-1', 'link-1').subscribe();
    const assign = httpMock.expectOne('/api/orders/store/order-1/delivery-driver');
    expect(assign.request.method).toBe('PUT');
    expect(assign.request.body).toEqual({ deliveryDriverId: 'link-1' });
    assign.flush({ id: 'order-1' });

    service.removeAssignment('order-1').subscribe();
    const remove = httpMock.expectOne('/api/orders/store/order-1/delivery-driver');
    expect(remove.request.method).toBe('DELETE');
    remove.flush({ id: 'order-1' });
  });

  it('loads driver stores and orders by validated store id', () => {
    service.getDriverStores().subscribe();
    const stores = httpMock.expectOne('/api/driver/stores');
    expect(stores.request.method).toBe('GET');
    stores.flush([]);

    service.getDriverOrders('store 1').subscribe();
    const orders = httpMock.expectOne('/api/driver/orders?storeId=store%201');
    expect(orders.request.method).toBe('GET');
    orders.flush({ storeNotAvailable: false, orders: [] });
  });

  it('starts delivery and confirms the delivery PIN', () => {
    service.startDelivery('order-1').subscribe();
    const start = httpMock.expectOne('/api/driver/orders/order-1/start');
    expect(start.request.method).toBe('POST');
    start.flush({ status: OrderStatus.OnDelivery });

    service.confirmPin('order-1', '123456').subscribe();
    const pin = httpMock.expectOne('/api/driver/orders/order-1/confirm-pin');
    expect(pin.request.method).toBe('POST');
    expect(pin.request.body).toEqual({ pin: '123456' });
    pin.flush({ status: OrderStatus.Delivered });
  });
});
