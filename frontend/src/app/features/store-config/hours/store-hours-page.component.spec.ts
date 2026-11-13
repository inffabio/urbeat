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

  it('copies only the clicked shift to an open target day', () => {
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

    expect(component.schedule().terca.shifts.map(shift => `${shift.startTime}-${shift.endTime}`))
      .toEqual(['11:00-13:00']);
    expect(component.schedule().segunda.shifts).toHaveLength(2);
  });

  it('replaces every shift of the destination with the copied shift', () => {
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

    expect(component.schedule().terca.shifts.map(shift => `${shift.startTime}-${shift.endTime}`))
      .toEqual(['11:00-13:00']);
    expect(component.schedule().segunda.shifts).toHaveLength(1);
  });

  it('tells the user that the selected days will be replaced', () => {
    const template = readFileSync(resolve(__dirname, 'store-hours-page.component.html'), 'utf8');

    expect(template).toContain('Os horários atuais dos dias selecionados serão substituídos.');
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
