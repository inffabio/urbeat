import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { SellerDeliveriesPageComponent } from './seller-deliveries-page.component';

describe('SellerDeliveriesPageComponent', () => {
  let orderServiceMock: { getStoreDeliveries: jest.Mock };

  beforeEach(async () => {
    orderServiceMock = {
      getStoreDeliveries: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerDeliveriesPageComponent],
      providers: [{ provide: OrderService, useValue: orderServiceMock }],
    }).compileComponents();
  });

  it('loads seller delivery aggregates and renders delivery details', () => {
    orderServiceMock.getStoreDeliveries.mockReturnValue(of([
      { id: 'order1', code: '123', customerName: 'Cliente Teste', customerPhoneNumber: '11988887777', addressSummary: 'Rua Teste, 10 - Centro', status: OrderStatus.OnDelivery, total: 42.5, createdAtUtc: '2026-07-29T10:00:00Z' },
    ]));

    const fixture = TestBed.createComponent(SellerDeliveriesPageComponent);
    fixture.detectChanges();

    expect(orderServiceMock.getStoreDeliveries).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('#123');
    expect(fixture.nativeElement.textContent).toContain('Cliente Teste');
    expect(fixture.nativeElement.textContent).toContain('Rua Teste, 10');
    expect(fixture.nativeElement.textContent).toContain('R$');
    expect(fixture.nativeElement.textContent).toContain('Entregas');
    expect(fixture.nativeElement.textContent).toContain('Entregas do painel');
    expect(fixture.nativeElement.textContent).toContain('Atualizado');
  });

  it('renders empty state when there are no delivery orders', () => {
    orderServiceMock.getStoreDeliveries.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerDeliveriesPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhuma entrega encontrada');
    expect(fixture.nativeElement.textContent).not.toContain('#123');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-empty')).not.toBeNull();
  });

  it('filters deliveries by search term', () => {
    orderServiceMock.getStoreDeliveries.mockReturnValue(of([
      { id: 'order1', code: '123', customerName: 'Cliente Teste', customerPhoneNumber: '11988887777', addressSummary: 'Rua Teste, 10 - Centro', status: OrderStatus.OnDelivery, total: 42.5, createdAtUtc: '2026-07-29T10:00:00Z' },
      { id: 'order2', code: '456', customerName: 'Outro Cliente', customerPhoneNumber: '11911112222', addressSummary: 'Rua Secundaria, 20 - Bairro', status: OrderStatus.Delivered, total: 18, createdAtUtc: '2026-07-29T11:00:00Z' },
    ]));

    const fixture = TestBed.createComponent(SellerDeliveriesPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.searchQuery.set('Outro');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Outro Cliente');
    expect(fixture.nativeElement.textContent).not.toContain('Cliente Teste');
  });

  it('shows retry state when delivery orders fail to load', () => {
    orderServiceMock.getStoreDeliveries.mockReturnValue(throwError(() => new Error('network')));

    const fixture = TestBed.createComponent(SellerDeliveriesPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nao foi possivel carregar as entregas');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-error button').textContent).toContain('Tentar novamente');
  });

});
