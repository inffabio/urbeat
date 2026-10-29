import { TestBed } from '@angular/core/testing';
import { AnalyticsDonutChartComponent } from './analytics-donut-chart.component';

describe('AnalyticsDonutChartComponent', () => {
  function create(slices: { label: string; value: number }[], total: number, ariaLabel = 'Produtos em outubro') {
    const fixture = TestBed.createComponent(AnalyticsDonutChartComponent);
    fixture.componentRef.setInput('slices', slices);
    fixture.componentRef.setInput('total', total);
    fixture.componentRef.setInput('ariaLabel', ariaLabel);
    fixture.detectChanges();
    return fixture;
  }

  it('renders one legend item per slice with quantity and percentage', () => {
    const fixture = create(
      [
        { label: 'X-Burger', value: 30 },
        { label: 'Batata', value: 10 },
      ],
      40,
    );

    const items = fixture.nativeElement.querySelectorAll('ul li');
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain('X-Burger');
    expect(items[0].textContent).toContain('30');
    expect(items[0].textContent).toContain('75%');
    expect(items[1].textContent).toContain('25%');
  });

  it('keeps the legend outside the image role', () => {
    const fixture = create([{ label: 'X-Burger', value: 12 }], 12);
    const image = fixture.nativeElement.querySelector('[role="img"]');

    expect(image.querySelector('ul')).toBeNull();
    expect(fixture.nativeElement.querySelector('ul.donut-legend')).not.toBeNull();
  });

  it('exposes an accessible image label with the total and period', () => {
    const fixture = create([{ label: 'X-Burger', value: 12 }], 12, 'Produtos em outubro de 2026');

    const image = fixture.nativeElement.querySelector('[role="img"]');
    expect(image.getAttribute('aria-label')).toContain('12');
    expect(image.getAttribute('aria-label')).toContain('outubro de 2026');
  });

  it('shows the total and center label', () => {
    const fixture = create([{ label: 'X-Burger', value: 7 }], 7);

    expect(fixture.nativeElement.querySelector('.donut-center').textContent).toContain('7');
    expect(fixture.nativeElement.querySelector('.donut-center').textContent).toContain('unidades');
  });

  it('shows an empty message without dividing by zero', () => {
    const fixture = create([], 0);

    expect(fixture.nativeElement.textContent).toContain('Nenhum pedido entregue neste período.');
    expect(fixture.nativeElement.textContent).not.toContain('NaN');
  });

  it('uses a neutral class for the Outros slice', () => {
    const fixture = create(
      [
        { label: 'X-Burger', value: 5 },
        { label: 'Outros', value: 5 },
      ],
      10,
    );

    const slices = fixture.nativeElement.querySelectorAll('.donut-slice');
    expect(slices[0].classList.contains('is-neutral')).toBe(false);
    expect(slices[1].classList.contains('is-neutral')).toBe(true);
  });
});
