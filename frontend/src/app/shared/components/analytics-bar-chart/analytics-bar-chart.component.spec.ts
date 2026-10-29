import { TestBed } from '@angular/core/testing';
import { AnalyticsBarChartComponent } from './analytics-bar-chart.component';

describe('AnalyticsBarChartComponent', () => {
  const bars = Array.from({ length: 12 }, (_, index) => ({
    label: ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'][index],
    value: index === 9 ? 100 : 0,
    highlighted: index === 9,
  }));

  function create(total = 100, bestLabel = 'Outubro') {
    const fixture = TestBed.createComponent(AnalyticsBarChartComponent);
    fixture.componentRef.setInput('bars', bars);
    fixture.componentRef.setInput('total', total);
    fixture.componentRef.setInput('ariaLabel', `Faturamento por mês em 2026. Total ${total}`);
    fixture.componentRef.setInput('caption', 'Faturamento por mês em 2026');
    fixture.componentRef.setInput('bestLabel', bestLabel);
    fixture.detectChanges();
    return fixture;
  }

  it('renders exactly twelve bars', () => {
    const fixture = create();

    expect(fixture.nativeElement.querySelectorAll('.bar')).toHaveLength(12);
  });

  it('applies the highlight class only to the highlighted bar', () => {
    const fixture = create();
    const barsElements = fixture.nativeElement.querySelectorAll('.bar');

    expect(barsElements[9].classList.contains('is-highlighted')).toBe(true);
    expect(barsElements[0].classList.contains('is-highlighted')).toBe(false);
  });

  it('renders an accessible table inside details', () => {
    const fixture = create();

    const details = fixture.nativeElement.querySelector('details');
    const table = details.querySelector('table');
    expect(table.querySelector('caption').textContent).toContain('Faturamento por mês');
    expect(table.querySelectorAll('th[scope="col"]')).toHaveLength(2);
    expect(table.querySelectorAll('tbody tr')).toHaveLength(12);
  });

  it('keeps the data table outside the image role', () => {
    const fixture = create();
    const image = fixture.nativeElement.querySelector('[role="img"]');

    expect(image.querySelector('details')).toBeNull();
    expect(fixture.nativeElement.querySelector('details table')).not.toBeNull();
  });

  it('exposes an accessible image label with total and best month', () => {
    const fixture = create();

    const image = fixture.nativeElement.querySelector('[role="img"]');
    expect(image.getAttribute('aria-label')).toContain('100');
    expect(image.getAttribute('aria-label')).toContain('Outubro');
  });

  it('keeps zero values finite and free of NaN', () => {
    const fixture = create(0, 'Sem faturamento');

    expect(fixture.nativeElement.textContent).not.toContain('NaN');
    expect(fixture.nativeElement.querySelectorAll('.bar')).toHaveLength(12);
  });
});
