import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { of } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerHoursPageComponent } from './seller-hours-page.component';

describe('SellerHoursPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn().mockReturnValue(of({ id: 'store-123', businessHours: [] })),
    getStoreBusinessHours: jest.fn().mockReturnValue(of({ items: [{ dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] }] })),
    upsertStoreBusinessHours: jest.fn(),
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

  it('shows shifts in ascending opening-time order after loading', async () => {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(of({
      items: [{
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '18:00', endTime: '22:00' },
          { startTime: '08:00', endTime: '12:00' },
        ],
      }],
    }));

    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.schedule().segunda.shifts.map(shift => shift.startTime))
      .toEqual(['08:00', '18:00']);
  });

  it('re-enables saving after a new edit following a successful save', async () => {
    storeServiceMock.upsertStoreBusinessHours.mockReturnValue(of({ items: [] }));
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const component = fixture.componentInstance;
    component.updateTime('segunda', 0, 'startTime', '09:00');
    await component.saveDraft();
    expect(component.hasChanges()).toBe(false);
    expect(component.isSaving()).toBe(false);

    component.updateTime('segunda', 0, 'startTime', '10:00');
    expect(component.hasChanges()).toBe(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.btn-primary-app').disabled).toBe(false);
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

  it('lays each day out as a compact grid with copy in the final column', () => {
    const styles = readFileSync(resolve(__dirname, 'seller-hours-page.component.scss'), 'utf8');
    const row = styles.match(/\.settings-row\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';

    expect(row).toContain('display: grid');
    expect(row).toMatch(/grid-template-columns:\s*156px minmax\(0, 1fr\) 44px/);
    expect(styles).toMatch(/\.copy-action\s*\{\s*justify-self:\s*end/);
  });

  it('keeps desktop time fields between 100 and 112px and 44px touch targets', () => {
    const styles = readFileSync(resolve(__dirname, 'seller-hours-page.component.scss'), 'utf8');
    const field = styles.match(/\.time-field\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const width = Number(field.match(/width:\s*(\d+)px/)?.[1]);

    expect(width).toBeGreaterThanOrEqual(100);
    expect(width).toBeLessThanOrEqual(112);

    const actions = styles.match(/\.remove-shift,\s*\.copy-action\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    expect(actions).toMatch(/height:\s*44px/);
  });

  it('respects prefers-reduced-motion for the switch and inputs', () => {
    const styles = readFileSync(resolve(__dirname, 'seller-hours-page.component.scss'), 'utf8');

    expect(styles).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{[\s\S]*\.switch-app[\s\S]*transition:\s*none/);
  });

  it('associates each time input with a label and accessible name', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const inputs = Array.from(
      fixture.nativeElement.querySelectorAll('input[type="time"]') as NodeListOf<HTMLInputElement>,
    );
    expect(inputs.length).toBeGreaterThan(0);

    for (const input of inputs) {
      expect(input.getAttribute('aria-label')).toBeTruthy();
      expect(input.id).toBeTruthy();
      const label = fixture.nativeElement.querySelector(`label[for="${input.id}"]`);
      expect(label).toBeTruthy();
    }
  });

  it('exposes the day switch as an accessible switch control', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const switchEl = fixture.nativeElement.querySelector('.switch-line[role="switch"]') as HTMLElement;
    expect(switchEl).toBeTruthy();
    expect(switchEl.getAttribute('tabindex')).toBe('0');
    expect(switchEl.hasAttribute('aria-checked')).toBe(true);
    expect(switchEl.getAttribute('aria-label')).toBeTruthy();
  });
});

describe('SellerHoursPageComponent wizard separation', () => {
  it('does not inherit wizard navigation from the shared state', () => {
    const component = Object.create(SellerHoursPageComponent.prototype) as unknown as Record<string, unknown>;

    expect(component['goNext']).toBeUndefined();
    expect(component['goBack']).toBeUndefined();
  });
});
