import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { SellerOrdersPageComponent } from './seller-orders-page.component';

describe('SellerOrdersPageComponent', () => {
  let orderServiceMock: { getStoreOrders: jest.Mock; getStoreOrder: jest.Mock; updateStoreOrderStatus: jest.Mock };
  let printingServiceMock: { printAcceptedOrder: jest.Mock };
  let toastServiceMock: { showSuccess: jest.Mock; showError: jest.Mock };
  let shellMock: { newOrderPulse: any; notifyOrderChanged: jest.Mock };
  let activatedRouteMock: { snapshot: { queryParamMap: ReturnType<typeof convertToParamMap> } };

  const buildOrder = (overrides: Partial<any> = {}) => ({
    id: 'order-1',
    code: 'ABC',
    storeId: 'store-1',
    status: OrderStatus.Received,
    total: 50,
    createdAtUtc: '2026-08-04T10:00:00Z',
    ...overrides,
  });

  beforeEach(async () => {
    orderServiceMock = {
      getStoreOrders: jest.fn().mockReturnValue(of({ items: [], totalItems: 0 })),
      getStoreOrder: jest.fn().mockReturnValue(of({ id: '', code: '', items: [], total: 0, createdAtUtc: '' })),
      updateStoreOrderStatus: jest.fn().mockReturnValue(of({})),
    };
    printingServiceMock = { printAcceptedOrder: jest.fn().mockResolvedValue(undefined) };
    toastServiceMock = { showSuccess: jest.fn().mockResolvedValue(undefined), showError: jest.fn().mockResolvedValue(undefined) };
    shellMock = { newOrderPulse: jest.fn(() => null), notifyOrderChanged: jest.fn() };
    activatedRouteMock = { snapshot: { queryParamMap: convertToParamMap({}) } };

    await TestBed.configureTestingModule({
      imports: [SellerOrdersPageComponent],
      providers: [
        { provide: OrderService, useValue: orderServiceMock },
        { provide: SellerPrintingService, useValue: printingServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
        { provide: SellerShellFacade, useValue: shellMock },
        { provide: ActivatedRoute, useValue: activatedRouteMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('shows empty new orders panel when no orders exist', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Novos pedidos');
    expect(fixture.nativeElement.textContent).toContain('Nenhum pedido novo');
  });

  it('shows new order cards in the top panel', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', customerName: 'Joao', customerPhoneNumber: '119999', paymentMethod: 0 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('ABC');
    expect(fixture.nativeElement.textContent).toContain('Joao');
    expect(fixture.nativeElement.textContent).toContain('Aceitar pedido');
  });

  it('shows orders in status columns', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: '2', code: 'DEF', status: OrderStatus.Preparing, total: 30 })], totalItems: 1 });
      }
      if (status === OrderStatus.Ready) {
        return of({ items: [buildOrder({ id: '3', code: 'GHI', status: OrderStatus.Ready, total: 40 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Em preparação');
    expect(fixture.nativeElement.textContent).toContain('Marcar como pronto');
    expect(fixture.nativeElement.textContent).toContain('Saiu para entrega');
  });

  it('shows confirmation modal when advancing status', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: '2', code: 'DEF', status: OrderStatus.Preparing, total: 30 })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('.action-orange');
    if (btn) btn.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Confirmar');
    expect(fixture.nativeElement.textContent).toContain('Cancelar');
  });

  it('sorts new orders and status columns by createdAtUtc descending', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [
            buildOrder({ id: 'older-received', code: 'OLD', createdAtUtc: '2026-08-04T10:00:00Z' }),
            buildOrder({ id: 'newer-received', code: 'NEW', createdAtUtc: '2026-08-04T10:05:00Z' }),
          ],
          totalItems: 2,
        });
      }

      if (status === OrderStatus.Preparing) {
        return of({
          items: [
            buildOrder({ id: 'older-preparing', code: 'P-OLD', status: OrderStatus.Preparing, createdAtUtc: '2026-08-04T09:50:00Z' }),
            buildOrder({ id: 'newer-preparing', code: 'P-NEW', status: OrderStatus.Preparing, createdAtUtc: '2026-08-04T10:10:00Z' }),
          ],
          totalItems: 2,
        });
      }

      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.newOrders().map((order) => order.id)).toEqual(['newer-received', 'older-received']);
    expect(fixture.componentInstance.statusGroups().preparing.map((order) => order.id)).toEqual(['newer-preparing', 'older-preparing']);
  });

  it('prints the order when acceptance moves it from received to preparing', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.confirmAdvance(buildOrder({ id: 'accepted-order' }), OrderStatus.Preparing, 'Aceitar pedido');
    fixture.componentInstance.executeAdvance();

    expect(orderServiceMock.updateStoreOrderStatus).toHaveBeenCalledWith(
      'accepted-order',
      OrderStatus.Preparing,
      'Atualizado pelo painel do lojista',
    );
    expect(printingServiceMock.printAcceptedOrder).toHaveBeenCalledWith('accepted-order');
  });

  it('keeps the acceptance flow running when automatic printing fails', () => {
    printingServiceMock.printAcceptedOrder.mockImplementation(() => {
      throw new Error('printer offline');
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.confirmAdvance(buildOrder({ id: 'accepted-order' }), OrderStatus.Preparing, 'Aceitar pedido');
    fixture.componentInstance.executeAdvance();

    expect(orderServiceMock.updateStoreOrderStatus).toHaveBeenCalledWith(
      'accepted-order',
      OrderStatus.Preparing,
      'Atualizado pelo painel do lojista',
    );
    expect(shellMock.notifyOrderChanged).toHaveBeenCalledWith('accepted-order');
    expect(orderServiceMock.getStoreOrders).toHaveBeenCalled();
    expect(toastServiceMock.showError).not.toHaveBeenCalled();
  });

  it('does not print when changing to a status other than preparing', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.confirmAdvance(
      buildOrder({ id: 'ready-order', status: OrderStatus.Preparing }),
      OrderStatus.Ready,
      'Marcar pronto',
    );
    fixture.componentInstance.executeAdvance();

    expect(printingServiceMock.printAcceptedOrder).not.toHaveBeenCalled();
  });

  it('consumes the order query param and highlights the target order with focus', () => {
    jest.useFakeTimers();
    activatedRouteMock.snapshot.queryParamMap = convertToParamMap({ order: 'target-order' });

    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: jest.fn(),
    });

    const scrollIntoViewSpy = jest.spyOn(HTMLElement.prototype, 'scrollIntoView').mockImplementation(jest.fn());
    const focusSpy = jest.spyOn(HTMLElement.prototype, 'focus').mockImplementation(jest.fn());

    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({
          items: [
            buildOrder({ id: 'target-order', code: 'TARGET', createdAtUtc: '2026-08-04T10:05:00Z' }),
            buildOrder({ id: 'other-order', code: 'OTHER', createdAtUtc: '2026-08-04T10:00:00Z' }),
          ],
          totalItems: 2,
        });
      }

      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();
    jest.runAllTimers();
    fixture.detectChanges();

    const targetCard = fixture.nativeElement.querySelector('[data-order-id="target-order"]');

    expect(targetCard).not.toBeNull();
    expect(targetCard.classList.contains('is-target')).toBe(true);
    expect(scrollIntoViewSpy).toHaveBeenCalled();
    expect(focusSpy).toHaveBeenCalled();
  });

  it('reloads orders when a new order pulse is emitted', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    shellMock.newOrderPulse.mockReturnValue({ id: 'pulse-1' });
    fixture.componentInstance['effectScheduler']?.();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreOrders).toHaveBeenCalled();
  });
});
