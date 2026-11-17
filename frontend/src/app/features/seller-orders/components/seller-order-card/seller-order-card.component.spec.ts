import { TestBed } from '@angular/core/testing';
import { FulfillmentType } from '../../../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../../../shared/enums/order-status.enum';
import { SellerOrderCardComponent } from './seller-order-card.component';

describe('SellerOrderCardComponent', () => {
  it('labels a ready pickup order as ready for pickup', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);

    expect(fixture.componentInstance.statusLabel(OrderStatus.Ready, FulfillmentType.PickUp)).toBe('Pronto para retirada');
    expect(fixture.componentInstance.statusLabel(OrderStatus.Ready, FulfillmentType.Delivery)).toBe('Pronto');
  });

  it('renders order code, total and status action', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'order1',
      code: '123',
      storeId: 'store1',
      status: OrderStatus.Received,
      total: 42.5,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('#123');
    expect(fixture.nativeElement.textContent).toContain('R$');
    expect(fixture.nativeElement.textContent).toContain('Aceitar pedido');
  });

  it('emits the next status when action is clicked', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'order1',
      code: '123',
      storeId: 'store1',
      status: OrderStatus.Received,
      total: 42.5,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    jest.spyOn(fixture.componentInstance.advance, 'emit');
    fixture.detectChanges();

    fixture.nativeElement.querySelector('button').click();

    expect(fixture.componentInstance.advance.emit).toHaveBeenCalledWith({
      orderId: 'order1',
      nextStatus: OrderStatus.Preparing,
    });
  });

  it('emits selected order when the card is clicked', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'order1',
      code: '123',
      storeId: 'store1',
      status: OrderStatus.Received,
      total: 42.5,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    jest.spyOn(fixture.componentInstance.select, 'emit');
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.order-card').click();

    expect(fixture.componentInstance.select.emit).toHaveBeenCalledWith('order1');
  });

  it('does not expose drag-and-drop as a status change affordance', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'order1',
      code: '123',
      storeId: 'store1',
      status: OrderStatus.Received,
      total: 42.5,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('.order-card') as HTMLElement;

    expect(card.getAttribute('draggable')).not.toBe('true');
  });

  it('renders all valid backend transitions for a ready delivery order', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'order1',
      code: '123',
      storeId: 'store1',
      status: OrderStatus.Ready,
      fulfillmentType: FulfillmentType.Delivery,
      total: 42.5,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Saiu para entrega');
    expect(text).toContain('Cancelar');
    // The backend requires a motoboy to start the delivery, so a Ready -> Delivered
    // shortcut would be rejected and must never be offered.
    expect(text).not.toContain('Entregue no balcão');
  });

  it('marks a pickup order as personal pickup and never offers delivery transitions', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'pickup1',
      code: 'PU-1',
      storeId: 'store1',
      status: OrderStatus.Ready,
      fulfillmentType: FulfillmentType.PickUp,
      total: 30,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Retirada pessoalmente');
    expect(text).toContain('Pronto para retirada');
    expect(text).not.toContain('Saiu para entrega');
    expect(text).not.toContain('Entregue no balcão');
    expect(fixture.componentInstance.statusActions(OrderStatus.Ready)).toEqual([]);
  });

  it('disables actions for the order currently being updated', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'order1',
      code: '123',
      storeId: 'store1',
      status: OrderStatus.Received,
      total: 42.5,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    fixture.componentRef.setInput('updatingOrderId', 'order1');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button').disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Atualizando');
  });
});
