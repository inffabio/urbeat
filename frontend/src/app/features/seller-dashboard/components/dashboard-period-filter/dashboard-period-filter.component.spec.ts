import { TestBed } from '@angular/core/testing';
import { DashboardPeriodFilterComponent } from './dashboard-period-filter.component';

describe('DashboardPeriodFilterComponent', () => {
  function create(overrides: Partial<Record<'period' | 'anchorDate' | 'weekBlock' | 'availableYears' | 'todayIso', unknown>> = {}) {
    const fixture = TestBed.createComponent(DashboardPeriodFilterComponent);
    fixture.componentRef.setInput('period', 'day');
    fixture.componentRef.setInput('anchorDate', '2026-10-29');
    fixture.componentRef.setInput('weekBlock', 5);
    fixture.componentRef.setInput('availableYears', [2026, 2025, 2024]);
    fixture.componentRef.setInput('todayIso', '2026-10-29');
    for (const [key, value] of Object.entries(overrides)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return fixture;
  }

  it('renders four accessible period buttons with the active one pressed', () => {
    const fixture = create();
    const group = fixture.nativeElement.querySelector('[role="group"][aria-label="Período do dashboard"]');
    const buttons = fixture.nativeElement.querySelectorAll('[data-period]');

    expect(group).not.toBeNull();
    expect(buttons).toHaveLength(4);
    expect(fixture.nativeElement.querySelector('[data-period="day"]').getAttribute('aria-pressed')).toBe('true');
    expect(fixture.nativeElement.querySelector('[data-period="week"]').getAttribute('aria-pressed')).toBe('false');
    expect(fixture.nativeElement.querySelector('[data-period="day"]').textContent).toContain('Dia');
    expect(fixture.nativeElement.querySelector('[data-period="week"]').textContent).toContain('Semana');
    expect(fixture.nativeElement.querySelector('[data-period="month"]').textContent).toContain('Mês');
    expect(fixture.nativeElement.querySelector('[data-period="year"]').textContent).toContain('Ano');
  });

  it('renders a labelled date input with today as max for the day period', () => {
    const fixture = create();
    const input = fixture.nativeElement.querySelector('input[type="date"]');

    expect(input).not.toBeNull();
    expect(input.getAttribute('max')).toBe('2026-10-29');
    expect(input.value).toBe('2026-10-29');
    const label = fixture.nativeElement.querySelector(`label[for="${input.id}"]`);
    expect(label.textContent).toContain('Dia');
    expect(fixture.nativeElement.querySelectorAll('select')).toHaveLength(0);
    expect(fixture.nativeElement.querySelectorAll('[data-week-block]')).toHaveLength(0);
  });

  it('renders month, year and block selectors for the week period', () => {
    const october = create({ period: 'week', anchorDate: '2026-10-29', weekBlock: 5 });

    expect(october.nativeElement.querySelectorAll('select')).toHaveLength(2);
    expect(october.nativeElement.querySelectorAll('[data-week-block]')).toHaveLength(5);
    expect((october.nativeElement.querySelector('#dashboard-month') as HTMLSelectElement).value).toBe('10');
    expect((october.nativeElement.querySelector('#dashboard-year') as HTMLSelectElement).value).toBe('2026');

    const february = create({ period: 'week', anchorDate: '2026-02-10', weekBlock: 2 });
    expect(february.nativeElement.querySelectorAll('[data-week-block]')).toHaveLength(4);

    const leapFebruary = create({ period: 'week', anchorDate: '2028-02-10', weekBlock: 2, availableYears: [2028] });
    expect(leapFebruary.nativeElement.querySelectorAll('[data-week-block]')).toHaveLength(5);
  });

  it('marks the selected block and gives it a full accessible label', () => {
    const fixture = create({ period: 'week', anchorDate: '2026-10-29', weekBlock: 5 });
    const selected = fixture.nativeElement.querySelector('[data-week-block="5"]');

    expect(selected.getAttribute('aria-pressed')).toBe('true');
    expect(selected.getAttribute('aria-label')).toBe('Dias 29 a 31 de outubro de 2026');
    expect(selected.textContent).toContain('29–31');
    expect(fixture.nativeElement.querySelector('[data-week-block="1"]').getAttribute('aria-pressed')).toBe('false');
  });

  it('renders only month and year selectors for the month period', () => {
    const fixture = create({ period: 'month', anchorDate: '2026-10-01' });

    expect(fixture.nativeElement.querySelectorAll('select')).toHaveLength(2);
    expect(fixture.nativeElement.querySelector('input[type="date"]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('[data-week-block]')).toHaveLength(0);
  });

  it('renders only the year selector with descending options for the year period', () => {
    const fixture = create({ period: 'year', anchorDate: '2026-01-01' });
    const selects = fixture.nativeElement.querySelectorAll('select');

    expect(selects).toHaveLength(1);
    const options = Array.from(selects[0].querySelectorAll('option')).map((option: any) => option.value);
    expect(options).toEqual(['2026', '2025', '2024']);
  });

  it('does not offer future months when the selected year is the current year', () => {
    const fixture = create({ period: 'month', anchorDate: '2026-10-01' });
    const monthSelect = fixture.nativeElement.querySelector('#dashboard-month');
    const months = Array.from(monthSelect.querySelectorAll('option')).map((option: any) => option.value);

    expect(months).toEqual(Array.from({ length: 10 }, (_, index) => String(index + 1)));
  });

  it('keeps the current month blocks available even when the block starts after today', () => {
    const fixture = create({ period: 'week', anchorDate: '2026-10-01', weekBlock: 1 });

    expect(fixture.nativeElement.querySelectorAll('[data-week-block]')).toHaveLength(5);
  });

  it('emits period, anchor and week block intents', () => {
    const fixture = create({ period: 'day' });
    const periodChange = jest.fn();
    const anchorDateChange = jest.fn();
    const weekBlockChange = jest.fn();
    fixture.componentInstance.periodChange.subscribe(periodChange);
    fixture.componentInstance.anchorDateChange.subscribe(anchorDateChange);
    fixture.componentInstance.weekBlockChange.subscribe(weekBlockChange);

    fixture.nativeElement.querySelector('[data-period="week"]').click();

    const weekFixture = create({ period: 'week', anchorDate: '2026-10-29', weekBlock: 5 });
    weekFixture.componentInstance.anchorDateChange.subscribe(anchorDateChange);
    weekFixture.componentInstance.weekBlockChange.subscribe(weekBlockChange);
    const monthSelect = weekFixture.nativeElement.querySelector('#dashboard-month') as HTMLSelectElement;
    monthSelect.value = '9';
    monthSelect.dispatchEvent(new Event('change'));
    weekFixture.nativeElement.querySelector('[data-week-block="2"]').click();

    const dayFixture = create({ period: 'day' });
    dayFixture.componentInstance.anchorDateChange.subscribe(anchorDateChange);
    const dateInput = dayFixture.nativeElement.querySelector('input[type="date"]') as HTMLInputElement;
    dateInput.value = '2026-10-20';
    dateInput.dispatchEvent(new Event('change'));

    expect(periodChange).toHaveBeenCalledWith('week');
    expect(anchorDateChange).toHaveBeenCalledWith('2026-09-01');
    expect(weekBlockChange).toHaveBeenCalledWith(2);
    expect(anchorDateChange).toHaveBeenCalledWith('2026-10-20');
  });

  it('exposes a reusable control class with a 44px minimum height', () => {
    const fixture = create({ period: 'week', anchorDate: '2026-10-29', weekBlock: 5 });
    const controls = fixture.nativeElement.querySelectorAll('.filter-control');

    expect(controls.length).toBeGreaterThan(0);
    controls.forEach((control: HTMLElement) => {
      expect(control.classList.contains('filter-control')).toBe(true);
    });
  });
});
