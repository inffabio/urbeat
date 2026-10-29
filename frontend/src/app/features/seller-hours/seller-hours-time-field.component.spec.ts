import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { SellerHoursTimeFieldComponent } from './seller-hours-time-field.component';

@Component({
  standalone: true,
  imports: [SellerHoursTimeFieldComponent],
  template: `
    <app-seller-hours-time-field
      fieldId="start-segunda-0"
      label="Abertura"
      ariaLabel="Abertura do turno 1 de Segunda-feira"
      value="08:00"
    />
    <app-seller-hours-time-field
      fieldId="end-segunda-0"
      label="Fechamento"
      ariaLabel="Fechamento do turno 1 de Segunda-feira"
      value="12:00"
    />
  `,
})
class TwoFieldsHostComponent {}

describe('SellerHoursTimeFieldComponent', () => {
  let fixture: ComponentFixture<SellerHoursTimeFieldComponent>;
  let component: SellerHoursTimeFieldComponent;
  let emitted: string[];

  function create(value = '', fieldId = 'start-segunda-0'): void {
    emitted = [];
    fixture = TestBed.createComponent(SellerHoursTimeFieldComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('fieldId', fieldId);
    fixture.componentRef.setInput('label', 'Abertura');
    fixture.componentRef.setInput('ariaLabel', 'Abertura do turno 1 de Segunda-feira');
    fixture.componentRef.setInput('value', value);
    component.valueChange.subscribe((next) => emitted.push(next));
    fixture.detectChanges();
  }

  function input(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input') as HTMLInputElement;
  }

  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.time-trigger') as HTMLButtonElement;
  }

  function list(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="listbox"]') as HTMLElement | null;
  }

  function type(text: string): void {
    const element = input();
    element.value = text;
    element.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function blur(): void {
    input().dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  function key(keyName: string): void {
    input().dispatchEvent(new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  function openList(): void {
    trigger().click();
    fixture.detectChanges();
  }

  function bindParentValue(value: string): void {
    fixture.componentRef.setInput('value', value);
    fixture.detectChanges();
  }

  function options(): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[role="option"]')) as HTMLElement[];
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SellerHoursTimeFieldComponent, TwoFieldsHostComponent],
    }).compileComponents();
  });

  it('shows HH:MM for backend times with seconds without emitting a change', () => {
    create('08:00:30.2500000');

    expect(input().value).toBe('08:00');
    expect(emitted).toEqual([]);
  });

  it('keeps an invalid stored suffix visible until it is corrected', () => {
    create('08:00:99');

    expect(input().value).toBe('08:00:99');
  });

  it('renders one text field with a single icon and no duplicated native time control', () => {
    create('08:00');

    expect(input().getAttribute('type')).toBe('text');
    expect(fixture.nativeElement.querySelector('input[type="time"]')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.time-box ion-icon')).toHaveLength(1);
  });

  it('opens an accessible 30-minute listbox with unique ids and full range', () => {
    create('08:00', 'start-segunda-0');
    openList();

    expect(input().getAttribute('aria-expanded')).toBe('true');
    expect(input().getAttribute('role')).toBe('combobox');
    expect(input().getAttribute('aria-controls')).toBe('start-segunda-0-options');
    expect(list()?.id).toBe('start-segunda-0-options');

    const rendered = options();
    expect(rendered).toHaveLength(48);
    expect(rendered[0].textContent?.trim()).toBe('00:00');
    expect(rendered[47].textContent?.trim()).toBe('23:30');
    expect(rendered[0].id).toBe('start-segunda-0-option-0');
    expect(rendered[47].id).toBe('start-segunda-0-option-47');
  });

  it('supports arrow navigation, enter selection and escape dismissal', () => {
    create('08:00', 'start-segunda-0');
    input().focus();
    key('ArrowDown');

    expect(list()).not.toBeNull();
    expect(input().getAttribute('aria-activedescendant')).toBe('start-segunda-0-option-16');

    key('ArrowDown');
    key('Enter');

    expect(emitted).toEqual(['08:30']);
    expect(list()).toBeNull();

    openList();
    key('Escape');

    expect(list()).toBeNull();
    expect(emitted).toEqual(['08:30']);
  });

  it('selects an option with the pointer and closes the list', () => {
    create('08:00');
    openList();

    options()[19].click();
    fixture.detectChanges();

    expect(emitted).toEqual(['09:30']);
    expect(list()).toBeNull();
  });

  it('preserves seconds when the selected option matches the displayed HH:MM', () => {
    create('08:00:30.2500000');
    openList();

    options()[16].click();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
  });

  it('normalizes a typed time on blur', () => {
    create('08:00');
    type('9:30');
    blur();

    expect(emitted).toEqual(['09:30']);
  });

  it('does not emit when the typed value matches the displayed time', () => {
    create('08:00:30.2500000');
    type('08:00');
    blur();

    expect(emitted).toEqual([]);
  });

  it('reports a pending draft while typing without committing the value', () => {
    create('08:00');
    const drafts: Array<{ fieldId: string; hasDraft: boolean }> = [];
    component.draftChange.subscribe((state) => drafts.push(state));

    type('09:30');

    expect(drafts).toEqual([{ fieldId: 'start-segunda-0', hasDraft: true }]);
    expect(emitted).toEqual([]);

    type('09:31');
    expect(drafts).toHaveLength(1);

    blur();

    expect(drafts).toEqual([
      { fieldId: 'start-segunda-0', hasDraft: true },
      { fieldId: 'start-segunda-0', hasDraft: false },
    ]);
    expect(emitted).toEqual(['09:31']);
  });

  it('clears the pending draft state on escape', () => {
    create('08:00');
    const drafts: Array<{ fieldId: string; hasDraft: boolean }> = [];
    component.draftChange.subscribe((state) => drafts.push(state));

    type('10:15');
    key('Escape');

    expect(drafts).toEqual([
      { fieldId: 'start-segunda-0', hasDraft: true },
      { fieldId: 'start-segunda-0', hasDraft: false },
    ]);
    expect(emitted).toEqual([]);
  });

  it('corrects an invalid stored suffix when the selected option matches the displayed HH:MM', () => {
    create('08:00:99');
    openList();

    options()[16].click();

    expect(emitted).toEqual(['08:00']);
  });

  it('corrects an invalid stored suffix when the typed value matches the displayed HH:MM', () => {
    create('08:00:99');
    type('08:00');
    blur();

    expect(emitted).toEqual(['08:00']);
  });

  it('keeps invalid typed values, reports them and never silently falls back', () => {
    create('08:00');
    type('25:00');
    blur();

    expect(emitted).toEqual(['25:00']);

    bindParentValue('25:00');
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement | null;

    expect(input().getAttribute('aria-invalid')).toBe('true');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('HH:MM');
    expect(input().getAttribute('aria-describedby')).toBe(alert?.id);
    expect(input().value).toBe('25:00');
  });

  it('reverts the draft without emitting when escape closes a typed value', () => {
    create('08:00:30.2500000');
    type('10:15');
    key('Escape');

    expect(emitted).toEqual([]);
    expect(input().value).toBe('08:00');
    expect(list()).toBeNull();
  });

  it('closes the list on tab without changing the stored value', () => {
    create('08:00');
    openList();
    key('Tab');

    expect(list()).toBeNull();
    expect(emitted).toEqual([]);
  });

  it('associates the label, accessible name and error description', () => {
    create('08:00', 'start-segunda-0');

    const label = fixture.nativeElement.querySelector('label[for="start-segunda-0"]') as HTMLLabelElement | null;

    expect(label?.textContent?.trim()).toBe('Abertura');
    expect(input().getAttribute('aria-label')).toBe('Abertura do turno 1 de Segunda-feira');
    expect(input().getAttribute('aria-invalid')).toBe('false');
  });

  it('keeps each field list independent with its own ids', () => {
    const hostFixture = TestBed.createComponent(TwoFieldsHostComponent);
    hostFixture.detectChanges();

    const triggers = Array.from(hostFixture.nativeElement.querySelectorAll('.time-trigger')) as HTMLButtonElement[];
    triggers[0].click();
    hostFixture.detectChanges();

    const startList = hostFixture.nativeElement.querySelector('[role="listbox"]') as HTMLElement;
    expect(startList.id).toBe('start-segunda-0-options');
    expect(startList.querySelector('[role="option"]')?.id).toBe('start-segunda-0-option-0');

    triggers[1].click();
    hostFixture.detectChanges();

    const lists = Array.from(hostFixture.nativeElement.querySelectorAll('[role="listbox"]')) as HTMLElement[];
    expect(lists).toHaveLength(1);
    expect(lists[0].id).toBe('end-segunda-0-options');
    expect(lists[0].id).not.toBe(startList.id);
    expect(lists[0].querySelector('[role="option"]')?.id).toBe('end-segunda-0-option-0');
  });

  it('keeps 44px touch targets and a non-truncating field in its stylesheet', () => {
    const styles = readFileSync(resolve(__dirname, 'seller-hours-time-field.component.scss'), 'utf8');
    const triggerRule = styles.match(/\.time-trigger\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const optionRule = styles.match(/\.time-option\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const inputRule = styles.match(/\.time-input\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';

    expect(triggerRule).toMatch(/width:\s*44px/);
    expect(triggerRule).toMatch(/height:\s*44px/);
    expect(optionRule).toMatch(/min-height:\s*44px/);
    expect(inputRule).toMatch(/font-size:\s*16px/);
    expect(inputRule).toMatch(/font-variant-numeric:\s*tabular-nums/);
  });
});
