import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PaymentPageComponent } from './payment-page.component';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { PaymentService } from '../../core/services/payment.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { OrderService } from '../../core/services/order.service';
import { AuthService } from '../../core/services/auth.service';
import { SignalRService } from '../../core/services/signalr.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { AddressService } from '../../core/services/address.service';
import { StoreService } from '../../core/services/store.service';

describe('PaymentPageComponent', () => {
  let cart: CartService;
  let checkoutMock: { [key: string]: any };
  let paymentMock: { createPayment: jest.Mock };
  let routerMock: { navigate: jest.Mock; url: string };

  beforeEach(async () => {
    localStorage.clear();

    checkoutMock = {
      fulfillmentType: signal(FulfillmentType.Delivery),
      customerAddressId: signal('addr1'),
      customerAddress: signal(null),
      lastOrderId: signal(null),
      lastOrderCode: signal(null),
      orderNotes: signal(''),
      preview: jest.fn().mockReturnValue(of({ deliveryFee: 0, freeShippingApplied: false })),
      confirm: jest.fn().mockReturnValue(of({ orderId: 'order1', code: '0001' })),
    };
    paymentMock = { createPayment: jest.fn().mockReturnValue(of({ paymentId: 'payment1', gatewayCheckoutUrl: 'https://pay.example/pix' })) };
    routerMock = { navigate: jest.fn(), url: '/loja/checkout/pagamento' };

    await TestBed.configureTestingModule({
      imports: [PaymentPageComponent],
      providers: [
        CartService,
        CustomerOrderTrackingService,
        { provide: CheckoutService, useValue: checkoutMock },
        { provide: PaymentService, useValue: paymentMock },
        { provide: AddressService, useValue: { list: jest.fn().mockReturnValue(of([{ id: 'addr1', city: 'Campos', cep: '28000-000', neighborhood: 'Jardim Aurora' }])) } },
        { provide: StoreService, useValue: { getStoreById: jest.fn().mockReturnValue(of({ address: { city: 'Campos', zipCode: '28010-000', neighborhood: 'Centro' } })) } },
        { provide: OrderService, useValue: { getOrder: jest.fn().mockReturnValue(of({ id: 'order1' })) } },
        { provide: AuthService, useValue: { token: signal('customer-token'), token$: of('customer-token'), getToken: () => 'customer-token' } },
        {
          provide: SignalRService,
          useValue: {
            startCustomerHub: jest.fn().mockResolvedValue(undefined),
            onCustomerEvent: jest.fn(),
            removeCustomerListener: jest.fn(),
            stopCustomerHub: jest.fn(),
            onCustomerStateChange: jest.fn(() => jest.fn()),
          },
        },
        { provide: Router, useValue: routerMock },
        { provide: Location, useValue: { back: jest.fn() } },
      ],
    }).compileComponents();

    cart = TestBed.inject(CartService);
    cart.storeId.set('store1');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
  });

  it('should render only Pix and pay-on-receive as native radio inputs', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    const radios = fixture.debugElement.queryAll(By.css('input[type="radio"][name="payment-category"]'));

    expect(radios).toHaveLength(2);
    expect(radios.map(radio => radio.nativeElement.value)).toEqual(['pix', 'receive']);
  });

  it('should confirm order directly when pay on receive is selected', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('receive');
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledWith(expect.objectContaining({
      paymentMethod: PaymentMethod.CashOnDelivery,
      storeId: 'store1',
    }));
    expect(paymentMock.createPayment).not.toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'order1']);
  });

  it('tracks the order before navigating when pay on receive is selected', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const trackOrderSpy = jest.spyOn(tracking, 'trackOrder');

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('receive');
    fixture.componentInstance.continue();

    expect(trackOrderSpy).toHaveBeenCalledWith('order1');
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'order1']);
  });

  it('should create pending Pix payment after confirming Pix order', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('pix');
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledWith(expect.objectContaining({
      paymentMethod: PaymentMethod.PixOnline,
      storeId: 'store1',
    }));
    expect(paymentMock.createPayment).toHaveBeenCalledWith('order1');
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'checkout', 'pagar']);
  });

  it('retries Pix payment for the existing order instead of re-confirming it', () => {
    checkoutMock.lastOrderId.set('order1');
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('pix');
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).not.toHaveBeenCalled();
    expect(paymentMock.createPayment).toHaveBeenCalledWith('order1');
  });

  it('keeps the created order and allows retrying Pix without creating a new order', () => {
    paymentMock.createPayment.mockReturnValueOnce(throwError(() => new Error('pix fail')));
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('pix');
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledTimes(1);
    expect(checkoutMock.lastOrderId()).toBe('order1');
    expect(fixture.componentInstance.errorMessage()).toContain('Pedido criado');

    paymentMock.createPayment.mockReturnValueOnce(of({ paymentId: 'payment1', gatewayCheckoutUrl: 'https://pay.example/pix' }));
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledTimes(1);
    expect(paymentMock.createPayment).toHaveBeenCalledTimes(2);
    expect(paymentMock.createPayment).toHaveBeenLastCalledWith('order1');
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'checkout', 'pagar']);
  });

  it('should mark the details sheet as a modal dialog', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.componentInstance.showDetailsModal.set(true);
    fixture.detectChanges();

    const modal = fixture.debugElement.query(By.css('.modal-sheet')).nativeElement as HTMLElement;

    expect(modal.getAttribute('role')).toBe('dialog');
    expect(modal.getAttribute('aria-modal')).toBe('true');
    expect(modal.getAttribute('aria-labelledby')).toBe('details-title');
  });

  it('should show the delivery coverage modal instead of a toast when checkout blocks the neighborhood', () => {
    checkoutMock.confirm.mockReturnValueOnce(throwError(() => ({ error: { error: 'Ainda nao entregamos no seu bairro.' } })));
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.continue();
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('app-delivery-coverage-modal'))).not.toBeNull();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });

  it('should recognize the flat HttpErrorResponse shape returned by the API', () => {
    checkoutMock.confirm.mockReturnValueOnce(throwError(() => ({ error: 'Ainda nao entregamos no seu bairro.' })));
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.continue();
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('app-delivery-coverage-modal'))).not.toBeNull();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });

  it('does not navigate after destroy when the order confirms', () => {
    const pending: ((order: any) => void)[] = [];
    checkoutMock.confirm.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.select('receive');
    fixture.componentInstance.continue();
    fixture.destroy();

    pending[0]({ orderId: 'order1', code: '0001' });

    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('does not navigate after destroy when the Pix payment is created', () => {
    const pending: ((res: any) => void)[] = [];
    paymentMock.createPayment.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((res) => subscriber.next(res));
        }),
    );

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.select('pix');
    fixture.componentInstance.continue();
    fixture.destroy();

    pending[0]({ paymentId: 'payment1', gatewayCheckoutUrl: 'https://pay.example/pix' });

    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('cancels the checkout preview subscription on destroy', () => {
    let unsubscribed = false;
    checkoutMock.preview.mockReturnValue(
      new Observable(() => () => {
        unsubscribed = true;
      }),
    );

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.destroy();

    expect(unsubscribed).toBe(true);
  });
});
