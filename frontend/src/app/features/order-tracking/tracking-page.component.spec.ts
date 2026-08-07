import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';

import { TrackingPageComponent } from './tracking-page.component';
import { CartService } from '../../core/services/cart.service';
import { OrderService } from '../../core/services/order.service';
import { SignalRService } from '../../core/services/signalr.service';
import { StoreContextService } from '../../core/services/store-context.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { OrderDetails } from '../../shared/models/order.model';

describe('TrackingPageComponent', () => {
  const order: OrderDetails = {
    id: 'order1',
    code: 'ABC123',
    storeId: 'store1',
    fulfillmentType: FulfillmentType.Delivery,
    status: OrderStatus.Preparing,
    paymentMethod: PaymentMethod.CardOnDelivery,
    subtotal: 20,
    deliveryFee: 6.99,
    total: 26.99,
    createdAtUtc: '2026-07-28T12:00:00Z',
    addressStreet: 'Rua A',
    addressNumber: '123',
    addressNeighborhood: 'Centro',
    addressCity: 'Campos',
    addressState: 'RJ',
    items: [{ productName: 'X-burguer', quantity: 1, unitPrice: 20, totalPrice: 20 }],
    history: [{ createdAtUtc: '2026-07-28T12:05:00Z', previousStatus: OrderStatus.Received, newStatus: OrderStatus.Preparing }],
  };

  beforeEach(async () => {
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [TrackingPageComponent],
      providers: [
        CartService,
        { provide: OrderService, useValue: { getOrder: jest.fn().mockReturnValue(of(order)) } },
        { provide: SignalRService, useValue: { startCustomerHub: jest.fn().mockResolvedValue(undefined), onCustomerEvent: jest.fn(), removeCustomerListener: jest.fn(), stopCustomerHub: jest.fn() } },
        { provide: StoreContextService, useValue: { phoneNumber: signal('22999999999'), storeName: signal('Loja Teste') } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: jest.fn().mockReturnValue('order1') } } } },
        { provide: Router, useValue: { navigate: jest.fn(), url: '/loja/pedido/order1' } },
      ],
    }).compileComponents();
  });

  it('should expose tracking steps as a list with the current step marked', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const list = fixture.debugElement.query(By.css('.track-line[role="list"]'));
    const current = fixture.debugElement.query(By.css('.track-step[aria-current="step"]'));

    expect(list).not.toBeNull();
    expect(current.nativeElement.textContent).toContain('Preparando seu pedido');
  });

  it('should render help action as a button instead of a link without href', () => {
    const fixture = TestBed.createComponent(TrackingPageComponent);
    fixture.detectChanges();

    const helpButton = fixture.debugElement.query(By.css('button.help-card'));
    const helpAnchor = fixture.debugElement.query(By.css('a.help-card'));

    expect(helpButton).not.toBeNull();
    expect(helpButton.nativeElement.type).toBe('button');
    expect(helpAnchor).toBeNull();
  });
});
