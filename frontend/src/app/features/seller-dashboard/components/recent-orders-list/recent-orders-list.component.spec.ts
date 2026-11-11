import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FulfillmentType } from '../../../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../../../shared/enums/order-status.enum';
import { RecentOrdersListComponent } from './recent-orders-list.component';

describe('RecentOrdersListComponent', () => {
  it('labels a ready pickup order as ready for pickup', () => {
    const fixture = TestBed.createComponent(RecentOrdersListComponent);

    expect(fixture.componentInstance.statusLabel(OrderStatus.Ready, FulfillmentType.PickUp)).toBe('Pronto para retirada');
    expect(fixture.componentInstance.statusLabel(OrderStatus.Ready, FulfillmentType.Delivery)).toBe('Pronto');
  });

  it('shows an empty state when there are no orders', () => {
    const fixture = TestBed.createComponent(RecentOrdersListComponent);
    fixture.componentRef.setInput('orders', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhum pedido encontrado');
  });

  it('renders recent order code and total', () => {
    const fixture = TestBed.createComponent(RecentOrdersListComponent);
    fixture.componentRef.setInput('orders', [
      {
        id: 'o1',
        code: '123',
        storeId: 's1',
        status: OrderStatus.Received,
        total: 42.5,
        createdAtUtc: '2026-07-29T10:00:00Z',
      },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('#123');
    expect(fixture.nativeElement.textContent).toContain('R$');
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });
});
