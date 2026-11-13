import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { of } from 'rxjs';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { StoreHoursPageComponent } from './store-hours-page.component';

describe('StoreHoursPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn().mockReturnValue(of({ id: 'store-123' })),
    getStoreBusinessHours: jest.fn().mockReturnValue(of({ items: [] })),
    upsertStoreBusinessHours: jest.fn(),
  };

  const toastServiceMock = {
    showError: jest.fn(),
    showInfo: jest.fn(),
    showGrouped: jest.fn(),
  };

  beforeEach(async () => {
    jest.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [StoreHoursPageComponent],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
      ],
    }).compileComponents();
  });

  function createComponent(): ComponentFixture<StoreHoursPageComponent> {
    const fixture = TestBed.createComponent(StoreHoursPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  // Segunda-feira is index 1 because Domingo is the first rendered day.
  function mondayRow(fixture: ComponentFixture<StoreHoursPageComponent>): HTMLElement {
    return fixture.nativeElement.querySelectorAll('.day-row')[1] as HTMLElement;
  }

  function setMondayShifts(
    fixture: ComponentFixture<StoreHoursPageComponent>,
    shifts: Array<{ startTime: string; endTime: string }>,
  ): void {
    fixture.componentInstance.schedule.update(schedule => ({
      ...schedule,
      segunda: { isOpen: true, shifts },
    }));
    fixture.detectChanges();
  }

  it('renders seven day rows with shift controls for open days', () => {
    const fixture = createComponent();
    setMondayShifts(fixture, [
      { startTime: '11:00', endTime: '14:30' },
      { startTime: '18:00', endTime: '23:00' },
    ]);

    const rows = fixture.nativeElement.querySelectorAll('.day-row');
    expect(rows.length).toBe(7);
    expect(mondayRow(fixture).querySelectorAll('.shift-row').length).toBe(2);
  });

  it('renders one copy action per shift, including the first, and no day-level copy action', () => {
    const fixture = createComponent();
    setMondayShifts(fixture, [
      { startTime: '08:00', endTime: '10:00' },
      { startTime: '11:00', endTime: '13:00' },
      { startTime: '14:00', endTime: '16:00' },
    ]);

    const row = mondayRow(fixture);
    const copyButtons = Array.from(row.querySelectorAll('.shift-row .btn-copy-shift')) as HTMLButtonElement[];

    expect(copyButtons).toHaveLength(3);
    expect(copyButtons.map(button => button.getAttribute('aria-label'))).toEqual([
      'Copiar turno 1 de Segunda-feira',
      'Copiar turno 2 de Segunda-feira',
      'Copiar turno 3 de Segunda-feira',
    ]);
    expect(copyButtons.every(button => button.type === 'button')).toBe(true);
    expect(copyButtons.every(button => !!button.getAttribute('title'))).toBe(true);

    expect(row.querySelector('.day-actions')).toBeNull();
    expect(row.querySelector('.btn-copy')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.day-actions').length).toBe(0);
    expect(fixture.nativeElement.querySelectorAll('.btn-copy').length).toBe(0);
  });

  it('shows a visual tooltip for copy and remove actions without replacing the accessible name', () => {
    const fixture = createComponent();
    setMondayShifts(fixture, [
      { startTime: '08:00', endTime: '10:00' },
      { startTime: '11:00', endTime: '13:00' },
    ]);

    const row = mondayRow(fixture);
    const copyButtons = Array.from(row.querySelectorAll('.btn-copy-shift')) as HTMLButtonElement[];
    const removeButtons = Array.from(row.querySelectorAll('.btn-remove-shift')) as HTMLButtonElement[];

    expect(copyButtons).toHaveLength(2);
    expect(removeButtons).toHaveLength(2);

    for (const button of copyButtons) {
      const tooltip = button.querySelector('.shift-tooltip') as HTMLElement | null;
      expect(tooltip).not.toBeNull();
      expect(tooltip?.textContent?.trim()).toBe('Copiar turno');
      expect(tooltip?.getAttribute('aria-hidden')).toBe('true');
      expect(button.getAttribute('aria-label')).toBeTruthy();
      expect(button.getAttribute('title')).toBeTruthy();
    }

    for (const button of removeButtons) {
      const tooltip = button.querySelector('.shift-tooltip') as HTMLElement | null;
      expect(tooltip).not.toBeNull();
      expect(tooltip?.textContent?.trim()).toBe('Excluir turno');
      expect(tooltip?.getAttribute('aria-hidden')).toBe('true');
      expect(button.getAttribute('aria-label')).toMatch(/^Remover turno \d+ de /);
      expect(button.getAttribute('title')).toMatch(/^Remover turno \d+ de /);
    }
  });

  it('keeps the shift tooltip non-interactive, keyboard reachable and respecting reduced motion', () => {
    const styles = readFileSync(resolve(__dirname, 'store-hours-page.component.scss'), 'utf8');
    const tooltip = styles.match(/\.shift-tooltip\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const reducedMotion = styles.match(/@media \(prefers-reduced-motion: reduce\)\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';

    expect(tooltip).toContain('position: absolute');
    expect(tooltip).toContain('pointer-events: none');
    expect(tooltip).toContain('visibility: hidden');
    expect(tooltip).toMatch(/background: var\(--app-ink\)/);
    expect(tooltip).toContain('white-space: nowrap');

    expect(styles).toMatch(/\.btn-copy-shift:focus-visible \.shift-tooltip/);
    expect(styles).toMatch(/\.btn-remove-shift:focus-visible \.shift-tooltip/);
    expect(styles).toMatch(/@media \(hover: hover\) and \(pointer: fine\)/);

    expect(reducedMotion).toContain('.shift-tooltip { transition: none; }');
  });

  it('opens the copy modal for the clicked shift with its day and position', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;
    setMondayShifts(fixture, [
      { startTime: '08:00', endTime: '10:00' },
      { startTime: '11:00', endTime: '13:00' },
      { startTime: '14:00', endTime: '16:00' },
    ]);

    const copyButtons = mondayRow(fixture).querySelectorAll('.btn-copy-shift') as NodeListOf<HTMLButtonElement>;
    copyButtons[1].click();
    fixture.detectChanges();

    expect(component.copySourceShift()).toMatchObject({
      dayId: 'segunda',
      shiftIndex: 1,
      shift: { startTime: '11:00', endTime: '13:00' },
    });
    expect(component.copySourceShiftNumber()).toBe(2);
    expect(component.copySourceDayLabel()).toBe('Segunda-feira');
    expect(component.isCopyModalOpen()).toBe(true);
  });

  it('fills the copied position of an empty destination and leaves earlier positions pending', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;
    setMondayShifts(fixture, [
      { startTime: '08:00', endTime: '10:00' },
      { startTime: '11:00', endTime: '13:00' },
    ]);
    component.schedule.update(schedule => ({
      ...schedule,
      terca: { isOpen: true, shifts: [] },
    }));
    fixture.detectChanges();

    const copyButtons = mondayRow(fixture).querySelectorAll('.btn-copy-shift') as NodeListOf<HTMLButtonElement>;
    copyButtons[1].click();
    fixture.detectChanges();
    component.toggleCopyTarget('terca');
    component.confirmCopy();

    expect(component.schedule().terca.shifts.map(shift => [shift.startTime, shift.endTime]))
      .toEqual([['', ''], ['11:00', '13:00']]);
    expect(component.validateDay('terca')).toBe('Preencha início e fim de todos os turnos.');
    expect(component.schedule().segunda.shifts).toHaveLength(2);
  });

  it('overwrites only the matching position and keeps the other destination shifts', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;
    setMondayShifts(fixture, [{ startTime: '11:00', endTime: '13:00' }]);
    component.schedule.update(schedule => ({
      ...schedule,
      terca: {
        isOpen: true,
        shifts: [
          { startTime: '08:00', endTime: '09:00' },
          { startTime: '15:00', endTime: '18:00' },
        ],
      },
    }));
    fixture.detectChanges();

    component.openCopyModal('segunda', 0);
    component.toggleCopyTarget('terca');
    component.confirmCopy();

    expect(component.schedule().terca.shifts.map(shift => [shift.startTime, shift.endTime]))
      .toEqual([['11:00', '13:00'], ['15:00', '18:00']]);
    expect(component.schedule().segunda.shifts).toHaveLength(1);
  });

  it('tells the user that the other shifts are kept', () => {
    const template = readFileSync(resolve(__dirname, 'store-hours-page.component.html'), 'utf8');

    expect(template).toContain(
      'O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.',
    );
    expect(template).toContain('class="copy-modal-note"');
  });

  it('disables every per-shift copy action while sync all is enabled', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;
    setMondayShifts(fixture, [
      { startTime: '08:00', endTime: '10:00' },
      { startTime: '11:00', endTime: '13:00' },
    ]);

    component.toggleSyncAll();
    fixture.detectChanges();

    const copyButtons = Array.from(mondayRow(fixture).querySelectorAll('.btn-copy-shift')) as HTMLButtonElement[];
    expect(copyButtons).toHaveLength(2);
    expect(copyButtons.every(button => button.disabled)).toBe(true);
  });
});
