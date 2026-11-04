import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';
import { DeliveryDriverService } from './delivery-driver.service';

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
    req.flush([{ id: 'driver-1', name: 'Ana', phoneNumber: '11999999999', status: 2 }]);
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

  it('does not expose invite, password or authenticated driver portal operations', () => {
    const legacy = service as unknown as Record<string, unknown>;

    expect(legacy['invite']).toBeUndefined();
    expect(legacy['resendInvite']).toBeUndefined();
    expect(legacy['revoke']).toBeUndefined();
    expect(legacy['previewInvite']).toBeUndefined();
    expect(legacy['acceptInvite']).toBeUndefined();
    expect(legacy['getDriverStores']).toBeUndefined();
    expect(legacy['getDriverOrders']).toBeUndefined();
    expect(legacy['getDriverOrder']).toBeUndefined();
    expect(legacy['startDelivery']).toBeUndefined();
    expect(legacy['confirmHandover']).toBeUndefined();
    expect(legacy['confirmPin']).toBeUndefined();
  });
});
