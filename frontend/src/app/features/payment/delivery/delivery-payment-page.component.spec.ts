import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { of } from 'rxjs';

import { DeliveryPaymentPageComponent } from './delivery-payment-page.component';
import { CartService } from '../../../core/services/cart.service';
import { CheckoutService } from '../../../core/services/checkout.service';
import { ToastService } from '../../../core/services/toast.service';
import { FulfillmentType } from '../../../shared/enums/fulfillment-type.enum';

describe('DeliveryPaymentPageComponent', () => {
  beforeEach(async () => {
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [DeliveryPaymentPageComponent],
      providers: [
        CartService,
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
});
