import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NotificationType } from '../../../../shared/models/seller-notification.model';
import { SellerOpsCardComponent } from './seller-ops-card.component';

describe('SellerOpsCardComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  it('shows waiting message when there is no new order', () => {
    const fixture = TestBed.createComponent(SellerOpsCardComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Aguardando novos pedidos');
  });

  it('shows new order message when notification is present', () => {
    const fixture = TestBed.createComponent(SellerOpsCardComponent);
    fixture.componentRef.setInput('newOrder', {
      id: 'n1',
      orderId: 'o1',
      type: NotificationType.NewOrder,
      title: 'Novo pedido recebido',
      message: 'Pedido #123',
      isRead: false,
      createdAtUtc: '2026-07-29T10:00:00Z',
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Novo pedido recebido');
    expect(fixture.nativeElement.textContent).toContain('Pedido #123');
  });
});
