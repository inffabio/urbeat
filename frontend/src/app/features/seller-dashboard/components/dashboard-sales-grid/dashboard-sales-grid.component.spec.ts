import { TestBed } from '@angular/core/testing';
import { PagedStoreSales } from '../../../../shared/models/order.model';
import { DashboardSalesGridComponent } from './dashboard-sales-grid.component';

describe('DashboardSalesGridComponent', () => {
  const baseResult: PagedStoreSales = {
    period: 'week',
    anchorDate: '2026-10-29',
    weekBlock: 5,
    rangeStartUtc: '2026-10-29T03:00:00Z',
    rangeEndUtcExclusive: '2026-11-01T03:00:00Z',
    timeZone: 'America/Sao_Paulo',
    page: 1,
    pageSize: 20,
    totalItems: 57,
    totalPages: 3,
    totals: { salesTotal: 12345.67, deliveryFees: 456.78 },
    sales: [
      {
        orderId: 'order-1',
        code: 'A1B2C3D4',
        customerName: null,
        createdAtUtc: '2026-10-29T16:42:00Z',
        subtotal: 80,
        deliveryFee: 5,
        total: 85,
        items: [
          { productName: 'X-Burger', variationName: null, quantity: 2, unitPrice: 35, totalPrice: 70 },
          { productName: 'Batata grande', variationName: 'Grande', quantity: 1, unitPrice: 10, totalPrice: 10 },
        ],
      },
    ],
  };

  function create(overrides: {
    result?: PagedStoreSales | null;
    loading?: boolean;
    error?: boolean;
    expandedOrders?: ReadonlySet<string>;
    rangeLabel?: string;
  } = {}) {
    const fixture = TestBed.createComponent(DashboardSalesGridComponent);
    fixture.componentRef.setInput('result', overrides.result === undefined ? baseResult : overrides.result);
    fixture.componentRef.setInput('loading', overrides.loading ?? false);
    fixture.componentRef.setInput('error', overrides.error ?? false);
    fixture.componentRef.setInput('expandedOrders', overrides.expandedOrders ?? new Set<string>());
    fixture.componentRef.setInput('rangeLabel', overrides.rangeLabel ?? '29/10 a 31/10/2026');
    fixture.detectChanges();
    return fixture;
  }

  it('renders the title, subtitle and range label', () => {
    const fixture = create();

    expect(fixture.nativeElement.textContent).toContain('Vendas do período');
    expect(fixture.nativeElement.textContent).toContain('Pedidos entregues, pela data de criação.');
    expect(fixture.nativeElement.textContent).toContain('29/10 a 31/10/2026');
  });

  it('renders the five documented columns and falls back to Cliente for a null name', () => {
    const fixture = create();
    const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th')).map((th: any) => th.textContent.trim());

    expect(headers).toEqual(['Pedido', 'Cliente', 'Itens', 'Total da venda', 'Taxa de entrega']);
    expect(fixture.nativeElement.querySelector('.sale-row').textContent).toContain('Cliente');
    expect(fixture.nativeElement.querySelector('.sale-row').textContent).toContain('A1B2C3D4');
    expect(fixture.nativeElement.querySelector('.sale-row').textContent).toContain('R$');
  });

  it('renders independent totals with explicit labels and no general total row', () => {
    const fixture = create();
    const footer = fixture.nativeElement.querySelector('tfoot');

    expect(footer.textContent).toContain('Total das vendas (inclui taxas de entrega)');
    expect(footer.textContent).toContain('Taxas de entrega (já incluídas no total)');
    expect(footer.textContent).toContain('Total do período');
    expect(footer.textContent).toContain('As taxas já estão dentro do total; não somar as duas colunas.');
    expect(footer.textContent).not.toContain('Total geral');
    expect(footer.textContent).toContain('R$ 12.345,67');
    expect(footer.textContent).toContain('R$ 456,78');
  });

  it('keeps the totals when the page changes', () => {
    const pageTwo = { ...baseResult, page: 2, sales: [] };
    const fixture = create({ result: pageTwo });

    expect(fixture.nativeElement.querySelector('tfoot').textContent).toContain('R$ 12.345,67');
    expect(fixture.nativeElement.querySelector('tfoot').textContent).toContain('R$ 456,78');
  });

  it('expands items with prices and keeps the 44px toggle contract', () => {
    const fixture = create({ expandedOrders: new Set(['order-1']) });
    const toggle = fixture.nativeElement.querySelector('.items-toggle');
    const detail = fixture.nativeElement.querySelector('#sale-items-order-1');

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.getAttribute('aria-controls')).toBe('sale-items-order-1');
    expect(toggle.classList.contains('filter-control')).toBe(true);
    expect(detail).not.toBeNull();
    expect(detail.hasAttribute('hidden')).toBe(false);
    expect(detail.textContent).toContain('2 × R$ 35,00 = R$ 70,00');
    expect(detail.textContent).toContain('Subtotal dos itens');
    expect(detail.textContent).toContain('Taxa de entrega');
    expect(detail.textContent).toContain('Subtotal + taxa = total');
    expect(detail.textContent).toContain('Adicionais e opções já estão incluídos no preço do item');
  });

  it('keeps the aria-controls target in the DOM while collapsed', () => {
    const fixture = create();
    const toggle = fixture.nativeElement.querySelector('.items-toggle');
    const detail = fixture.nativeElement.querySelector('#sale-items-order-1');

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(detail).not.toBeNull();
    expect(detail.hasAttribute('hidden')).toBe(true);
  });

  it('emits toggleItems without doing requests', () => {
    const fixture = create();
    const toggleItems = jest.fn();
    fixture.componentInstance.toggleItems.subscribe(toggleItems);

    fixture.nativeElement.querySelector('.items-toggle').click();

    expect(toggleItems).toHaveBeenCalledWith('order-1');
  });

  it('paginates with accessible labels and disabled bounds', () => {
    const fixture = create();
    const nav = fixture.nativeElement.querySelector('nav[aria-label="Paginação das vendas"]');
    const previous = fixture.nativeElement.querySelector('[data-page-action="previous"]');
    const next = fixture.nativeElement.querySelector('[data-page-action="next"]');
    const pageChange = jest.fn();
    fixture.componentInstance.pageChange.subscribe(pageChange);

    expect(nav.textContent).toContain('Página 1 de 3');
    expect(nav.textContent).toContain('57 vendas entregues');
    expect(previous.disabled).toBe(true);
    expect(next.disabled).toBe(false);

    next.click();
    expect(pageChange).toHaveBeenCalledWith(2);
  });

  it('renders the empty state with zeroed labelled totals and no pagination', () => {
    const fixture = create({
      result: { ...baseResult, totalItems: 0, totalPages: 0, totals: { salesTotal: 0, deliveryFees: 0 }, sales: [] },
    });

    expect(fixture.nativeElement.textContent).toContain('Nenhuma venda entregue neste período.');
    expect(fixture.nativeElement.querySelector('tfoot').textContent).toContain('R$ 0,00');
    expect(fixture.nativeElement.querySelector('nav[aria-label="Paginação das vendas"]')).toBeNull();
  });

  it('shows an error banner with retry while keeping previous content', () => {
    const fixture = create({ error: true });
    const retry = jest.fn();
    fixture.componentInstance.retry.subscribe(retry);

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar as vendas.');
    expect(fixture.nativeElement.querySelector('.sale-row')).not.toBeNull();

    fixture.nativeElement.querySelector('.sales-retry').click();
    expect(retry).toHaveBeenCalled();
  });

  it('offers the last page when the current page is beyond the end', () => {
    const fixture = create({ result: { ...baseResult, page: 5, sales: [] } });
    const pageChange = jest.fn();
    fixture.componentInstance.pageChange.subscribe(pageChange);

    expect(fixture.nativeElement.textContent).toContain('Esta página não existe mais.');
    fixture.nativeElement.querySelector('[data-page-action="last"]').click();
    expect(pageChange).toHaveBeenCalledWith(3);
  });

  it('exposes mobile card labels and a 44px items toggle class', () => {
    const fixture = create();
    const cells = fixture.nativeElement.querySelectorAll('tbody .sale-row td, tbody .sale-row th');

    cells.forEach((cell: HTMLElement) => {
      expect(cell.getAttribute('data-label')).toBeTruthy();
    });
  });

  it('renders a five-row skeleton and hides totals on the first load', () => {
    const fixture = create({ result: null, loading: true });

    expect(fixture.nativeElement.querySelectorAll('.skeleton-row')).toHaveLength(5);
    expect(fixture.nativeElement.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('tfoot')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-page-action="next"]').disabled).toBe(true);
  });

  it('keeps the previous totals and announces the page while loading another page', () => {
    const fixture = create({ result: { ...baseResult, page: 2 }, loading: true });

    expect(fixture.nativeElement.textContent).toContain('Carregando página 2');
    expect(fixture.nativeElement.querySelector('tfoot').textContent).toContain('R$ 12.345,67');
  });
});
