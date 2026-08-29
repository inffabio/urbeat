import { TestBed } from '@angular/core/testing';

import { CheckoutService } from './checkout.service';
import { ApiService } from './api.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';

describe('CheckoutService', () => {
  let service: CheckoutService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: ApiService, useValue: {} }],
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
});
