import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { of } from 'rxjs';

import { OnlinePaymentPageComponent } from './online-payment-page.component';
import { CartService } from '../../../core/services/cart.service';
import { CheckoutService } from '../../../core/services/checkout.service';
import { OrderService } from '../../../core/services/order.service';
import { PaymentService } from '../../../core/services/payment.service';
import { ToastService } from '../../../core/services/toast.service';
import { FulfillmentType } from '../../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../../shared/enums/payment-method.enum';
import { OrderStatus } from '../../../shared/enums/order-status.enum';

describe('OnlinePaymentPageComponent', () => {
  let checkoutMock: { [key: string]: any };
  let paymentMock: { getPayment: jest.Mock; createPayment: jest.Mock };
  let orderMock: { getOrder: jest.Mock };
  let routerMock: { navigate: jest.Mock; url: string };

  beforeEach(async () => {
    localStorage.clear();

    checkoutMock = {
      fulfillmentType: signal(FulfillmentType.Delivery),
      customerAddressId: signal('addr1'),
      orderNotes: signal(''),
      lastOrderId: signal('o1'),
      lastOrderCode: signal('ABC123'),
      confirm: jest.fn().mockReturnValue(of({ orderId: 'o1', code: 'ABC123' })),
    };
    paymentMock = {
      createPayment: jest.fn().mockReturnValue(of({})),
      getPayment: jest.fn().mockReturnValue(of({
        paymentId: 'pay1',
        orderId: 'o1',
        gateway: 1,
        gatewayTransactionId: 'tx1',
        gatewayCheckoutUrl: 'https://pay.example/pix',
        method: PaymentMethod.PixOnline,
        status: 1,
        amount: 20,
        createdAtUtc: '2026-07-29T00:00:00.000Z',
      })),
    };
    orderMock = { getOrder: jest.fn().mockReturnValue(of({ status: OrderStatus.PendingPayment })) };
    routerMock = { navigate: jest.fn(), url: '/loja/checkout/pagar' };

    await TestBed.configureTestingModule({
      imports: [OnlinePaymentPageComponent],
      providers: [
        CartService,
        { provide: CheckoutService, useValue: checkoutMock },
        { provide: PaymentService, useValue: paymentMock },
        { provide: OrderService, useValue: orderMock },
        { provide: ToastService, useValue: { showError: jest.fn() } },
        { provide: Router, useValue: routerMock },
        { provide: Location, useValue: { back: jest.fn() } },
      ],
    }).compileComponents();

    const cart = TestBed.inject(CartService);
    cart.storeId.set('store1');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
  });

  it('should load the existing payment for the pending Pix order', () => {
    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(paymentMock.getPayment).toHaveBeenCalledWith('o1');
    expect(fixture.debugElement.query(By.css('.pix-link')).nativeElement.getAttribute('href')).toBe('https://pay.example/pix');
  });

  it('should not render online payment method radios', () => {
    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    const radios = fixture.debugElement.queryAll(By.css('input[type="radio"]'));

    expect(radios).toHaveLength(0);
  });

  it('should redirect to tracking when payment releases the order', () => {
    jest.useFakeTimers();
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.Received }));
    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'o1']);
    jest.useRealTimers();
  });
});
