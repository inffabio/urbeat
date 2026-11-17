import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { CheckoutService } from './checkout.service';
import { ApiService } from './api.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';

describe('CheckoutService', () => {
  let service: CheckoutService;
  let api: { post: jest.Mock };

  beforeEach(() => {
    api = { post: jest.fn().mockReturnValue(of({})) };
    TestBed.configureTestingModule({
      providers: [{ provide: ApiService, useValue: api }],
    });

    service = TestBed.inject(CheckoutService);
  });

  it('clears personal data and address on resetCheckout', () => {
    service.customerInfo.set({ fullName: 'Maria', email: 'maria@example.com', phoneNumber: '22999999999' });
    service.customerAddress.set({ cep: '28000-000', street: 'Rua A', number: '10', neighborhood: 'Centro', city: 'Campos', state: 'RJ' });
    service.customerAddressId.set('addr-1');

    service.resetCheckout();

    expect(service.customerInfo()).toBeNull();
    expect(service.customerAddress()).toBeNull();
    expect(service.customerAddressId()).toBeNull();
  });

  it('clears the pending order, verification and payment state on resetCheckout', () => {
    service.paymentMethod.set(PaymentMethod.PixOnline);
    service.orderNotes.set('sem cebola');
    service.lastOrderId.set('order-1');
    service.lastOrderCode.set('ABC123');
    service.verificationId.set('ver-1');
    service.verificationExpiresAtUtc.set('2026-07-28T12:00:00Z');
    service.verificationResendAvailableAtUtc.set('2026-07-28T12:01:00Z');
    service.verificationMaskedPhone.set('(**) ****-9999');

    service.resetCheckout();

    expect(service.paymentMethod()).toBeNull();
    expect(service.orderNotes()).toBe('');
    expect(service.lastOrderId()).toBeNull();
    expect(service.lastOrderCode()).toBeNull();
    expect(service.verificationId()).toBeNull();
    expect(service.verificationExpiresAtUtc()).toBeNull();
    expect(service.verificationResendAvailableAtUtc()).toBeNull();
    expect(service.verificationMaskedPhone()).toBeNull();
  });

  it('resets fulfillment back to Delivery on resetCheckout', () => {
    service.fulfillmentType.set(FulfillmentType.PickUp);

    service.resetCheckout();

    expect(service.fulfillmentType()).toBe(FulfillmentType.Delivery);
  });

  it('clears the customer address id as soon as fulfillment switches to PickUp', () => {
    service.customerAddressId.set('addr-1');

    service.setFulfillmentType(FulfillmentType.PickUp);

    expect(service.fulfillmentType()).toBe(FulfillmentType.PickUp);
    expect(service.customerAddressId()).toBeNull();
  });

  it('keeps the customer address id when fulfillment stays on Delivery', () => {
    service.customerAddressId.set('addr-1');

    service.setFulfillmentType(FulfillmentType.Delivery);

    expect(service.fulfillmentType()).toBe(FulfillmentType.Delivery);
    expect(service.customerAddressId()).toBe('addr-1');
  });

  it('omits a stale customerAddressId from a pickup preview request', () => {
    service.preview({
      storeId: 'store-1',
      fulfillmentType: FulfillmentType.PickUp,
      customerAddressId: 'addr-1',
      items: [],
    }).subscribe();

    expect(api.post).toHaveBeenCalledWith('/api/checkout/preview', expect.any(Object));
    expect(api.post.mock.calls[0][1]).not.toHaveProperty('customerAddressId');
  });

  it('omits a stale customerAddressId from a pickup confirm request', () => {
    service.confirm({
      storeId: 'store-1',
      fulfillmentType: FulfillmentType.PickUp,
      customerAddressId: 'addr-1',
      paymentMethod: PaymentMethod.CashOnDelivery,
      items: [],
    }).subscribe();

    expect(api.post).toHaveBeenCalledWith('/api/checkout/confirm', expect.any(Object));
    expect(api.post.mock.calls[0][1]).not.toHaveProperty('customerAddressId');
  });

  it('keeps the customerAddressId on delivery requests', () => {
    service.preview({
      storeId: 'store-1',
      fulfillmentType: FulfillmentType.Delivery,
      customerAddressId: 'addr-1',
      items: [],
    }).subscribe();

    expect(api.post).toHaveBeenCalledWith('/api/checkout/preview', expect.objectContaining({
      customerAddressId: 'addr-1',
    }));
  });
});
