import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { EMPTY, Observable, of, throwError } from 'rxjs';
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
import { PaymentGateway } from '../../../shared/enums/payment-gateway.enum';
import { OrderStatus } from '../../../shared/enums/order-status.enum';

describe('OnlinePaymentPageComponent', () => {
  let checkoutMock: { [key: string]: any };
  let paymentMock: { getPayment: jest.Mock; createPayment: jest.Mock };
  let orderMock: { getOrder: jest.Mock };
  let routerMock: { navigate: jest.Mock; url: string };

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

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

  it('polls the order in the background so the overlay is not dimmed', () => {
    jest.useFakeTimers();
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);

    expect(orderMock.getOrder).toHaveBeenCalledWith('o1', { background: true });
    jest.useRealTimers();
  });

  it('keeps waiting and retries after a transient polling failure without a fatal state', () => {
    jest.useFakeTimers();
    let call = 0;
    orderMock.getOrder.mockImplementation(() => {
      call++;
      if (call === 1) {
        return throwError(
          () => new HttpErrorResponse({ status: 504, statusText: 'Gateway Timeout' }),
        );
      }
      return of({ status: OrderStatus.PendingPayment });
    });

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);

    expect(orderMock.getOrder).toHaveBeenCalledWith('o1', { background: true });
    expect(fixture.componentInstance.pollingUnstable()).toBe(false);
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(routerMock.navigate).not.toHaveBeenCalled();

    // The first retry keeps the normal cadence; the order is still pending.
    jest.advanceTimersByTime(4000);

    expect(orderMock.getOrder).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(routerMock.navigate).not.toHaveBeenCalled();

    jest.useRealTimers();
  });

  it('shows an informative reconnecting state after repeated failures and clears it on recovery', () => {
    jest.useFakeTimers();
    let call = 0;
    orderMock.getOrder.mockImplementation(() => {
      call++;
      if (call <= 2) {
        return throwError(
          () => new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }),
        );
      }
      return of({ status: OrderStatus.PendingPayment });
    });

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000); // first failure
    expect(fixture.componentInstance.pollingUnstable()).toBe(false);

    jest.advanceTimersByTime(4000); // second failure -> unstable, backoff 8s
    expect(fixture.componentInstance.pollingUnstable()).toBe(true);
    expect(fixture.componentInstance.expired()).toBe(false);

    jest.advanceTimersByTime(4000); // still inside backoff
    expect(orderMock.getOrder).toHaveBeenCalledTimes(2);

    jest.advanceTimersByTime(4000); // recovered
    expect(orderMock.getOrder).toHaveBeenCalledTimes(3);
    expect(fixture.componentInstance.pollingUnstable()).toBe(false);
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(routerMock.navigate).not.toHaveBeenCalled();

    jest.useRealTimers();
  });

  it('stops polling and shows the unavailable state when the order poll returns 404', () => {
    jest.useFakeTimers();
    orderMock.getOrder.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' })),
    );

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    fixture.detectChanges();

    expect(fixture.componentInstance.orderUnavailable()).toBe(true);
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(routerMock.navigate).not.toHaveBeenCalled();
    expect(fixture.debugElement.query(By.css('#order-unavailable-title'))).not.toBeNull();
    expect(fixture.debugElement.nativeElement.textContent).toContain('VOLTAR AO CARDÁPIO');
    expect(fixture.componentInstance.stickyLabel()).toBe('Tentar novamente');

    // Terminal: no endless retry and no confirmation while the order is unknown.
    jest.advanceTimersByTime(30_000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(routerMock.navigate).not.toHaveBeenCalled();

    jest.useRealTimers();
  });

  it.each([
    [400, 'Não foi possível consultar este pedido. Volte ao cardápio e tente novamente.'],
    [403, 'Você não tem permissão para acompanhar este pedido.'],
    [409, 'Este pedido foi alterado e não pode mais ser acompanhado nesta tela. Atualize a página ou volte ao cardápio.'],
  ])('stops polling and shows a non-transient error for %s', (status, message) => {
    jest.useFakeTimers();
    orderMock.getOrder.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status, statusText: 'Error' })),
    );

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    fixture.detectChanges();

    expect(fixture.componentInstance.orderUnavailable()).toBe(false);
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.componentInstance.errorMessage()).toBe(message);
    expect(fixture.debugElement.nativeElement.textContent).toContain(message);
    expect(routerMock.navigate).not.toHaveBeenCalled();

    jest.advanceTimersByTime(30_000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(1);
    expect(routerMock.navigate).not.toHaveBeenCalled();

    jest.useRealTimers();
  });

  it('resets the reconnecting state and backoff when loadPayment succeeds', () => {
    jest.useFakeTimers();
    let orderCall = 0;
    orderMock.getOrder.mockImplementation(() => {
      orderCall++;
      if (orderCall <= 2) {
        return throwError(
          () => new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }),
        );
      }
      return of({ status: OrderStatus.PendingPayment });
    });

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000); // first failure -> 4s
    jest.advanceTimersByTime(4000); // second failure -> unstable, 8s backoff
    expect(fixture.componentInstance.pollingUnstable()).toBe(true);

    fixture.componentInstance.refreshPayment();
    fixture.detectChanges();

    expect(fixture.componentInstance.pollingUnstable()).toBe(false);

    // The old 8s timer is discarded and polling is re-armed at the normal 4s.
    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(3);
    expect(fixture.componentInstance.pollingUnstable()).toBe(false);

    jest.useRealTimers();
  });

  it('releases the in-flight guard when the poll completes without a value', () => {
    jest.useFakeTimers();
    let orderCall = 0;
    orderMock.getOrder.mockImplementation(() => {
      orderCall++;
      if (orderCall === 1) {
        return EMPTY;
      }
      return of({ status: OrderStatus.PendingPayment });
    });

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(1);

    fixture.componentInstance.refreshPayment();
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(2);
    expect(routerMock.navigate).not.toHaveBeenCalled();

    jest.useRealTimers();
  });

  it('keeps waiting when the server returns an unknown order status', () => {
    jest.useFakeTimers();
    orderMock.getOrder.mockReturnValue(of({ status: 99 as OrderStatus }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);

    expect(routerMock.navigate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.componentInstance.errorMessage()).toBeNull();

    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(2);
    expect(routerMock.navigate).not.toHaveBeenCalled();

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

  it('does not create duplicate order poll intervals when loadPayment re-arms polling', () => {
    jest.useFakeTimers();
    paymentMock.getPayment.mockReturnValue(of(mockPendingPayment(new Date(Date.now() + 60000).toISOString())));
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder).toHaveBeenCalledTimes(2);

    jest.useRealTimers();
  });

  it('re-arms order polling when refreshPayment reloads a valid pending attempt', () => {
    jest.useFakeTimers();
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      return of(getCall === 1
        ? { ...mockPendingPayment('2026-07-29T00:00:00.000Z'), status: PaymentStatus.Failed }
        : mockPendingPayment(new Date(Date.now() + 60000).toISOString()));
    });
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(true);
    const callsBeforeRefresh = orderMock.getOrder.mock.calls.length;

    fixture.componentInstance.refreshPayment();
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder.mock.calls.length).toBeGreaterThan(callsBeforeRefresh);

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

  const mockPendingPayment = (expiresAtUtc: string) => ({
    paymentId: 'pay1',
    orderId: 'o1',
    gateway: PaymentGateway.Mock,
    gatewayTransactionId: 'mock_o1_1',
    gatewayCheckoutUrl: '/mock-pix-checkout',
    method: PaymentMethod.PixOnline,
    status: PaymentStatus.Pending,
    amount: 20,
    createdAtUtc: '2026-07-29T00:00:00.000Z',
    expiresAtUtc,
  });

  const mockMercadoPagoPending = () => ({
    paymentId: 'pay1',
    orderId: 'o1',
    gateway: PaymentGateway.MercadoPago,
    gatewayTransactionId: 'mp_pending',
    gatewayCheckoutUrl: 'https://pay.example/pix',
    method: PaymentMethod.PixOnline,
    status: PaymentStatus.Pending,
    amount: 20,
    createdAtUtc: '2026-07-29T00:00:00.000Z',
  });

  it('should render a server-driven countdown starting at 01:00 for a mock Pix payment', () => {
    jest.useFakeTimers();
    paymentMock.getPayment.mockReturnValue(of(mockPendingPayment(new Date(Date.now() + 60000).toISOString())));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    const countdownValue = fixture.debugElement.query(By.css('.countdown-value'));
    expect(countdownValue.nativeElement.textContent).toBe('01:00');

    jest.advanceTimersByTime(1000);
    fixture.detectChanges();
    expect(fixture.componentInstance.countdownLabel()).toBe('00:59');

    jest.useRealTimers();
  });

  it('should not expose a gateway link for a mock Pix payment', () => {
    paymentMock.getPayment.mockReturnValue(of(mockPendingPayment(new Date(Date.now() + 60000).toISOString())));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('a.pix-link'))).toBeNull();
    expect(fixture.debugElement.nativeElement.textContent).toContain('Pix simulado');
  });

  it('marks expired and stops polling after a final server confirmation at zero', () => {
    jest.useFakeTimers();
    paymentMock.getPayment.mockReturnValue(of(mockPendingPayment(new Date(Date.now() + 5000).toISOString())));
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(4000);
    expect(orderMock.getOrder).toHaveBeenCalled();
    const orderCallsAtExpiry = orderMock.getOrder.mock.calls.length;
    const paymentCallsAtExpiry = paymentMock.getPayment.mock.calls.length;

    jest.advanceTimersByTime(2000);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(true);
    expect(fixture.debugElement.query(By.css('.expired-card'))).not.toBeNull();
    expect(fixture.debugElement.nativeElement.textContent).toContain('Tempo para pagamento encerrado');
    expect(paymentMock.getPayment.mock.calls.length).toBe(paymentCallsAtExpiry + 1);
    expect(paymentMock.getPayment).toHaveBeenLastCalledWith('o1', { background: true });

    jest.advanceTimersByTime(8000);
    expect(orderMock.getOrder.mock.calls.length).toBe(orderCallsAtExpiry);

    jest.useRealTimers();
  });

  it('navigates to tracking when the final server confirmation returns paid at zero', () => {
    jest.useFakeTimers();
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      return of(getCall === 1
        ? mockPendingPayment(new Date(Date.now() + 5000).toISOString())
        : { ...mockPendingPayment(new Date(Date.now() - 1000).toISOString()), status: PaymentStatus.Paid });
    });
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(5000);
    fixture.detectChanges();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'o1']);
    jest.useRealTimers();
  });

  it('confirms with the server and expires when loading an already-elapsed deadline', () => {
    jest.useFakeTimers();
    paymentMock.getPayment.mockReturnValue(of(mockPendingPayment(new Date(Date.now() - 1000).toISOString())));
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(true);
    expect(fixture.componentInstance.remainingSeconds()).toBe(0);
    expect(fixture.debugElement.query(By.css('.expired-card'))).not.toBeNull();
    expect(paymentMock.getPayment).toHaveBeenCalledTimes(2);

    jest.advanceTimersByTime(8000);
    expect(orderMock.getOrder).not.toHaveBeenCalled();

    jest.useRealTimers();
  });

  it('does not loop or re-query when the final server confirmation errors', () => {
    jest.useFakeTimers();
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      if (getCall === 1) {
        return of(mockPendingPayment(new Date(Date.now() + 5000).toISOString()));
      }
      return throwError(() => new Error('network'));
    });
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(6000);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(true);
    expect(paymentMock.getPayment).toHaveBeenCalledTimes(2);

    const orderCallsAtExpiry = orderMock.getOrder.mock.calls.length;
    jest.advanceTimersByTime(8000);
    expect(paymentMock.getPayment).toHaveBeenCalledTimes(2);
    expect(orderMock.getOrder.mock.calls.length).toBe(orderCallsAtExpiry);

    jest.useRealTimers();
  });

  it('should show the expired state when the server reports a failed mock payment', () => {
    paymentMock.getPayment.mockReturnValue(of({
      ...mockPendingPayment('2026-07-29T00:00:00.000Z'),
      status: PaymentStatus.Failed,
    }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(true);
    expect(fixture.debugElement.query(By.css('.expired-card'))).not.toBeNull();
  });

  it('should not treat a Mercado Pago failed payment as expired/retryable mock Pix', () => {
    paymentMock.getPayment.mockReturnValue(of({
      ...mockPendingPayment('2026-07-29T00:00:00.000Z'),
      gateway: PaymentGateway.MercadoPago,
      gatewayCheckoutUrl: 'https://pay.example/pix',
      status: PaymentStatus.Failed,
    }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.debugElement.query(By.css('.expired-card'))).toBeNull();
    expect(fixture.debugElement.query(By.css('a.pix-link'))).not.toBeNull();
    expect(fixture.debugElement.nativeElement.textContent).not.toContain('Tempo para pagamento encerrado');
  });

  it('clears the expired mock state when a later load returns a pending Mercado Pago payment', () => {
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      return of(getCall === 1
        ? { ...mockPendingPayment('2026-07-29T00:00:00.000Z'), status: PaymentStatus.Failed }
        : mockMercadoPagoPending());
    });
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(true);

    fixture.componentInstance.refreshPayment();
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.componentInstance.remainingSeconds()).toBe(0);
    expect(fixture.debugElement.query(By.css('.expired-card'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.countdown-value'))).toBeNull();
    expect(fixture.debugElement.query(By.css('a.pix-link'))).not.toBeNull();
  });

  it('stops the mock countdown timer when a later load returns a pending Mercado Pago payment', () => {
    jest.useFakeTimers();
    const mockDeadline = new Date(Date.now() + 5000).toISOString();
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      return of(getCall === 1
        ? mockPendingPayment(mockDeadline)
        : mockMercadoPagoPending());
    });
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.remainingSeconds()).toBe(5);
    expect(fixture.componentInstance.expired()).toBe(false);

    fixture.componentInstance.refreshPayment();
    fixture.detectChanges();

    expect(fixture.componentInstance.remainingSeconds()).toBe(0);
    expect(fixture.componentInstance.expired()).toBe(false);

    jest.advanceTimersByTime(10000);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.debugElement.query(By.css('.expired-card'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.countdown-value'))).toBeNull();

    jest.useRealTimers();
  });

  it('ignores a stale confirmation response when refreshPayment runs during a pending confirmation', () => {
    jest.useFakeTimers();
    const confirmSubscribers: ((payment: any) => void)[] = [];
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      if (getCall === 1) {
        return of(mockPendingPayment(new Date(Date.now() + 5000).toISOString()));
      }
      if (getCall === 2) {
        return new Observable((subscriber) => {
          confirmSubscribers.push((p) => subscriber.next(p));
        });
      }
      return of(mockMercadoPagoPending());
    });
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(6000);
    fixture.detectChanges();

    expect(paymentMock.getPayment).toHaveBeenCalledTimes(2);

    fixture.componentInstance.refreshPayment();
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(false);

    confirmSubscribers[0]({
      ...mockPendingPayment(new Date(Date.now() - 1000).toISOString()),
      status: PaymentStatus.Failed,
    });
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.debugElement.query(By.css('.expired-card'))).toBeNull();

    jest.useRealTimers();
  });

  it('should retry the mock payment without recreating the order', () => {
    jest.useFakeTimers();
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      return of(getCall === 1
        ? { ...mockPendingPayment('2026-07-29T00:00:00.000Z'), status: PaymentStatus.Failed }
        : mockPendingPayment(new Date(Date.now() + 60000).toISOString()));
    });
    paymentMock.createPayment.mockReturnValue(of({}));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.expired()).toBe(true);

    fixture.componentInstance.retryPayment();

    expect(paymentMock.createPayment).toHaveBeenCalledWith('o1');
    expect(fixture.componentInstance.expired()).toBe(false);

    jest.useRealTimers();
  });

  it('should request a fresh attempt when retrying right after the countdown reaches zero', () => {
    jest.useFakeTimers();
    let getCall = 0;
    paymentMock.getPayment.mockImplementation(() => {
      getCall++;
      return of(getCall === 1
        ? mockPendingPayment(new Date(Date.now() + 1000).toISOString())
        : mockPendingPayment(new Date(Date.now() + 60000).toISOString()));
    });
    orderMock.getOrder.mockReturnValue(of({ status: OrderStatus.PendingPayment }));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    jest.advanceTimersByTime(2000);
    fixture.detectChanges();
    expect(fixture.componentInstance.expired()).toBe(true);

    paymentMock.createPayment.mockReturnValue(of(mockPendingPayment(new Date(Date.now() + 60000).toISOString())));

    fixture.componentInstance.retryPayment();
    fixture.detectChanges();

    expect(paymentMock.createPayment).toHaveBeenCalledWith('o1');
    expect(fixture.componentInstance.expired()).toBe(false);
    expect(fixture.componentInstance.remainingSeconds()).toBe(60);

    jest.useRealTimers();
  });

  it('should stop the countdown when the component is destroyed', () => {
    jest.useFakeTimers();
    paymentMock.getPayment.mockReturnValue(of(mockPendingPayment(new Date(Date.now() + 60000).toISOString())));

    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.remainingSeconds()).toBe(60);

    fixture.destroy();
    jest.advanceTimersByTime(3000);

    expect(fixture.componentInstance.remainingSeconds()).toBe(60);
    jest.useRealTimers();
  });

  it('renders the Continuar bar as a primary in-flow bar inside the scroll content', () => {
    const fixture = TestBed.createComponent(OnlinePaymentPageComponent);
    fixture.detectChanges();

    const bar = fixture.debugElement.query(By.css('app-sticky-action-bar'));
    expect(bar).not.toBeNull();

    const host = bar.nativeElement as HTMLElement;
    expect(host.classList.contains('sticky-action-primary')).toBe(true);
    expect(host.classList.contains('in-flow')).toBe(true);
    // Footer-less payment route: the bar owns the device inset.
    expect(host.classList.contains('in-flow-safe-area')).toBe(true);

    const content = fixture.debugElement.query(By.css('ion-content')).nativeElement as HTMLElement;
    const section = fixture.debugElement.query(By.css('.screen-padding')).nativeElement as HTMLElement;
    expect(content.contains(host)).toBe(true);
    expect(section.contains(host)).toBe(false);
    expect(section.compareDocumentPosition(host) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    const button = bar.query(By.css('button.sticky-action')).nativeElement as HTMLButtonElement;
    expect(button.textContent).toContain('Continuar');
    expect(button.disabled).toBe(false);
  });
});

describe('OnlinePaymentPageComponent payment action layout', () => {
  it('declares the primary inline bar with the existing status action', () => {
    const template = readFileSync(resolve(__dirname, 'online-payment-page.component.html'), 'utf8');

    expect(template).toMatch(
      /<app-sticky-action-bar[\s\S]*?placement="inline"[\s\S]*?\[safeArea\]="true"[\s\S]*?appearance="primary"[\s\S]*?\[label\]="stickyLabel\(\)"[\s\S]*?\[disabled\]="loading\(\)"[\s\S]*?\(action\)="onStickyAction\(\)"/,
    );

    const barIndex = template.indexOf('<app-sticky-action-bar');
    expect(barIndex).toBeGreaterThan(template.indexOf('</section>'));
    expect(barIndex).toBeLessThan(template.indexOf('</ion-content>'));
  });

  it('bounds the route column and removes the legacy fixed-footer clearance', () => {
    const styles = readFileSync(resolve(__dirname, 'online-payment-page.component.scss'), 'utf8');

    expect(styles).toMatch(
      /:host\s*\{[\s\S]*display:\s*flex;[\s\S]*flex-direction:\s*column;[\s\S]*flex:\s*1 1 0;[\s\S]*min-height:\s*0;/,
    );
    expect(styles).toMatch(/\.pay-content\s*\{[\s\S]*min-height:\s*0;/);
    expect(styles).toMatch(/\.screen-padding\s*\{[\s\S]*padding:\s*0 18px 24px;/);
    expect(styles).not.toMatch(/--store-footer-clearance/);
    expect(styles).not.toMatch(/\.sticky-action\s*\{/);
  });
});
