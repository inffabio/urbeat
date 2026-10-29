import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { of } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerHoursPageComponent } from './seller-hours-page.component';

function fieldFor(input: HTMLInputElement): HTMLElement {
  return input.closest('.time-field') as HTMLElement;
}

function firstShiftInput(
  fixture: ComponentFixture<SellerHoursPageComponent>,
  kind: 'start' | 'end',
): HTMLInputElement {
  const mondayRow = fixture.nativeElement.querySelectorAll('.settings-row')[0] as HTMLElement;
  const fields = mondayRow.querySelectorAll<HTMLElement>('.time-field');
  const field = kind === 'start' ? fields[0] : fields[1];

  return field.querySelector('input') as HTMLInputElement;
}

describe('SellerHoursPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn().mockReturnValue(of({ id: 'store-123', businessHours: [] })),
    getStoreBusinessHours: jest.fn().mockReturnValue(of({ items: [{ dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '08:00', endTime: '12:00' }] }] })),
    upsertStoreBusinessHours: jest.fn(),
  };

  const toastServiceMock = {
    showError: jest.fn(),
    showSuccess: jest.fn(),
    showGrouped: jest.fn(),
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

  it('places the copy button inline on the first shift row before the remove button, without a second line', async () => {
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

    const segundaRow = rows[0] as HTMLElement;
    expect(segundaRow).toBeTruthy();

    const shiftGroups = segundaRow.querySelectorAll('.shift-group');
    expect(shiftGroups.length).toBe(2);

    for (const group of Array.from(shiftGroups)) {
      const inputs = group.querySelectorAll('input[role="combobox"]');
      expect(inputs.length).toBe(2);
      expect(group.querySelectorAll('.time-box ion-icon').length).toBe(2);

      const removeShift = group.querySelector('.remove-shift');
      expect(removeShift).toBeTruthy();
    }

    const firstGroup = shiftGroups[0] as HTMLElement;
    const firstGroupClasses = Array.from(firstGroup.children).map(child => child.className);
    const copyIndex = firstGroupClasses.findIndex(className => className.includes('copy-action'));
    const removeIndex = firstGroupClasses.findIndex(className => className.includes('remove-shift'));
    expect(copyIndex).toBeGreaterThanOrEqual(0);
    expect(removeIndex).toBeGreaterThan(copyIndex);

    const directCopyChild = Array.from(segundaRow.children).some(
      (child: Element) => child.classList.contains('copy-action'),
    );
    expect(directCopyChild).toBe(false);
    expect(segundaRow.querySelectorAll('.copy-action').length).toBe(1);

    const dayLabels = fixture.nativeElement.querySelectorAll('.settings-row .day-summary strong');
    expect(dayLabels.length).toBe(7);
    expect(dayLabels[0].textContent!.trim()).toBe('Segunda-feira');
    expect(dayLabels[dayLabels.length - 1].textContent!.trim()).toBe('Domingo');
  });

  it('lays each day out as a compact grid with copy and remove inline in the shift group', () => {
    const styles = readFileSync(resolve(__dirname, 'seller-hours-page.component.scss'), 'utf8');
    const row = styles.match(/\.settings-row\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';

    expect(row).toContain('display: grid');
    expect(row).toMatch(/grid-template-columns:\s*156px minmax\(0, 1fr\)/);
    expect(row).not.toMatch(/grid-template-columns:\s*156px minmax\(0, 1fr\) 44px/);

    const group = styles.match(/\.shift-group\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    expect(group).toMatch(/grid-template-columns:\s*repeat\(2,\s*minmax\(144px,\s*160px\)\)\s*44px\s*44px/);

    expect(styles).not.toMatch(/\.copy-action\s*\{\s*justify-self:\s*end/);
  });

  it('keeps desktop time fields wide enough for HH:MM and 44px touch targets', () => {
    const styles = readFileSync(resolve(__dirname, 'seller-hours-page.component.scss'), 'utf8');
    const field = styles.match(/\.time-field\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const width = Number(field.match(/width:\s*(\d+)px/)?.[1]);

    expect(width).toBeGreaterThanOrEqual(128);
    expect(styles).not.toMatch(/width:\s*106px/);

    const actions = styles.match(/\.remove-shift,\s*\.copy-action\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    expect(actions).toMatch(/height:\s*44px/);
  });

  it('stacks the shift actions below two full-width fields on mobile', () => {
    const styles = readFileSync(resolve(__dirname, 'seller-hours-page.component.scss'), 'utf8');
    const pageRule = styles.match(/\.hours-page\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const mobile = styles.match(/@media\s*\(max-width:\s*767px\)\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const group = mobile.match(/\.shift-group\s*\{([\s\S]*?)\n\s*\}/)?.[1] ?? '';

    expect(pageRule).toMatch(/grid-template-columns:\s*minmax\(0,\s*1fr\)/);
    expect(group).toMatch(/grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
    expect(group).not.toMatch(/44px\s*44px/);
    expect(mobile).toMatch(/\.time-field[^{]*\{\s*width:\s*100%/);
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
      fixture.nativeElement.querySelectorAll('input[role="combobox"]') as NodeListOf<HTMLInputElement>,
    );
    expect(inputs.length).toBeGreaterThan(0);

    for (const input of inputs) {
      expect(input.getAttribute('aria-label')).toBeTruthy();
      expect(input.id).toBeTruthy();
      expect(input.getAttribute('role')).toBe('combobox');
      expect(input.hasAttribute('aria-expanded')).toBe(true);
      expect(input.getAttribute('aria-controls')).toBeTruthy();
      const label = fixture.nativeElement.querySelector(`label[for="${input.id}"]`);
      expect(label).toBeTruthy();
    }
  });

  it('renders a single icon per time box without a native time input', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const boxes = Array.from(fixture.nativeElement.querySelectorAll('.time-box')) as HTMLElement[];
    expect(boxes.length).toBeGreaterThan(0);

    for (const box of boxes) {
      expect(box.querySelectorAll('ion-icon')).toHaveLength(1);
      expect(box.querySelector('input[type="time"]')).toBeNull();
    }
  });

  it('shows backend HH:mm:ss values as HH:MM and preserves them while opening and closing the list', async () => {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(of({
      items: [{
        dayOfWeek: 1,
        isOpen: true,
        shifts: [{ startTime: '08:00:30.2500000', endTime: '12:30:45.5000000' }],
      }],
    }));

    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const startInput = firstShiftInput(fixture, 'start');
    expect(startInput.value).toBe('08:00');

    fieldFor(startInput).querySelector<HTMLButtonElement>('.time-trigger')!.click();
    fixture.detectChanges();
    expect(fieldFor(startInput).querySelector('[role="listbox"]')).not.toBeNull();

    startInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(fieldFor(startInput).querySelector('[role="listbox"]')).toBeNull();
    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00:30.2500000');
    expect(component.schedule().segunda.shifts[0].endTime).toBe('12:30:45.5000000');
    expect(component.hasChanges()).toBe(false);
  });

  it('selects a 30-minute option and closes that field list', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const startInput = firstShiftInput(fixture, 'start');
    fieldFor(startInput).querySelector<HTMLButtonElement>('.time-trigger')!.click();
    fixture.detectChanges();

    const options = Array.from(fieldFor(startInput).querySelectorAll('[role="option"]')) as HTMLElement[];
    options[19].click();
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('09:30');
    expect(startInput.value).toBe('09:30');
    expect(fieldFor(startInput).querySelector('[role="listbox"]')).toBeNull();
    expect(component.hasChanges()).toBe(true);
  });

  it('keeps the other field raw value and restores everything on cancel', async () => {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(of({
      items: [{
        dayOfWeek: 1,
        isOpen: true,
        shifts: [{ startTime: '08:00:30.2500000', endTime: '12:30:45.5000000' }],
      }],
    }));

    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const endInput = firstShiftInput(fixture, 'end');
    endInput.value = '13:00';
    endInput.dispatchEvent(new Event('input'));
    endInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00:30.2500000');
    expect(component.schedule().segunda.shifts[0].endTime).toBe('13:00');

    component.cancelChanges();
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00:30.2500000');
    expect(component.schedule().segunda.shifts[0].endTime).toBe('12:30:45.5000000');
    expect(component.hasChanges()).toBe(false);
    expect(firstShiftInput(fixture, 'start').value).toBe('08:00');
  });

  it('sends untouched backend times intact in the save payload', async () => {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(of({
      items: [{
        dayOfWeek: 1,
        isOpen: true,
        shifts: [{ startTime: '08:00:30.2500000', endTime: '12:30:45.5000000' }],
      }],
    }));
    storeServiceMock.upsertStoreBusinessHours.mockReturnValue(of({ items: [] }));

    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const endInput = firstShiftInput(fixture, 'end');
    endInput.value = '13:00';
    endInput.dispatchEvent(new Event('input'));
    endInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).toHaveBeenCalledTimes(1);
    const [, request] = storeServiceMock.upsertStoreBusinessHours.mock.calls[0] as [
      string,
      { items: Array<{ dayOfWeek: number; shifts: Array<{ startTime: string; endTime: string }> }> },
    ];
    const monday = request.items.find((item) => item.dayOfWeek === 1);

    expect(monday?.shifts).toEqual([
      { startTime: '08:00:30.2500000', endTime: '13:00' },
    ]);
  });

  it('reports an invalid typed time, blocks saving and restores it on cancel', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const startInput = firstShiftInput(fixture, 'start');
    startInput.value = '25:00';
    startInput.dispatchEvent(new Event('input'));
    startInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    const alert = fieldFor(startInput).querySelector('[role="alert"]') as HTMLElement | null;
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('HH:MM');
    expect(startInput.getAttribute('aria-invalid')).toBe('true');
    expect(component.schedule().segunda.shifts[0].startTime).toBe('25:00');

    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
    expect(toastServiceMock.showGrouped).toHaveBeenCalled();
    expect(component.hasChanges()).toBe(true);

    component.cancelChanges();
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00');
    expect(firstShiftInput(fixture, 'start').value).toBe('08:00');
  });

  it('does not render NaN in the weekly summary while an invalid draft is displayed', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const startInput = firstShiftInput(fixture, 'start');
    startInput.value = 'ab:cd';
    startInput.dispatchEvent(new Event('input'));
    startInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(component.weeklyHours()).not.toContain('NaN');
    expect(fixture.nativeElement.textContent).not.toContain('NaN');
    expect(component.validateDay('segunda')).toBe('Informe horários válidos no formato HH:MM.');
    expect(component.hasChanges()).toBe(true);
  });

  it('warns about an unsaved draft before blur without committing the value', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const startInput = firstShiftInput(fixture, 'start');
    startInput.value = '09:30';
    startInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const pendingPill = fixture.nativeElement.querySelector('.date-pill') as HTMLElement;

    expect(component.hasChanges()).toBe(false);
    expect(component.hasPendingDrafts()).toBe(true);
    expect(component.hasUnsavedChanges()).toBe(true);
    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00');
    expect(pendingPill.textContent).toContain('Alterações pendentes');

    startInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('09:30');
    expect(component.hasChanges()).toBe(true);
    expect(component.hasPendingDrafts()).toBe(false);
    expect(component.hasUnsavedChanges()).toBe(true);
  });

  it('corrects an invalid suffix when the same visible time is selected', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const startInput = firstShiftInput(fixture, 'start');
    startInput.value = '08:00:99';
    startInput.dispatchEvent(new Event('input'));
    startInput.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00:99');

    fieldFor(startInput).querySelector<HTMLButtonElement>('.time-trigger')!.click();
    fixture.detectChanges();
    const options = Array.from(fieldFor(startInput).querySelectorAll('[role="option"]')) as HTMLElement[];
    options[16].click();
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[0].startTime).toBe('08:00');
    expect(startInput.value).toBe('08:00');
    expect(fieldFor(startInput).querySelector('[role="alert"]')).toBeNull();
  });

  it('keeps touched field state attached to the same shift after removing an earlier shift', async () => {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(of({
      items: [{
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '08:00', endTime: '12:00' },
          { startTime: '13:00', endTime: '18:00' },
        ],
      }],
    }));

    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;

    const mondayRow = fixture.nativeElement.querySelectorAll('.settings-row')[0] as HTMLElement;
    const shiftInputs = Array.from(mondayRow.querySelectorAll('.time-field input')) as HTMLInputElement[];
    const secondShiftStart = shiftInputs[2];

    secondShiftStart.value = 'ab:cd';
    secondShiftStart.dispatchEvent(new Event('input'));
    secondShiftStart.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(component.schedule().segunda.shifts[1].startTime).toBe('ab:cd');
    expect(fieldFor(secondShiftStart).querySelector('[role="alert"]')).not.toBeNull();
    const secondShiftFieldId = secondShiftStart.id;

    jest.spyOn(window, 'confirm').mockReturnValue(true);
    component.removeShift('segunda', 0);
    fixture.detectChanges();

    const remainingStart = firstShiftInput(fixture, 'start');
    expect(component.schedule().segunda.shifts).toHaveLength(1);
    expect(component.schedule().segunda.shifts[0].startTime).toBe('ab:cd');
    expect(remainingStart.value).toBe('ab:cd');
    expect(remainingStart.id).toBe(secondShiftFieldId);
    expect(fieldFor(remainingStart).querySelector('[role="alert"]')).not.toBeNull();
  });

  it('keeps opening and closing one field list independent from the other field', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const startInput = firstShiftInput(fixture, 'start');
    const endInput = firstShiftInput(fixture, 'end');

    fieldFor(startInput).querySelector<HTMLButtonElement>('.time-trigger')!.click();
    fixture.detectChanges();

    const startList = fieldFor(startInput).querySelector('[role="listbox"]') as HTMLElement;
    expect(startList).not.toBeNull();

    fieldFor(endInput).querySelector<HTMLButtonElement>('.time-trigger')!.click();
    fixture.detectChanges();

    const endList = fieldFor(endInput).querySelector('[role="listbox"]') as HTMLElement;

    expect(endList).not.toBeNull();
    expect(fieldFor(startInput).querySelector('[role="listbox"]')).toBeNull();
    expect(startList.id).not.toBe(endList.id);
    expect(startInput.getAttribute('aria-controls')).toBe(startList.id);
    expect(endInput.getAttribute('aria-controls')).toBe(endList.id);
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

  it('disables the add-shift action once the day reaches five shifts', async () => {
    storeServiceMock.getStoreBusinessHours.mockReturnValueOnce(of({
      items: [{
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '00:00', endTime: '01:00' },
          { startTime: '01:00', endTime: '02:00' },
          { startTime: '02:00', endTime: '03:00' },
          { startTime: '03:00', endTime: '04:00' },
          { startTime: '04:00', endTime: '05:00' },
        ],
      }],
    }));

    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const segundaRow = fixture.nativeElement.querySelectorAll('.settings-row')[0] as HTMLElement;
    const addButton = segundaRow.querySelector('.add-shift-link') as HTMLButtonElement;

    expect(addButton).toBeTruthy();
    expect(addButton.disabled).toBe(true);
  });
});

describe('SellerHoursPageComponent wizard separation', () => {
  it('does not inherit wizard navigation from the shared state', () => {
    const component = Object.create(SellerHoursPageComponent.prototype) as unknown as Record<string, unknown>;

    expect(component['goNext']).toBeUndefined();
    expect(component['goBack']).toBeUndefined();
  });
});
