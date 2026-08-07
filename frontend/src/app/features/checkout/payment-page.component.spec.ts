import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { of } from 'rxjs';

import { PaymentPageComponent } from './payment-page.component';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { PaymentService } from '../../core/services/payment.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';

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
        { provide: CheckoutService, useValue: checkoutMock },
        { provide: PaymentService, useValue: paymentMock },
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

  it('should mark the details sheet as a modal dialog', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.componentInstance.showDetailsModal.set(true);
    fixture.detectChanges();

    const modal = fixture.debugElement.query(By.css('.modal-sheet')).nativeElement as HTMLElement;

    expect(modal.getAttribute('role')).toBe('dialog');
    expect(modal.getAttribute('aria-modal')).toBe('true');
    expect(modal.getAttribute('aria-labelledby')).toBe('details-title');
  });
});
