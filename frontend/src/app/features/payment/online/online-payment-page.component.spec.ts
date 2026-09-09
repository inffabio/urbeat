import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { OnlinePaymentPageComponent } from './online-payment-page.component';
import { CartService } from '../../../core/services/cart.service';
import { CheckoutService } from '../../../core/services/checkout.service';
import { CustomerOrderTrackingService } from '../../../core/services/customer-order-tracking.service';
import { OrderService } from '../../../core/services/order.service';
import { AuthService } from '../../../core/services/auth.service';
import { PaymentService } from '../../../core/services/payment.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { ToastService } from '../../../core/services/toast.service';
import { FulfillmentType } from '../../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../../shared/enums/payment-method.enum';
import { PaymentStatus } from '../../../shared/enums/payment-status.enum';
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
        CustomerOrderTrackingService,
        { provide: CheckoutService, useValue: checkoutMock },
        { provide: PaymentService, useValue: paymentMock },
        { provide: OrderService, useValue: orderMock },
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

  it('should register the order for tracking when payment releases it', () => {
    jest.useFakeTimers();
    orderMock.getOrder.mockReturnValue(of({ id: 'o1', status: OrderStatus.Received }));
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const trackOrderSpy = jest.spyOn(tracking, 'trackOrder');

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);

    expect(trackOrderSpy).toHaveBeenCalledWith('o1');
    jest.useRealTimers();
  });

  it('does not navigate after destroy when a pending payment resolves as paid', () => {
    const pending: ((payment: any) => void)[] = [];
    paymentMock.getPayment.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((p) => subscriber.next(p));
        }),
    );

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();
    fixture.destroy();

    pending[0]({
      paymentId: 'pay1',
      orderId: 'o1',
      status: PaymentStatus.Paid,
    });

    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('does not navigate after destroy when a pending order poll resolves as received', () => {
    jest.useFakeTimers();
    const pending: ((order: any) => void)[] = [];
    orderMock.getOrder.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((o) => subscriber.next(o));
        }),
    );

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();
    jest.advanceTimersByTime(4000);
    fixture.destroy();

    pending[0]({ status: OrderStatus.Received });

    expect(routerMock.navigate).not.toHaveBeenCalled();
    jest.useRealTimers();
  });

  it('navigates only once when payment and order polling resolve together', () => {
    jest.useFakeTimers();
    paymentMock.getPayment.mockReturnValue(of({
      paymentId: 'pay1',
      orderId: 'o1',
      status: PaymentStatus.Paid,
    }));
    orderMock.getOrder.mockReturnValue(of({ id: 'o1', status: OrderStatus.Received }));
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const trackOrderSpy = jest.spyOn(tracking, 'trackOrder');

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);

    expect(routerMock.navigate).toHaveBeenCalledTimes(1);
    expect(trackOrderSpy).toHaveBeenCalledTimes(1);
    jest.useRealTimers();
  });

  it('unsubscribes the previous payment load when refreshing', () => {
    let firstUnsubscribed = false;
    paymentMock.getPayment.mockReturnValue(
      new Observable(() => () => {
        firstUnsubscribed = true;
      }),
    );

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    paymentMock.getPayment.mockReturnValue(of({
      paymentId: 'pay1',
      orderId: 'o1',
      status: PaymentStatus.Pending,
    }));

    fixture.componentInstance.refreshPayment();

    expect(firstUnsubscribed).toBe(true);
  });

  it('does not overlap order polls while a poll is still in flight', () => {
    jest.useFakeTimers();
    let calls = 0;
    const pending: ((order: any) => void)[] = [];
    orderMock.getOrder.mockImplementation(
      () =>
        new Observable((subscriber) => {
          calls++;
          pending.push((order) => subscriber.next(order));
        }),
    );

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    jest.advanceTimersByTime(4000);
    jest.advanceTimersByTime(4000);

    expect(calls).toBe(1);

    pending[0]({ status: OrderStatus.PendingPayment });
    jest.advanceTimersByTime(4000);

    expect(calls).toBe(2);
    jest.useRealTimers();
  });

  it('cancels order polling when navigating to tracking after payment is paid', () => {
    jest.useFakeTimers();
    paymentMock.getPayment.mockReturnValue(of({
      paymentId: 'pay1',
      orderId: 'o1',
      status: PaymentStatus.Paid,
    }));
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'o1']);
    const callsAfterNavigation = orderMock.getOrder.mock.calls.length;

    jest.advanceTimersByTime(4000);

    expect(orderMock.getOrder).toHaveBeenCalledTimes(callsAfterNavigation);
    jest.useRealTimers();
  });
});

describe('OnlinePaymentPageComponent footer clearance', () => {
  it('keeps the scrollport clear of the fixed footer and sticky action bar', () => {
    const styles = readFileSync(resolve(__dirname, 'online-payment-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.screen-padding\s*\{[\s\S]*padding:\s*0 18px calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 56px\)/);
  });
});
