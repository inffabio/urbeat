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

  it('accepts an overnight overlap between an early and a late shift on the same day', async () => {
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

    expect(component.validateDay('segunda')).toBeNull();
  });

  it('accepts two overlapping shifts on the same day', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '09:00', endTime: '13:00' },
          { startTime: '12:00', endTime: '15:00' },
        ],
      },
    ]);

    expect(component.validateDay('segunda')).toBeNull();
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

  it('saves a schedule whose overnight shift overlaps the next day', async () => {
    await load([
      { dayOfWeek: 0, isOpen: true, shifts: [{ startTime: '23:00', endTime: '01:00' }] },
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '00:30', endTime: '02:00' }] },
    ]);

    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).toHaveBeenCalledTimes(1);
    const [, request] = storeServiceMock.upsertStoreBusinessHours.mock.calls[0] as [
      string,
      { items: Array<{ dayOfWeek: number; shifts: Array<{ startTime: string; endTime: string }> }> },
    ];
    expect(request.items.find(item => item.dayOfWeek === 0)?.shifts)
      .toEqual([{ startTime: '23:00', endTime: '01:00' }]);
    expect(request.items.find(item => item.dayOfWeek === 1)?.shifts)
      .toEqual([{ startTime: '00:30', endTime: '02:00' }]);
    expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    expect(component.hasChanges()).toBe(false);
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

    function shiftSlots(dayId: string): Array<[string, string]> {
      return component.schedule()[dayId].shifts.map(shift => [shift.startTime, shift.endTime]);
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

    it('overwrites the matching position and preserves the other destination shifts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00'], ['22:00', '23:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['09:00-12:00', '22:00-23:00']);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00', '14:00-18:00']);
      expect(component.hasChanges()).toBe(true);
      expect(component.isCopyModalOpen()).toBe(false);
    });

    it.each([0, 1, 2])('overwrites only the position at index %i', async (index) => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '19:00'], ['19:00', '20:00'], ['20:00', '21:00']] },
      ));

      component.openCopyModal('segunda', index);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const source = ['08:00-10:00', '11:00-13:00', '14:00-16:00'];
      const expected = ['18:00-19:00', '19:00-20:00', '20:00-21:00'];
      expected[index] = source[index];
      expect(shiftTimes('terca')).toEqual(expected);
      expect(component.hasChanges()).toBe(true);
    });

    it('preserves the destination key and copies backend times into the matching position', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00:30.2500000', '12:30:45.5000000'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      const destinationKeys = component.schedule().terca.shifts.map(shift => shift.key);
      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const destinationShifts = component.schedule().terca.shifts;
      expect(destinationShifts).toHaveLength(1);
      expect(destinationShifts[0].startTime).toBe('08:00:30.2500000');
      expect(destinationShifts[0].endTime).toBe('12:30:45.5000000');
      expect(destinationShifts[0].key).toBe(destinationKeys[0]);
    });

    it('uses a snapshot of the shift taken when the modal was opened', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00'], ['22:00', '23:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.updateTime('segunda', 0, 'startTime', '10:00');
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['09:00', '12:00'], ['22:00', '23:00']]);
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

    it('merges into each selected destination independently', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['19:00', '22:00']] },
        { dayOfWeek: 3, shifts: [['10:00', '11:00']] },
      ));

      component.openCopyModal('segunda', 1);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['08:00-10:00', '14:00-18:00', '19:00-22:00']);
      expect(shiftTimes('quarta')).toEqual(['10:00-11:00', '14:00-18:00']);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00', '14:00-18:00']);
      expect(component.hasChanges()).toBe(true);
    });

    it('keeps the destination day open and the origin untouched', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00'], ['22:00', '23:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(component.schedule().terca.isOpen).toBe(true);
      expect(component.schedule().terca.shifts).toHaveLength(2);
      expect(shiftTimes('terca')).toEqual(['09:00-12:00', '22:00-23:00']);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00']);
      expect(component.copySourceShift()).toBeNull();
      expect(component.copyTargets().size).toBe(0);
    });

    it('accepts a copied shift that overlaps the destination previous overnight shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['00:30-02:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Turno 1 copiado para 1 dia.');
    });

    it('does not show grouped warnings when destinations are merged', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['11:00', '13:00']] },
        { dayOfWeek: 3, shifts: [['18:00', '22:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.confirmCopy();

      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
      expect(toastServiceMock.showInfo).toHaveBeenCalledTimes(1);
      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Turno 1 copiado para 2 dias.');
    });

    it('appends the copied shift as the second shift when the destination has only one', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['18:00', '22:00']] },
        { dayOfWeek: 2, shifts: [['09:00', '13:00']] },
      ));

      component.openCopyModal('segunda', 1);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['09:00', '13:00'], ['18:00', '22:00']]);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('creates empty intermediate shifts until the copied position', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
      ));

      component.openCopyModal('segunda', 2);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['18:00', '20:00'], ['', ''], ['14:00', '16:00']]);
      expect(component.validateDay('terca')).toBe('Preencha início e fim de todos os turnos.');
      expect(component.hasChanges()).toBe(true);
    });

    it('blocks saving until the intermediate shifts are filled', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
      ));

      component.openCopyModal('segunda', 2);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      await component.saveDraft();

      expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
      expect(toastServiceMock.showGrouped).toHaveBeenCalled();
      const [errors] = toastServiceMock.showGrouped.mock.calls[0] as [Array<{ type: string; text: string }>];
      expect(errors.some(error => error.text.includes('Preencha início e fim de todos os turnos.'))).toBe(true);
    });

    it('creates the first shift of an open destination that has none', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['09:00', '12:00']]);
      expect(component.hasChanges()).toBe(true);
    });

    it('preserves the destination shift keys and creates keys only for new positions', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['08:00', '10:00']] },
      ));

      const sourceKeys = component.schedule().segunda.shifts.map(shift => shift.key);
      const destinationKeys = component.schedule().terca.shifts.map(shift => shift.key);

      component.openCopyModal('segunda', 1);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const shifts = component.schedule().terca.shifts;
      expect(shifts).toHaveLength(2);
      expect(shifts[0].key).toBe(destinationKeys[0]);
      expect(shifts[1].key).toBeTruthy();
      expect(destinationKeys).not.toContain(shifts[1].key);
      expect(sourceKeys).not.toContain(shifts[1].key);
    });

    it('does not call the API during copy', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
      expect(component.hasChanges()).toBe(true);
    });

    it('overwrites the first position of a destination that already has an overnight shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['00:30', '02:00']] },
        { dayOfWeek: 2, shifts: [['23:00', '01:00'], ['02:00', '03:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['00:30-02:00', '23:00-01:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('merges a copied overnight shift even when it overlaps the next day', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00'], ['21:00', '22:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['23:00-01:00', '21:00-22:00']);
      expect(shiftTimes('quarta')).toEqual(['00:30-02:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('merges into Monday even when the Sunday overnight shift ends after midnight', async () => {
      await load(hours(
        { dayOfWeek: 0, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 1, shifts: [['18:00', '20:00'], ['21:00', '22:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('segunda');
      component.confirmCopy();

      expect(shiftTimes('segunda')).toEqual(['00:30-02:00', '21:00-22:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('merges when the previous day overnight shift ends before the copy starts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['02:00', '04:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['02:00-04:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('merges an overnight shift when the next day already has a shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['02:00', '04:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['23:00-01:00']);
      expect(shiftTimes('quarta')).toEqual(['02:00-04:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('keeps five shifts and overwrites the copied position', async () => {
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
      expect(shiftTimes('terca')).toEqual([
        '09:00-10:00', '12:00-13:00', '13:00-14:00', '14:00-15:00', '15:00-16:00',
      ]);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('rejects the copy when the position exceeds the maximum of five shifts', async () => {
      await load(hours(
        {
          dayOfWeek: 1,
          shifts: [
            ['08:00', '09:00'], ['09:00', '10:00'], ['10:00', '11:00'],
            ['11:00', '12:00'], ['12:00', '13:00'], ['13:00', '14:00'],
          ],
        },
        { dayOfWeek: 2, shifts: [['10:00', '11:00']] },
      ));

      component.openCopyModal('segunda', 5);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Máximo de 5 turnos por dia.');
      expect(component.hasChanges()).toBe(false);
      expect(component.isCopyModalOpen()).toBe(true);
      expect(shiftTimes('terca')).toEqual(['10:00-11:00']);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
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
      component.schedule.update(schedule => ({
        ...schedule,
        terca: { isOpen: false, shifts: schedule.terca.shifts },
        quarta: { isOpen: false, shifts: schedule.quarta.shifts },
      }));
      component.confirmCopy();

      expect(component.hasChanges()).toBe(false);
      expect(component.isCopyModalOpen()).toBe(false);
      expect(component.copyTargets().size).toBe(0);
      expect(component.copySourceShift()).toBeNull();
      expect(shiftTimes('terca')).toEqual(['10:00-11:00']);
      expect(shiftTimes('quarta')).toEqual(['08:00-09:30']);
      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Nenhum dia recebeu o turno.');
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
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
