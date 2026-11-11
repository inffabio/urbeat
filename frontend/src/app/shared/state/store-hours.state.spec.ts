import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { StoreHoursState } from './store-hours.state';

@Component({ standalone: true, template: '' })
class TestHoursHostComponent extends StoreHoursState {}

describe('StoreHoursState', () => {
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

  let fixture: ComponentFixture<TestHoursHostComponent>;
  let component: TestHoursHostComponent;

  async function load(items: unknown[]): Promise<void> {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(of({ items }));
    fixture = TestBed.createComponent(TestHoursHostComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  }

  beforeEach(async () => {
    jest.clearAllMocks();
    storeServiceMock.upsertStoreBusinessHours.mockReturnValue(of({ items: [] }));

    await TestBed.configureTestingModule({
      imports: [TestHoursHostComponent],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
      ],
    }).compileComponents();
  });

  it('preserves shift ids returned by the backend when loading hours', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ id: 'shift-1', startTime: '08:00', endTime: '12:00' }] },
    ]);

    expect(component.schedule().segunda.shifts[0].id).toBe('shift-1');
  });

  it('never replaces empty shift times with 09:00/18:00 in the request', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ id: 'shift-1', startTime: '', endTime: '' }] },
    ]);

    const request = (component as unknown as { buildRequest(): { items: Array<{ dayOfWeek: number; shifts: Array<{ startTime: string; endTime: string }> }> } }).buildRequest();
    const monday = request.items.find((item) => item.dayOfWeek === 1);

    expect(monday?.shifts[0].startTime).toBe('');
    expect(monday?.shifts[0].endTime).toBe('');
  });

  it('blocks saveDraft and reports errors when a day has an incomplete shift', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] },
    ]);

    component.updateTime('segunda', 0, 'endTime', '');
    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
    expect(component.hasChanges()).toBe(true);
    expect(toastServiceMock.showGrouped).toHaveBeenCalled();
  });

  it('saves a valid schedule and clears the changes flag', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] },
    ]);

    component.updateTime('segunda', 0, 'startTime', '09:00');
    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).toHaveBeenCalledTimes(1);
    expect(component.hasChanges()).toBe(false);
  });

  it('keeps shift indices stable while editing a time so the edited shift is not reordered', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { id: 'shift-a', startTime: '08:00', endTime: '12:00' },
          { id: 'shift-b', startTime: '13:00', endTime: '18:00' },
        ],
      },
    ]);

    component.updateTime('segunda', 1, 'startTime', '07:00');

    const shifts = component.schedule().segunda.shifts;
    expect(shifts[0].id).toBe('shift-a');
    expect(shifts[0].startTime).toBe('08:00');
    expect(shifts[1].id).toBe('shift-b');
    expect(shifts[1].startTime).toBe('07:00');
    expect(shifts[1].endTime).toBe('18:00');
  });

  it('does not move a shift to the top when its time is cleared', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '08:00', endTime: '12:00' },
          { startTime: '13:00', endTime: '18:00' },
        ],
      },
    ]);

    component.updateTime('segunda', 1, 'startTime', '');

    const shifts = component.schedule().segunda.shifts;
    expect(shifts[0].startTime).toBe('08:00');
    expect(shifts[1].startTime).toBe('');
    expect(component.validateDay('segunda')).toBe('Preencha início e fim de todos os turnos.');
  });

  it('persists the edited shift values through saveDraft', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '09:00', endTime: '10:00' },
          { startTime: '11:00', endTime: '12:00' },
        ],
      },
    ]);

    component.updateTime('segunda', 0, 'endTime', '09:30');
    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).toHaveBeenCalledTimes(1);
    const [storeId, request] = storeServiceMock.upsertStoreBusinessHours.mock.calls[0] as [
      string,
      { items: Array<{ dayOfWeek: number; shifts: Array<{ startTime: string; endTime: string }> }> },
    ];
    expect(storeId).toBe('store-123');
    const monday = request.items.find((item) => item.dayOfWeek === 1);
    expect(monday?.shifts).toEqual([
      { startTime: '09:00', endTime: '09:30' },
      { startTime: '11:00', endTime: '12:00' },
    ]);
    expect(component.hasChanges()).toBe(false);
  });

  it('marks the schedule dirty when toggling sync all', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] },
    ]);

    expect(component.hasChanges()).toBe(false);
    component.toggleSyncAll();

    expect(component.hasChanges()).toBe(true);
  });

  it('detects the backend overnight overlap between an early and a late shift on the same day', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '00:30', endTime: '02:00' },
          { startTime: '23:00', endTime: '01:00' },
        ],
      },
    ]);

    expect(component.validateDay('segunda')).not.toBeNull();
  });

  it('rejects a shift whose start time equals its end time', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '09:00', endTime: '18:00' }] },
    ]);

    component.updateTime('segunda', 0, 'endTime', '09:00');

    expect(component.validateDay('segunda')).toBe('Início e fim do turno não podem ser iguais.');
  });

  it('rejects time values that are not backend-compatible', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '09:00', endTime: '18:00' }] },
    ]);

    for (const invalid of ['25:00', '9:00', '09:70', '0900', 'ab:cd']) {
      component.updateTime('segunda', 0, 'startTime', invalid);
      expect(component.validateDay('segunda')).toBe('Informe horários válidos no formato HH:MM.');
    }
  });

  it('accepts backend-compatible boundary times', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '09:00', endTime: '18:00' }] },
    ]);

    component.updateTime('segunda', 0, 'startTime', '00:00');
    component.updateTime('segunda', 0, 'endTime', '23:59');

    expect(component.validateDay('segunda')).toBeNull();
  });

  it('blocks saveDraft when a shift starts and ends at the same time', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '09:00', endTime: '18:00' }] },
    ]);

    component.updateTime('segunda', 0, 'endTime', '09:00');
    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
    expect(toastServiceMock.showGrouped).toHaveBeenCalled();
  });

  it('reports an overnight shift that conflicts with the next day, including Sunday to Monday', async () => {
    await load([
      { dayOfWeek: 0, isOpen: true, shifts: [{ startTime: '23:00', endTime: '01:00' }] },
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '00:30', endTime: '02:00' }] },
    ]);

    const conflicts = component.validateCrossDayConflicts();

    expect(conflicts.length).toBeGreaterThan(0);
    expect(conflicts[0]).toContain('Domingo');
    expect(conflicts[0]).toContain('Segunda');
  });

  it('does not report a next-day shift that starts after the overnight shift ends', async () => {
    await load([
      { dayOfWeek: 0, isOpen: true, shifts: [{ startTime: '23:00', endTime: '01:00' }] },
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '02:00', endTime: '03:00' }] },
    ]);

    expect(component.validateCrossDayConflicts()).toEqual([]);
  });

  it('blocks saveDraft when there is a cross-day conflict', async () => {
    await load([
      { dayOfWeek: 0, isOpen: true, shifts: [{ startTime: '23:00', endTime: '01:00' }] },
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '00:30', endTime: '02:00' }] },
    ]);

    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
    expect(toastServiceMock.showGrouped).toHaveBeenCalled();
  });

  it('preserves backend time values with seconds and fractions when loading', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [{ startTime: '08:00:30.2500000', endTime: '12:30:45.5000000' }],
      },
    ]);

    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00:30.2500000');
    expect(component.schedule().segunda.shifts[0].endTime).toBe('12:30:45.5000000');
    expect(component.validateDay('segunda')).toBeNull();
  });

  it('keeps seconds and fractions of the unedited shift when another shift is edited', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { id: 'shift-a', startTime: '08:00:30.2500000', endTime: '12:15:45.5000000' },
          { id: 'shift-b', startTime: '13:00:10.1250000', endTime: '18:00:00.7500000' },
        ],
      },
    ]);

    component.updateTime('segunda', 1, 'startTime', '14:00');
    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).toHaveBeenCalledTimes(1);
    const [, request] = storeServiceMock.upsertStoreBusinessHours.mock.calls[0] as [
      string,
      { items: Array<{ dayOfWeek: number; shifts: Array<{ startTime: string; endTime: string }> }> },
    ];
    const monday = request.items.find((item) => item.dayOfWeek === 1);

    expect(monday?.shifts).toEqual([
      { startTime: '08:00:30.2500000', endTime: '12:15:45.5000000' },
      { startTime: '14:00', endTime: '18:00:00.7500000' },
    ]);
  });

  it('does not submit hidden shifts for a closed day', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] },
    ]);

    component.updateTime('segunda', 0, 'endTime', '');
    component.toggleDay('segunda');
    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).toHaveBeenCalledTimes(1);
    const [, request] = storeServiceMock.upsertStoreBusinessHours.mock.calls[0] as [
      string,
      { items: Array<{ dayOfWeek: number; isOpen: boolean; shifts: unknown[] }> },
    ];
    const monday = request.items.find((item) => item.dayOfWeek === 1);

    expect(monday?.isOpen).toBe(false);
    expect(monday?.shifts).toEqual([]);
  });

  it('blocks adding a shift when the current end time is invalid', async () => {
    await load([
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] },
    ]);

    component.updateTime('segunda', 0, 'endTime', 'ab:cd');
    component.addShift('segunda');

    expect(component.schedule().segunda.shifts).toHaveLength(1);
    expect(component.schedule().segunda.shifts[0].endTime).toBe('ab:cd');
    expect(toastServiceMock.showError).toHaveBeenCalledWith('Corrija o horário do turno atual antes de adicionar outro.');
  });

  it('limits each day to five shifts', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '00:00', endTime: '01:00' },
          { startTime: '01:00', endTime: '02:00' },
          { startTime: '02:00', endTime: '03:00' },
          { startTime: '03:00', endTime: '04:00' },
          { startTime: '04:00', endTime: '05:00' },
        ],
      },
    ]);

    expect(component.canAddShift('segunda')).toBe(false);

    component.addShift('segunda');
    expect(component.schedule().segunda.shifts).toHaveLength(5);

    component.schedule.update((schedule) => ({
      ...schedule,
      segunda: {
        isOpen: true,
        shifts: [
          { startTime: '00:00', endTime: '01:00' },
          { startTime: '01:00', endTime: '02:00' },
          { startTime: '02:00', endTime: '03:00' },
          { startTime: '03:00', endTime: '04:00' },
          { startTime: '04:00', endTime: '05:00' },
          { startTime: '05:00', endTime: '06:00' },
        ],
      },
    }));

    expect(component.validateDay('segunda')).toBe('Máximo de 5 turnos por dia.');
  });

  it('keeps the schedule and blocks saving when loading hours fails', async () => {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(throwError(() => new Error('offline')));

    fixture = TestBed.createComponent(TestHoursHostComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.loadFailed()).toBe(true);

    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
    expect(toastServiceMock.showError).toHaveBeenCalled();
  });

  it('can reload hours after a failed load', async () => {
    storeServiceMock.getStoreBusinessHours
      .mockReturnValueOnce(throwError(() => new Error('offline')))
      .mockReturnValueOnce(of({
        items: [{ dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] }],
      }));

    fixture = TestBed.createComponent(TestHoursHostComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

    component.reload();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.loadFailed()).toBe(false);
    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00');
  });

  describe('individual shift copy', () => {
    type HoursItem = { dayOfWeek: number; isOpen: boolean; shifts: Array<{ startTime: string; endTime: string }> };

    function hours(...days: Array<{ dayOfWeek: number; shifts: Array<[string, string]> }>): HoursItem[] {
      return days.map(day => ({
        dayOfWeek: day.dayOfWeek,
        isOpen: true,
        shifts: day.shifts.map(([startTime, endTime]) => ({ startTime, endTime })),
      }));
    }

    function shiftTimes(dayId: string): string[] {
      return component.schedule()[dayId].shifts.map(shift => `${shift.startTime}-${shift.endTime}`);
    }

    function groupedText(): string {
      return (toastServiceMock.showGrouped.mock.calls as Array<[Array<{ text: string }>]>)
        .flatMap(([lines]) => lines.map(line => line.text))
        .join('\n');
    }

    it('lists only open days other than the origin as copy targets', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 3, shifts: [['18:00', '22:00']] },
      ));

      component.openCopyModal('segunda', 0);

      expect(component.isCopyModalOpen()).toBe(true);
      expect(component.copyTargetDays().map(day => day.id)).toEqual(['quarta']);
    });

    it('exposes the origin day label and shift number used by the modal', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 1);

      expect(component.copySourceShiftNumber()).toBe(2);
      expect(component.copySourceDayLabel()).toBe('Segunda-feira');
    });

    it('ignores an invalid day or shift index', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 4);
      component.openCopyModal('domingo', 0);

      expect(component.isCopyModalOpen()).toBe(false);
      expect(component.copySourceShift()).toBeNull();
    });

    it('copies only the first shift of a day that has two shifts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['09:00-12:00', '19:00-21:00']);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00', '14:00-18:00']);
      expect(component.hasChanges()).toBe(true);
      expect(component.isCopyModalOpen()).toBe(false);
    });

    it.each([1, 2])('copies only the shift at index %i', async (index) => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', index);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const expected = ['08:00-10:00', '11:00-13:00', '14:00-16:00'][index];
      expect(shiftTimes('terca')).toEqual([expected, '19:00-21:00'].sort());
    });

    it('clones the selected shift with a new key and preserves backend times', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00:30.2500000', '12:30:45.5000000'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      const sourceKeys = new Set(component.schedule().segunda.shifts.map(shift => shift.key));
      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const copied = component.schedule().terca.shifts.find(shift => shift.startTime === '08:00:30.2500000');
      expect(copied?.endTime).toBe('12:30:45.5000000');
      expect(copied?.key).toBeTruthy();
      expect(sourceKeys.has(copied?.key)).toBe(false);
    });

    it('uses a snapshot of the shift taken when the modal was opened', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.updateTime('segunda', 0, 'startTime', '10:00');
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['09:00-12:00', '19:00-21:00']);
    });

    it('never copies to a closed day', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));
      component.toggleDay('terca');

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(component.schedule().terca.isOpen).toBe(false);
      expect(shiftTimes('terca')).toEqual(['19:00-21:00']);
      expect(component.isCopyModalOpen()).toBe(false);
    });

    it('applies the copy to valid days and warns about each conflicting day', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['11:00', '13:00']] },
        { dayOfWeek: 3, shifts: [['18:00', '22:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.confirmCopy();

      expect(shiftTimes('quarta')).toEqual(['09:00-12:00', '18:00-22:00']);
      expect(shiftTimes('terca')).toEqual(['11:00-13:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).toHaveBeenCalled();
      expect(groupedText()).toContain('Terça-feira');
      expect(groupedText()).toContain('sobreposição');
    });

    it('blocks a copy that overlaps an overnight shift on the destination day', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['00:30', '02:00']] },
        { dayOfWeek: 2, shifts: [['23:00', '01:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['23:00-01:00']);
      expect(component.hasChanges()).toBe(false);
      expect(groupedText()).toContain('Terça-feira');
    });

    it('blocks a copied overnight shift that conflicts with the next day of the destination', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['18:00-20:00']);
      expect(component.hasChanges()).toBe(false);
      expect(groupedText()).toContain('Terça-feira');
      expect(groupedText()).toContain('sobreposição');
    });

    it('blocks a normal copy that starts before the previous day overnight shift ends', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['18:00-20:00']);
      expect(component.hasChanges()).toBe(false);
      expect(groupedText()).toContain('Terça-feira');
      expect(groupedText()).toContain('sobreposição');
    });

    it('blocks a normal copy into Monday when the Sunday overnight shift ends after midnight', async () => {
      await load(hours(
        { dayOfWeek: 0, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 1, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('segunda');
      component.confirmCopy();

      expect(shiftTimes('segunda')).toEqual(['18:00-20:00']);
      expect(component.hasChanges()).toBe(false);
      expect(groupedText()).toContain('Segunda-feira');
      expect(groupedText()).toContain('sobreposição');
    });

    it('copies a normal shift when the previous day overnight shift ends before it starts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['02:00', '04:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['02:00-04:00', '18:00-20:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('copies an overnight shift when the next day has no conflicting shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['02:00', '04:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['18:00-20:00', '23:00-01:00']);
      expect(component.hasChanges()).toBe(true);
    });

    it('does not exceed the maximum shifts per day and warns about the day at the limit', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '10:00']] },
        {
          dayOfWeek: 2,
          shifts: [['11:00', '12:00'], ['12:00', '13:00'], ['13:00', '14:00'], ['14:00', '15:00'], ['15:00', '16:00']],
        },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(component.schedule().terca.shifts).toHaveLength(5);
      expect(component.hasChanges()).toBe(false);
      expect(groupedText()).toContain('Terça-feira');
      expect(groupedText()).toContain('limite');
    });

    it('keeps the draft clean and closes the modal when no destination receives the shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['10:00', '11:00']] },
        { dayOfWeek: 3, shifts: [['08:00', '09:30']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.confirmCopy();

      expect(component.hasChanges()).toBe(false);
      expect(component.isCopyModalOpen()).toBe(false);
      expect(component.copyTargets().size).toBe(0);
      expect(component.copySourceShift()).toBeNull();
      expect(groupedText()).toContain('Terça-feira');
      expect(groupedText()).toContain('Quarta-feira');
    });

    it('requires at least one selected destination', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.confirmCopy();

      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Selecione pelo menos um dia.');
      expect(component.isCopyModalOpen()).toBe(true);
      expect(component.hasChanges()).toBe(false);
    });

    it('clears the copy state when the modal is cancelled', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.closeCopyModal();

      expect(component.isCopyModalOpen()).toBe(false);
      expect(component.copySourceShift()).toBeNull();
      expect(component.copyTargets().size).toBe(0);
      expect(component.hasChanges()).toBe(false);
      expect(shiftTimes('terca')).toEqual(['19:00-21:00']);
    });
  });
});
