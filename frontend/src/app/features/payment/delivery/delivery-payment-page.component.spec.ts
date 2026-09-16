import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { DeliveryPaymentPageComponent } from './delivery-payment-page.component';
import { CartService } from '../../../core/services/cart.service';
import { CheckoutService } from '../../../core/services/checkout.service';
import { CustomerOrderTrackingService } from '../../../core/services/customer-order-tracking.service';
import { OrderService } from '../../../core/services/order.service';
import { AuthService } from '../../../core/services/auth.service';
import { SignalRService } from '../../../core/services/signalr.service';
import { ToastService } from '../../../core/services/toast.service';
import { FulfillmentType } from '../../../shared/enums/fulfillment-type.enum';

describe('DeliveryPaymentPageComponent', () => {
  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [DeliveryPaymentPageComponent],
      providers: [
        CartService,
        CustomerOrderTrackingService,
        {
          provide: CheckoutService,
          useValue: {
            fulfillmentType: signal(FulfillmentType.Delivery),
            customerAddressId: signal('addr1'),
            lastOrderId: signal(null),
            lastOrderCode: signal(null),
            confirm: jest.fn().mockReturnValue(of({ orderId: 'o1', code: 'ABC123' })),
          },
        },
        { provide: OrderService, useValue: { getOrder: jest.fn().mockReturnValue(of({ id: 'o1', status: 3 })) } },
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
        { provide: ToastService, useValue: { showSuccess: jest.fn(), showError: jest.fn() } },
        { provide: Router, useValue: { navigate: jest.fn(), url: '/loja/checkout/entrega' } },
        { provide: Location, useValue: { back: jest.fn() } },
      ],
    }).compileComponents();

    const cart = TestBed.inject(CartService);
    cart.storeId.set('store1');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
  });

  it('should render delivery payment methods as native radio inputs', () => {
    const fixture = TestBed.createComponent(DeliveryPaymentPageComponent);
    fixture.detectChanges();

    const radios = fixture.debugElement.queryAll(By.css('input[type="radio"][name="delivery-payment-method"]'));

    expect(radios).toHaveLength(2);
    expect(radios.map(radio => radio.nativeElement.value)).toEqual(['cash', 'card']);
  });

  it('should render cash change choice as native radio inputs', () => {
    const fixture = TestBed.createComponent(DeliveryPaymentPageComponent);
    fixture.componentInstance.select('cash');
    fixture.detectChanges();

    const radios = fixture.debugElement.queryAll(By.css('input[type="radio"][name="needs-change"]'));

    expect(radios).toHaveLength(2);
    expect(radios.map(radio => radio.nativeElement.value)).toEqual(['no', 'yes']);
  });

  it('should register the confirmed order for tracking', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const trackOrderSpy = jest.spyOn(tracking, 'trackOrder');

    const fixture = TestBed.createComponent(DeliveryPaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.finalize();

    expect(trackOrderSpy).toHaveBeenCalledWith('o1');
  });

  it('does not navigate after destroy when the order confirms', () => {
    const checkout = TestBed.inject(CheckoutService) as unknown as { confirm: jest.Mock };
    const router = TestBed.inject(Router) as unknown as { navigate: jest.Mock };
    const pending: ((order: any) => void)[] = [];
    checkout.confirm.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const fixture = TestBed.createComponent(DeliveryPaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.finalize();
    fixture.destroy();

    pending[0]({ orderId: 'o1', code: 'ABC123' });

    expect(router.navigate).not.toHaveBeenCalled();
  });
});

describe('DeliveryPaymentPageComponent footer clearance', () => {
  it('keeps the scrollport clear of the fixed footer and sticky action bar', () => {
    const styles = readFileSync(resolve(__dirname, 'delivery-payment-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.screen-padding\s*\{[\s\S]*padding:\s*0 18px calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 56px\)/);
  });
});
