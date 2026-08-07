import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { SubscriptionService } from '../../core/services/subscription.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { SellerPrintingService } from '../seller-printing/seller-printing.service';
import { SellerDashboardPageComponent } from './seller-dashboard-page.component';

describe('SellerDashboardPageComponent', () => {
  let orderServiceMock: { getStoreReport: jest.Mock; getStoreOrders: jest.Mock };
  let shellFacadeMock: any;

  beforeEach(async () => {
    orderServiceMock = {
      getStoreReport: jest.fn(),
      getStoreOrders: jest.fn(),
    };
    shellFacadeMock = {
      unreadCount: jest.fn(() => 3),
      store: jest.fn(() => ({ isSubscriptionBlocked: false })),
      newOrderPulse: jest.fn(() => null),
      orderActivityPulse: signal(null),
    };

    await TestBed.configureTestingModule({
      imports: [SellerDashboardPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: OrderService, useValue: orderServiceMock },
        { provide: SubscriptionService, useValue: { getMySubscription: jest.fn().mockReturnValue(of({})) } },
        { provide: SellerShellFacade, useValue: shellFacadeMock },
        { provide: SellerPrintingService, useValue: { config: jest.fn(() => ({ autoPrint: false, connectionType: 'browser-print' })), bluetoothState: jest.fn(() => ({ status: 'disconnected' })) } },
      ],
    }).compileComponents();
  });

  it('loads dashboard metrics from seller order report', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 2, totalRevenue: 100, inProgressOrders: 4 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('2');
    expect(fixture.nativeElement.textContent).toContain('R$');
    expect(fixture.nativeElement.textContent).toContain('Pedidos em andamento');
    expect(fixture.nativeElement.textContent).toContain('Exigem atenção');
  });

  it('shows the subscription banner when loaded', () => {
    TestBed.overrideProvider(SubscriptionService, {
      useValue: {
        getMySubscription: jest.fn().mockReturnValue(of({ nextDueDateUtc: '2026-08-10T01:30:00.000Z' })),
      },
    });
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 0, totalRevenue: 0, inProgressOrders: 0 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Sua mensalidade');
    expect(fixture.componentInstance.subscriptionDueDate()).toBe('09/08/2026');
  });

  it('renders the documented dashboard heading and period controls', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 0, totalRevenue: 0, inProgressOrders: 0 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Visão geral da loja hoje');
    expect(fixture.nativeElement.textContent).toContain('Hoje');
    expect(fixture.nativeElement.textContent).toContain('Semana');
    expect(fixture.nativeElement.textContent).toContain('Mês');
  });

  it('uses the shared page header and seller table language', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 0, totalRevenue: 0, inProgressOrders: 0 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.topbar')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.seller-table')).not.toBeNull();
  });

  it('renders the documented date pill copy prefix', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 0, totalRevenue: 0, inProgressOrders: 0 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Hoje,');
  });

  it('shows the metrics summary with correct labels', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 0, totalRevenue: 0, inProgressOrders: 0 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Pedidos hoje');
    expect(fixture.nativeElement.textContent).toContain('Faturamento');
    expect(fixture.nativeElement.textContent).toContain('Ticket médio');
  });

  it('reloads metrics when an order activity pulse is emitted', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 1, totalRevenue: 50, inProgressOrders: 1 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    shellFacadeMock.orderActivityPulse.set({ id: 'pulse1', orderId: 'order1', source: 'manual-status-change' });
    fixture.detectChanges();

    expect(orderServiceMock.getStoreReport).toHaveBeenCalledTimes(2);
    expect(orderServiceMock.getStoreOrders).toHaveBeenCalledTimes(2);
  });

  it('reloads report with date range when seller changes the dashboard period', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 1, totalRevenue: 50, inProgressOrders: 1 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-period="week"]').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.selectedPeriod()).toBe('week');
    expect(orderServiceMock.getStoreReport).toHaveBeenLastCalledWith(expect.any(String), expect.any(String));
  });

  it('uses Sao Paulo calendar boundaries converted to UTC for dashboard periods', () => {
    jest.useFakeTimers().setSystemTime(new Date('2026-07-29T12:00:00.000Z'));
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 1, totalRevenue: 50, inProgressOrders: 1 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-period="today"]').click();

    expect(orderServiceMock.getStoreReport).toHaveBeenLastCalledWith('2026-07-29T03:00:00.000Z', '2026-07-29T12:00:00.000Z');
    jest.useRealTimers();
  });
});
