import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { of, Subject } from 'rxjs';
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
      ordersCount: signal(0),
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

  it('shares the selected period order count with the Pedidos navigation badge', () => {
    orderServiceMock.getStoreReport
      .mockReturnValueOnce(of({ totalOrders: 1, totalRevenue: 50, inProgressOrders: 1 }))
      .mockReturnValueOnce(of({ totalOrders: 6, totalRevenue: 300, inProgressOrders: 2 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(shellFacadeMock.ordersCount()).toBe(1);

    fixture.nativeElement.querySelector('[data-period="week"]').click();
    fixture.detectChanges();

    expect(shellFacadeMock.ordersCount()).toBe(6);
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

  it('passes the selected period range to getStoreOrders as well as the report', () => {
    jest.useFakeTimers().setSystemTime(new Date('2026-07-29T12:00:00.000Z'));
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 1, totalRevenue: 50, inProgressOrders: 1 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));
    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(orderServiceMock.getStoreOrders).toHaveBeenLastCalledWith({
      pageSize: 100,
      startDateUtc: '2026-07-29T03:00:00.000Z',
      endDateUtc: '2026-07-29T12:00:00.000Z',
    });

    fixture.nativeElement.querySelector('[data-period="week"]').click();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreOrders).toHaveBeenLastCalledWith({
      pageSize: 100,
      startDateUtc: '2026-07-23T03:00:00.000Z',
      endDateUtc: '2026-07-29T12:00:00.000Z',
    });
    jest.useRealTimers();
  });

  it('drops a stale report response and does not fetch orders for it when the period changes quickly', () => {
    const reportSubjects: Subject<{ totalOrders: number; totalRevenue: number; inProgressOrders: number }>[] = [];
    orderServiceMock.getStoreReport.mockImplementation(() => {
      const subject = new Subject<{ totalOrders: number; totalRevenue: number; inProgressOrders: number }>();
      reportSubjects.push(subject);
      return subject.asObservable();
    });
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.selectPeriod('week');
    fixture.detectChanges();

    expect(reportSubjects.length).toBe(2);

    reportSubjects[1].next({ totalOrders: 6, totalRevenue: 300, inProgressOrders: 2 });
    reportSubjects[1].complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.report()?.totalOrders).toBe(6);
    expect(orderServiceMock.getStoreOrders).toHaveBeenCalledTimes(1);

    reportSubjects[0].next({ totalOrders: 1, totalRevenue: 50, inProgressOrders: 1 });
    reportSubjects[0].complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.report()?.totalOrders).toBe(6);
    expect(orderServiceMock.getStoreOrders).toHaveBeenCalledTimes(1);
  });

  it('keeps the newest orders when a stale orders response resolves later', () => {
    const reportSubjects: Subject<{ totalOrders: number; totalRevenue: number; inProgressOrders: number }>[] = [];
    const orderSubjects: Subject<any>[] = [];
    orderServiceMock.getStoreReport.mockImplementation(() => {
      const subject = new Subject<{ totalOrders: number; totalRevenue: number; inProgressOrders: number }>();
      reportSubjects.push(subject);
      return subject.asObservable();
    });
    orderServiceMock.getStoreOrders.mockImplementation(() => {
      const subject = new Subject<any>();
      orderSubjects.push(subject);
      return subject.asObservable();
    });

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    reportSubjects[0].next({ totalOrders: 1, totalRevenue: 50, inProgressOrders: 1 });
    reportSubjects[0].complete();
    fixture.detectChanges();

    fixture.componentInstance.load({ silent: true });
    reportSubjects[1].next({ totalOrders: 2, totalRevenue: 100, inProgressOrders: 1 });
    reportSubjects[1].complete();
    fixture.detectChanges();

    expect(orderSubjects.length).toBe(2);

    orderSubjects[1].next({ items: [{ id: 'new', status: 1, total: 10 }] });
    orderSubjects[1].complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.recentOrders().map((o) => o.id)).toEqual(['new']);

    orderSubjects[0].next({ items: [{ id: 'old', status: 1, total: 5 }] });
    orderSubjects[0].complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.recentOrders().map((o) => o.id)).toEqual(['new']);
  });

  it('does not render fixed trend percentages on the metric cards', () => {
    orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 2, totalRevenue: 100, inProgressOrders: 4 }));
    orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 100, totalItems: 0, totalPages: 0, items: [] }));

    const fixture = TestBed.createComponent(SellerDashboardPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('12%');
    expect(fixture.nativeElement.textContent).not.toContain('8%');
    expect(fixture.nativeElement.textContent).not.toContain('5%');
    expect(fixture.nativeElement.querySelector('.trend')).toBeNull();
  });
});
