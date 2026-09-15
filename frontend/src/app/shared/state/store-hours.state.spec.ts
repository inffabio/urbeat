import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

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
});
