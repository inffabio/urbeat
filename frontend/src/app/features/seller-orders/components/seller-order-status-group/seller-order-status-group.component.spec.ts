import { TestBed } from '@angular/core/testing';
import { OrderStatus } from '../../../../shared/enums/order-status.enum';
import { SellerOrderStatusGroupComponent } from './seller-order-status-group.component';

describe('SellerOrderStatusGroupComponent', () => {
  it('renders group title and order count', () => {
    const fixture = TestBed.createComponent(SellerOrderStatusGroupComponent);
    fixture.componentRef.setInput('title', 'Recebidos');
    fixture.componentRef.setInput('orders', [
      {
        id: 'order1',
        code: '123',
        storeId: 'store1',
        status: OrderStatus.Received,
        total: 42.5,
        createdAtUtc: '2026-07-29T10:00:00Z',
      },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Recebidos');
    expect(fixture.nativeElement.textContent).toContain('1');
    expect(fixture.nativeElement.textContent).toContain('#123');
  });

  it('renders empty state when no orders are present', () => {
    const fixture = TestBed.createComponent(SellerOrderStatusGroupComponent);
    fixture.componentRef.setInput('title', 'Preparando');
    fixture.componentRef.setInput('orders', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhum pedido nesta etapa');
  });

  it('passes updating order id to order cards', () => {
    const fixture = TestBed.createComponent(SellerOrderStatusGroupComponent);
    fixture.componentRef.setInput('title', 'Recebidos');
    fixture.componentRef.setInput('updatingOrderId', 'order1');
    fixture.componentRef.setInput('orders', [
      {
        id: 'order1',
        code: '123',
        storeId: 'store1',
        status: OrderStatus.Received,
        total: 42.5,
        createdAtUtc: '2026-07-29T10:00:00Z',
      },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button').disabled).toBe(true);
  });

  it('does not accept dropped orders as a status change action', () => {
    const fixture = TestBed.createComponent(SellerOrderStatusGroupComponent);
    fixture.componentRef.setInput('title', 'Preparando');
    fixture.componentRef.setInput('status', OrderStatus.Preparing);
    fixture.componentRef.setInput('orders', []);
    fixture.detectChanges();

    const section = fixture.nativeElement.querySelector('.status-group') as HTMLElement;

    expect(section.getAttribute('draggable')).not.toBe('true');
    expect(section.outerHTML).not.toContain('dropOrder');
  });
});
