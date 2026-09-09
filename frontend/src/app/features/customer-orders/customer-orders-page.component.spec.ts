import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { CustomerOrdersPageComponent } from './customer-orders-page.component';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { StoreContextService } from '../../core/services/store-context.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { OrderDetails } from '../../shared/models/order.model';

describe('CustomerOrdersPageComponent', () => {
  const baseOrder: OrderDetails = {
    id: 'order1',
    code: 'URB-123',
    storeId: 'store1',
    fulfillmentType: FulfillmentType.Delivery,
    status: OrderStatus.Preparing,
    paymentMethod: PaymentMethod.CardOnDelivery,
    subtotal: 20,
    deliveryFee: 6.99,
    total: 26.99,
    createdAtUtc: '2026-07-28T12:00:00Z',
    items: [
      { productName: 'X-burguer', quantity: 2, unitPrice: 20, totalPrice: 40 },
      { productName: 'Refri', quantity: 1, unitPrice: 8, totalPrice: 8 },
    ],
    history: [],
  };

  let activeOrders: ReturnType<typeof signal<OrderDetails[]>>;
  let finishedOrders: ReturnType<typeof signal<OrderDetails[]>>;
  let routerMock: { navigate: jest.Mock; url: string };

  beforeEach(async () => {
    activeOrders = signal([]);
    finishedOrders = signal([]);
    routerMock = { navigate: jest.fn(), url: '/loja/pedidos' };

    await TestBed.configureTestingModule({
      imports: [CustomerOrdersPageComponent],
      providers: [
        { provide: CustomerOrderTrackingService, useValue: { activeOrders, finishedOrders } },
        { provide: StoreContextService, useValue: { storeId: signal('store1') } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: jest.fn().mockReturnValue('loja') } },
            paramMap: of({ get: () => 'loja' }),
          },
        },
        { provide: Router, useValue: routerMock },
      ],
    }).compileComponents();
  });

  it('lists multiple active orders and navigates to the selected order', () => {
    activeOrders.set([
      baseOrder,
      { ...baseOrder, id: 'order2', code: 'URB-456', createdAtUtc: '2026-07-29T12:00:00Z', status: OrderStatus.Ready },
    ]);

    const fixture = TestBed.createComponent(CustomerOrdersPageComponent);
    fixture.detectChanges();

    const cards = fixture.debugElement.queryAll(By.css('.order-card'));
    expect(cards.length).toBe(2);

    expect(cards[0].query(By.css('.order-code')).nativeElement.textContent).toContain('URB-456');
    expect(cards[1].query(By.css('.order-code')).nativeElement.textContent).toContain('URB-123');

    cards[0].nativeElement.click();
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'order2']);

    fixture.destroy();
  });

  it('renders order code, status, item count, total and date for each order', () => {
    activeOrders.set([baseOrder]);

    const fixture = TestBed.createComponent(CustomerOrdersPageComponent);
    fixture.detectChanges();

    const card = fixture.debugElement.query(By.css('.order-card'));
    expect(card.query(By.css('.order-code')).nativeElement.textContent).toContain('URB-123');
    expect(card.query(By.css('.order-status')).nativeElement.textContent).toContain('Preparando');
    expect(card.query(By.css('.order-meta')).nativeElement.textContent).toContain('3 itens');
    expect(card.query(By.css('.order-total')).nativeElement.textContent).toContain('26,99');
    expect(card.query(By.css('.order-date')).nativeElement.textContent).toContain('28/07/2026');

    fixture.destroy();
  });

  it('orders active orders by most recent first', () => {
    activeOrders.set([
      { ...baseOrder, id: 'order-old', code: 'URB-OLD', createdAtUtc: '2026-07-27T12:00:00Z' },
      { ...baseOrder, id: 'order-new', code: 'URB-NEW', createdAtUtc: '2026-07-29T12:00:00Z' },
      { ...baseOrder, id: 'order-mid', code: 'URB-MID', createdAtUtc: '2026-07-28T12:00:00Z' },
    ]);

    const fixture = TestBed.createComponent(CustomerOrdersPageComponent);
    fixture.detectChanges();

    const codes = fixture.debugElement
      .queryAll(By.css('.order-code'))
      .map((el) => el.nativeElement.textContent.trim());

    expect(codes).toEqual(['Pedido #URB-NEW', 'Pedido #URB-MID', 'Pedido #URB-OLD']);

    fixture.destroy();
  });

  it('filters out active orders from another store', () => {
    activeOrders.set([
      baseOrder,
      { ...baseOrder, id: 'order-other', code: 'URB-OTHER', storeId: 'store-other' },
    ]);

    const fixture = TestBed.createComponent(CustomerOrdersPageComponent);
    fixture.detectChanges();

    const cards = fixture.debugElement.queryAll(By.css('.order-card'));
    expect(cards.length).toBe(1);
    expect(cards[0].query(By.css('.order-code')).nativeElement.textContent).toContain('URB-123');

    fixture.destroy();
  });

  it('renders an empty state with a menu action', () => {
    activeOrders.set([]);

    const fixture = TestBed.createComponent(CustomerOrdersPageComponent);
    fixture.detectChanges();

    const empty = fixture.debugElement.query(By.css('app-empty-state'));
    expect(empty).not.toBeNull();

    const action = fixture.debugElement.query(By.css('app-empty-state .btn-primary'));
    action.nativeElement.click();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);

    fixture.destroy();
  });

  it('keeps the footer clearance inside the scrolling content', () => {
    const styles = readFileSync(resolve(__dirname, 'customer-orders-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.screen-padding\s*\{[\s\S]*padding:\s*0 18px calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 20px\)/);
  });

  it('shows finished orders separately and navigates to a finished order', () => {
    activeOrders.set([]);
    finishedOrders.set([
      {
        ...baseOrder,
        id: 'done1',
        code: 'URB-DONE',
        status: OrderStatus.Delivered,
        createdAtUtc: '2026-07-25T12:00:00Z',
      },
    ]);

    const fixture = TestBed.createComponent(CustomerOrdersPageComponent);
    fixture.detectChanges();

    const historyList = fixture.debugElement.query(By.css('[aria-label="Histórico de pedidos"]'));
    expect(historyList).not.toBeNull();

    const doneCard = historyList.query(By.css('.order-card'));
    expect(doneCard.query(By.css('.order-code')).nativeElement.textContent).toContain('URB-DONE');
    expect(doneCard.query(By.css('.order-status')).nativeElement.textContent).toContain('Entregue');

    doneCard.nativeElement.click();
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'done1']);

    fixture.destroy();
  });

  it('filters finished orders by store and sorts them most recent first', () => {
    activeOrders.set([]);
    finishedOrders.set([
      { ...baseOrder, id: 'old', code: 'URB-OLD', status: OrderStatus.Cancelled, createdAtUtc: '2026-07-24T12:00:00Z' },
      { ...baseOrder, id: 'new', code: 'URB-NEW', status: OrderStatus.Delivered, createdAtUtc: '2026-07-26T12:00:00Z' },
      { ...baseOrder, id: 'other', code: 'URB-OTHER', status: OrderStatus.Delivered, storeId: 'store-other', createdAtUtc: '2026-07-27T12:00:00Z' },
    ]);

    const fixture = TestBed.createComponent(CustomerOrdersPageComponent);
    fixture.detectChanges();

    const historyList = fixture.debugElement.query(By.css('[aria-label="Histórico de pedidos"]'));
    const codes = historyList
      .queryAll(By.css('.order-code'))
      .map((el) => el.nativeElement.textContent.trim());

    expect(codes).toEqual(['Pedido #URB-NEW', 'Pedido #URB-OLD']);

    fixture.destroy();
  });
});
