import { TestBed } from '@angular/core/testing';
import { OrderStatus } from '../../../../shared/enums/order-status.enum';
import { SellerOrderCardComponent } from './seller-order-card.component';

describe('SellerOrderCardComponent', () => {
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

  it('renders all valid backend transitions for a ready order', () => {
    const fixture = TestBed.createComponent(SellerOrderCardComponent);
    fixture.componentRef.setInput('order', {
      id: 'order1',
      code: '123',
      storeId: 'store1',
      status: OrderStatus.Ready,
      total: 42.5,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Saiu para entrega');
    expect(text).toContain('Entregue no balcão');
    expect(text).toContain('Cancelar');
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
