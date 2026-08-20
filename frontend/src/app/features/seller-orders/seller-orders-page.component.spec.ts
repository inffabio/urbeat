import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { SignalRService } from '../../core/services/signalr.service';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { SellerOrdersPageComponent } from './seller-orders-page.component';

jest.mock('../../core/utils/platform.helper', () => ({
  ...jest.requireActual('../../core/utils/platform.helper'),
  isWindowsPlatform: jest.fn(),
}));

import { isWindowsPlatform } from '../../core/utils/platform.helper';

const isWindowsPlatformMock = isWindowsPlatform as jest.Mock;

describe('SellerOrdersPageComponent', () => {
  let orderServiceMock: { getStoreOrders: jest.Mock; getStoreOrder: jest.Mock; updateStoreOrderStatus: jest.Mock; completeOrder: jest.Mock };
  let printingServiceMock: { printAcceptedOrder: jest.Mock };
  let toastServiceMock: { showSuccess: jest.Mock; showError: jest.Mock };
  let shellMock: { newOrderPulse: any; notifyOrderChanged: jest.Mock };
  let signalRServiceMock: { onSellerEvent: jest.Mock; removeSellerListener: jest.Mock };
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
    isWindowsPlatformMock.mockReturnValue(false);
    orderServiceMock = {
      getStoreOrders: jest.fn().mockReturnValue(of({ items: [], totalItems: 0 })),
      getStoreOrder: jest.fn().mockReturnValue(of({ id: '', code: '', items: [], total: 0, createdAtUtc: '' })),
      updateStoreOrderStatus: jest.fn().mockReturnValue(of({})),
      completeOrder: jest.fn().mockReturnValue(of({})),
    };
    printingServiceMock = { printAcceptedOrder: jest.fn().mockResolvedValue(undefined) };
    toastServiceMock = { showSuccess: jest.fn().mockResolvedValue(undefined), showError: jest.fn().mockResolvedValue(undefined) };
    shellMock = { newOrderPulse: jest.fn(() => null), notifyOrderChanged: jest.fn() };
    signalRServiceMock = { onSellerEvent: jest.fn(), removeSellerListener: jest.fn() };
    activatedRouteMock = { snapshot: { queryParamMap: convertToParamMap({}) } };

    await TestBed.configureTestingModule({
      imports: [SellerOrdersPageComponent],
      providers: [
        { provide: OrderService, useValue: orderServiceMock },
        { provide: SignalRService, useValue: signalRServiceMock },
        { provide: SellerPrintingService, useValue: printingServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
        { provide: SellerShellFacade, useValue: shellMock },
        { provide: ActivatedRoute, useValue: activatedRouteMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
    isWindowsPlatformMock.mockReturnValue(false);
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

  it('shows code and first name-phone on two lines of the new-order reference card', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Received) {
        return of({ items: [buildOrder({ id: '1', code: 'URB-123456', customerName: 'Joao Silva', customerPhoneNumber: '1199999' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const refCard = fixture.nativeElement.querySelector('.status-card.ref');
    expect(refCard.textContent).toContain('#URB-123456');
    expect(refCard.textContent).toContain('Joao - 1199999');
  });

  it('hides seller-completed orders from the day board', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({
          items: [
            buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, sellerCompletedAtUtc: '2026-08-04T11:00:00Z' }),
            buildOrder({ id: 'pending', code: 'URB-2', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' }),
          ],
          totalItems: 2,
        });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.statusGroups().delivered.map((o) => o.id)).toEqual(['pending']);
  });

  it('shows the Concluído action for delivered orders confirmed by the customer', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Concluído');
  });

  it('does not show Concluído for delivered orders without customer confirmation', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'wait', code: 'URB-3', status: OrderStatus.Delivered })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Concluído');
  });

  it('completes an order and removes it from the board on success', () => {
    orderServiceMock.completeOrder.mockReturnValue(of({ sellerCompletedAtUtc: '2026-08-04T12:00:00Z' }));
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.completeOrder(fixture.componentInstance.statusGroups().delivered[0]);

    expect(orderServiceMock.completeOrder).toHaveBeenCalledWith('done');
    expect(fixture.componentInstance.statusGroups().delivered.length).toBe(0);
    expect(toastServiceMock.showSuccess).toHaveBeenCalled();
  });

  it('shows a loading state and disables the action while completing', () => {
    const subject = new Subject<any>();
    orderServiceMock.completeOrder.mockReturnValue(subject.asObservable());
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.completeOrder(fixture.componentInstance.statusGroups().delivered[0]);
    fixture.detectChanges();

    expect(fixture.componentInstance.completingOrderId()).toBe('done');
    expect(fixture.nativeElement.textContent).toContain('Concluindo');
    expect(fixture.nativeElement.querySelector('.status-card.action-green').disabled).toBe(true);

    subject.next({ sellerCompletedAtUtc: '2026-08-04T12:00:00Z' });
    subject.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.completingOrderId()).toBeNull();
  });

  it('releases the Concluído button when completion fails', () => {
    orderServiceMock.completeOrder.mockReturnValue(throwError(() => new Error('fail')));
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Delivered) {
        return of({ items: [buildOrder({ id: 'done', code: 'URB-1', status: OrderStatus.Delivered, deliveryConfirmedAtUtc: '2026-08-04T11:00:00Z' })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.completeOrder(fixture.componentInstance.statusGroups().delivered[0]);

    expect(fixture.componentInstance.completingOrderId()).toBeNull();
    expect(fixture.componentInstance.statusGroups().delivered.length).toBe(1);
    expect(toastServiceMock.showError).toHaveBeenCalled();
  });

  it('reloads silently when OrderStatusUpdated arrives for a known order', () => {
    orderServiceMock.getStoreOrders.mockImplementation(({ status }: any) => {
      if (status === OrderStatus.Preparing) {
        return of({ items: [buildOrder({ id: '2', code: 'DEF', status: OrderStatus.Preparing })], totalItems: 1 });
      }
      return of({ items: [], totalItems: 0 });
    });

    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const listener = signalRServiceMock.onSellerEvent.mock.calls.find(([name]: [string]) => name === 'OrderStatusUpdated')?.[1];
    expect(listener).toBeDefined();

    const before = orderServiceMock.getStoreOrders.mock.calls.length;
    listener({ orderId: '2', status: OrderStatus.Ready });
    expect(orderServiceMock.getStoreOrders.mock.calls.length).toBeGreaterThan(before);
  });

  it('ignores OrderStatusUpdated for an unknown order', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const listener = signalRServiceMock.onSellerEvent.mock.calls.find(([name]: [string]) => name === 'OrderStatusUpdated')?.[1];
    expect(listener).toBeDefined();

    const before = orderServiceMock.getStoreOrders.mock.calls.length;
    listener({ orderId: 'unknown' });
    expect(orderServiceMock.getStoreOrders.mock.calls.length).toBe(before);
  });

  it('removes the OrderStatusUpdated listener on destroy', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    const listener = signalRServiceMock.onSellerEvent.mock.calls.find(([name]: [string]) => name === 'OrderStatusUpdated')?.[1];
    expect(listener).toBeDefined();

    fixture.destroy();

    expect(signalRServiceMock.removeSellerListener).toHaveBeenCalledWith('OrderStatusUpdated', listener);
  });

  it('does not show the fullscreen toggle outside Windows', () => {
    const fixture = TestBed.createComponent(SellerOrdersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.kiosk-toggle')).toBeNull();
  });

  describe('fullscreen kiosk on Windows', () => {
    let fullscreenElement: Element | null;
    let requestFullscreenMock: jest.Mock;
    let exitFullscreenMock: jest.Mock;

    beforeEach(() => {
      isWindowsPlatformMock.mockReturnValue(true);
      fullscreenElement = null;

      Object.defineProperty(document, 'fullscreenElement', { configurable: true, get: () => fullscreenElement });

      requestFullscreenMock = jest.fn(() => {
        fullscreenElement = document.documentElement;
        return Promise.resolve();
      });
      Object.defineProperty(document.documentElement, 'requestFullscreen', { configurable: true, value: requestFullscreenMock });

      exitFullscreenMock = jest.fn(() => {
        fullscreenElement = null;
        return Promise.resolve();
      });
      Object.defineProperty(document, 'exitFullscreen', { configurable: true, value: exitFullscreenMock });
    });

    it('shows the fullscreen toggle with an accessible pressed state', () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.kiosk-toggle');
      expect(button).not.toBeNull();
      expect(button.getAttribute('aria-pressed')).toBe('false');
    });

    it('enters fullscreen via requestFullscreen and reflects the state', async () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      await fixture.componentInstance.toggleFullscreen();
      fixture.detectChanges();

      expect(requestFullscreenMock).toHaveBeenCalled();
      expect(fixture.componentInstance.isFullscreen()).toBe(true);

      const button = fixture.nativeElement.querySelector('.kiosk-toggle');
      expect(button.getAttribute('aria-pressed')).toBe('true');
    });

    it('exits fullscreen when Escape is pressed', async () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      await fixture.componentInstance.toggleFullscreen();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await Promise.resolve();
      await Promise.resolve();
      fixture.detectChanges();

      expect(exitFullscreenMock).toHaveBeenCalled();
      expect(fixture.componentInstance.isFullscreen()).toBe(false);
    });

    it('ignores non-Escape keys while in fullscreen', async () => {
      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();

      await fixture.componentInstance.toggleFullscreen();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

      expect(exitFullscreenMock).not.toHaveBeenCalled();
    });

    it('cleans up fullscreen listeners on destroy', () => {
      const removeSpy = jest.spyOn(document, 'removeEventListener');

      const fixture = TestBed.createComponent(SellerOrdersPageComponent);
      fixture.detectChanges();
      fixture.destroy();

      expect(removeSpy).toHaveBeenCalledWith('keydown', expect.any(Function));
      expect(removeSpy).toHaveBeenCalledWith('fullscreenchange', expect.any(Function));

      removeSpy.mockRestore();
    });
  });
});
