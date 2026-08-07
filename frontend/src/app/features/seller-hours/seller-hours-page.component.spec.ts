import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerHoursPageComponent } from './seller-hours-page.component';

describe('SellerHoursPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn().mockReturnValue(of({ id: 'store-123', businessHours: [] })),
    getStoreBusinessHours: jest.fn().mockReturnValue(of({ items: [{ dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] }] })),
    upsertBusinessHours: jest.fn(),
  };

  const toastServiceMock = {
    showError: jest.fn(),
    showSuccess: jest.fn(),
  };

  beforeEach(async () => {
    jest.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [SellerHoursPageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
      ],
    }).compileComponents();
  });

  it('asks for confirmation before removing a shift', () => {
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    const component = fixture.componentInstance;

    component.schedule.set({
      segunda: {
        isOpen: true,
        shifts: [{ startTime: '08:00', endTime: '12:00' }],
      },
    } as any);

    component.removeShift('segunda', 0);

    expect(confirmSpy).toHaveBeenCalledWith('Excluir o turno 1 de segunda-feira?');
    expect(component.schedule().segunda.shifts).toHaveLength(1);
  });

  it('restores the last loaded schedule when cancelling changes', () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    const component = fixture.componentInstance;

    fixture.detectChanges();
    component.updateTime('segunda', 0, 'startTime', '09:00');

    component.cancelChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00');
    expect(component.hasChanges()).toBe(false);
  });

  it('segunda-feira aberta com dois turnos exibe dois .shift-group, .remove-shift em cada turno, botão copiar como último filho de .settings-row, e dias de Segunda a Domingo', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const component = fixture.componentInstance;
    component.schedule.update(s => ({
      ...s,
      segunda: {
        isOpen: true,
        shifts: [
          { startTime: '08:00', endTime: '12:00' },
          { startTime: '13:00', endTime: '18:00' },
        ],
      },
    }));

    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('.settings-row');
    expect(rows.length).toBeGreaterThanOrEqual(7);

    const segundaRow = rows[0];
    expect(segundaRow).toBeTruthy();

    const shiftGroups = segundaRow.querySelectorAll('.shift-group');
    expect(shiftGroups.length).toBe(2);

    for (const group of Array.from(shiftGroups)) {
      const inputs = group.querySelectorAll('input[type="time"]');
      expect(inputs.length).toBe(2);

      const removeShift = group.querySelector('.remove-shift');
      expect(removeShift).toBeTruthy();
    }

    const lastChild = segundaRow.lastElementChild;
    expect(lastChild).toBeTruthy();
    expect(lastChild!.querySelector('ion-icon[name="copy-outline"]')).toBeTruthy();

    const dayLabels = fixture.nativeElement.querySelectorAll('.settings-row .day-summary strong');
    expect(dayLabels.length).toBe(7);
    expect(dayLabels[0].textContent!.trim()).toBe('Segunda-feira');
    expect(dayLabels[dayLabels.length - 1].textContent!.trim()).toBe('Domingo');
  });
});
