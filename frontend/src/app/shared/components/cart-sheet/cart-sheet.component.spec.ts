import { ComponentFixture, TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { CartService } from '../../../core/services/cart.service';
import { CartSheetComponent } from './cart-sheet.component';

describe('CartSheetComponent', () => {
  let fixture: ComponentFixture<CartSheetComponent>;
  let cart: CartService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CartSheetComponent],
      providers: [CartService],
    }).compileComponents();

    fixture = TestBed.createComponent(CartSheetComponent);
    cart = TestBed.inject(CartService);
    cart.items.set([{ id: 'item-1', productId: 'product-1', productName: 'Hot dog', quantity: 2, unitPrice: 18 }]);
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
  });

  it('opens above the footer as a compact bottom sheet', () => {
    const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;
    const backdrop = fixture.nativeElement.querySelector('.cart-sheet-backdrop') as HTMLElement;

    expect(sheet).not.toBeNull();
    expect(sheet.textContent).toContain('Hot dog');
    expect(sheet.textContent?.replace(/\u00a0/g, ' ')).toContain('R$ 36,00');
    expect(sheet.style.getPropertyValue('--footer-height')).toBe('var(--store-footer-clearance, calc(64px + max(8px, env(safe-area-inset-bottom, 0px))))');
    expect(sheet.style.getPropertyValue('--cart-sheet-height')).toBe('min(58dvh, calc(100dvh - var(--footer-height)))');
    expect(sheet.classList).toContain('cart-sheet-above-footer');
    expect(backdrop.classList).toContain('cart-sheet-backdrop-above-footer');
  });

  it('defaults to a compact viewport-aware height taller than half the viewport', () => {
    const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;
    const height = sheet.style.getPropertyValue('--cart-sheet-height');

    expect(height).toBe('min(58dvh, calc(100dvh - var(--footer-height)))');
    expect(height).not.toContain('50vh');
  });

  it('stacks the sheet above the footer layer while keeping the backdrop below it', () => {
    const source = readFileSync(resolve(__dirname, 'cart-sheet.component.ts'), 'utf8');
    const sheetBlock = source.match(/\.cart-sheet\s*\{([\s\S]*?)\n\s*\}/);
    const backdropBlock = source.match(/\.cart-sheet-backdrop\s*\{([\s\S]*?)\n\s*\}/);

    expect(sheetBlock).not.toBeNull();
    expect(backdropBlock).not.toBeNull();

    const sheetZ = Number(sheetBlock![1].match(/z-index:\s*(\d+)/)?.[1]);
    const backdropZ = Number(backdropBlock![1].match(/z-index:\s*(\d+)/)?.[1]);

    expect(sheetZ).toBeGreaterThan(70);
    expect(backdropZ).toBeLessThan(sheetZ);
  });

  it('keeps the item list as the only scroll region and the footer stationary', () => {
    const source = readFileSync(resolve(__dirname, 'cart-sheet.component.ts'), 'utf8');
    const sheetBlock = source.match(/\.cart-sheet\s*\{([^}]*)\}/);
    const listBlock = source.match(/\.cart-sheet-list\s*\{([^}]*)\}/);
    const footerBlock = source.match(/\.cart-sheet-footer\s*\{(?=[^}]*flex-shrink)([^}]*safe-area-inset-bottom[^}]*)\}/);

    expect(sheetBlock).not.toBeNull();
    expect(listBlock).not.toBeNull();
    expect(footerBlock).not.toBeNull();

    expect(sheetBlock![1]).toMatch(/display:\s*flex/);
    expect(sheetBlock![1]).toMatch(/flex-direction:\s*column/);

    expect(listBlock![1]).toMatch(/flex:\s*1 1 auto/);
    expect(listBlock![1]).toMatch(/min-height:\s*0/);
    expect(listBlock![1]).toMatch(/overflow-y:\s*auto/);

    expect(footerBlock![1]).toMatch(/flex-shrink:\s*0/);
    expect(footerBlock![1]).toMatch(/safe-area-inset-bottom/);
  });

  it('renders the sheet outside the backdrop stacking context', () => {
    const backdrop = fixture.nativeElement.querySelector('.cart-sheet-backdrop') as HTMLElement;
    const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;

    expect(backdrop.contains(sheet)).toBe(false);
  });

  it('emits close from the top chevron and next from the primary action', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const next = jest.spyOn(fixture.componentInstance.next, 'emit');

    (fixture.nativeElement.querySelector('[aria-label="Recolher sacola"]') as HTMLButtonElement).click();
    (fixture.nativeElement.querySelector('[data-action="next-step"]') as HTMLButtonElement).click();

    expect(close).toHaveBeenCalled();
    expect(next).toHaveBeenCalled();
  });

  it('emits close when Escape is pressed', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');

    fixture.nativeElement.querySelector('.cart-sheet')
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(close).toHaveBeenCalled();
  });

  it('emits close when Escape is pressed on the document without focus on the sheet', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(close).toHaveBeenCalled();
  });

  it('ignores Escape on the document while the sheet is closed', () => {
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(close).not.toHaveBeenCalled();
  });

  it('renders a 44px-plus collapse handle that closes on click', () => {
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;

    expect(handle).not.toBeNull();
    expect(handle.getAttribute('aria-label')).toBe('Recolher sacola');
    expect(ruleBlock(readSource(), '.cart-sheet-collapse')).toMatch(/height:\s*44px/);

    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    handle.click();

    expect(close).toHaveBeenCalled();
  });

  it('keeps an accessible focus ring on the collapse control instead of removing the outline', () => {
    const source = readSource();
    const focusRule = /([^{}]*\.cart-sheet-collapse:focus-visible[^{}]*)\{([^}]*)\}/.exec(source);
    const focusBlock = focusRule?.[2] ?? '';

    expect(focusRule).not.toBeNull();
    expect(focusBlock).not.toMatch(/outline:\s*none/);
    expect(focusBlock).toMatch(/outline:\s*3px solid var\(--app-brand\)/);
    expect(focusBlock).not.toMatch(/--app-brand-shadow/);
    expect(focusBlock).toMatch(/outline-offset:\s*2px/);
  });

  it('renders a visual drag-handle stroke instead of an icon inside the collapse button', () => {
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;
    const stroke = handle.querySelector('.cart-sheet-handle') as HTMLElement | null;
    const strokeRule = ruleBlock(readSource(), '.cart-sheet-handle');

    expect(stroke).not.toBeNull();
    expect(stroke!.getAttribute('aria-hidden')).toBe('true');
    expect(handle.querySelector('ion-icon')).toBeNull();

    expect(strokeRule).toMatch(/width:\s*36px/);
    expect(strokeRule).toMatch(/height:\s*4px/);
    expect(strokeRule).toMatch(/border-radius:/);
  });

  it('closes after a downward pointer drag past the threshold on the handle', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100));
    handle.dispatchEvent(pointerEvent('pointerup', 190));

    expect(close).toHaveBeenCalled();
  });

  it('does not close after a short downward drag on the handle', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100));
    handle.dispatchEvent(pointerEvent('pointerup', 140));

    expect(close).not.toHaveBeenCalled();
  });

  it('does not close when a short drag is followed by the native trailing click', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 21));
    handle.dispatchEvent(pointerEvent('pointerup', 140, 21));
    handle.dispatchEvent(clickEvent(1));

    expect(close).not.toHaveBeenCalled();
  });

  it('suppresses the trailing click when the pointer moved even if it returns near the start', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 23));
    handle.dispatchEvent(pointerEvent('pointermove', 180, 23));
    handle.dispatchEvent(pointerEvent('pointerup', 104, 23));
    handle.dispatchEvent(clickEvent(1));

    expect(close).not.toHaveBeenCalled();
  });

  it('closes on a plain click without any pointer drag', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 22));
    handle.dispatchEvent(pointerEvent('pointerup', 100, 22));
    handle.dispatchEvent(clickEvent(1));

    expect(close).toHaveBeenCalledTimes(1);
  });

  it('captures the pointer on the handle so a release outside still closes the sheet', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    const setPointerCapture = jest.fn();
    const releasePointerCapture = jest.fn();
    handle.setPointerCapture = setPointerCapture;
    handle.releasePointerCapture = releasePointerCapture;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));

    expect(setPointerCapture).toHaveBeenCalledWith(7);

    handle.dispatchEvent(pointerEvent('pointerup', 190, 7));

    expect(releasePointerCapture).toHaveBeenCalledWith(7);
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('releases the pointer capture when the gesture is cancelled', () => {
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    const setPointerCapture = jest.fn();
    const releasePointerCapture = jest.fn();
    handle.setPointerCapture = setPointerCapture;
    handle.releasePointerCapture = releasePointerCapture;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 5));
    handle.dispatchEvent(pointerEvent('pointercancel', 120, 5));

    expect(releasePointerCapture).toHaveBeenCalledWith(5);
  });

  it('does not release a pointer the handle no longer captures', () => {
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    handle.setPointerCapture = jest.fn();
    handle.hasPointerCapture = jest.fn().mockReturnValue(false);
    const releasePointerCapture = jest.fn();
    handle.releasePointerCapture = releasePointerCapture;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    handle.dispatchEvent(pointerEvent('pointercancel', 120, 7));

    expect(releasePointerCapture).not.toHaveBeenCalled();
  });

  it('keeps the gesture logic working when releasing pointer capture throws', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    handle.setPointerCapture = jest.fn();
    handle.hasPointerCapture = jest.fn().mockReturnValue(true);
    handle.releasePointerCapture = jest.fn(() => {
      throw new DOMException('No active pointer with the given id', 'NotFoundError');
    });

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));

    expect(() => handle.dispatchEvent(pointerEvent('pointerup', 190, 7))).not.toThrow();
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('clears the active gesture when the sheet closes mid-drag and allows a new gesture after reopening', () => {
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    const setPointerCapture = jest.fn();
    const releasePointerCapture = jest.fn();
    handle.setPointerCapture = setPointerCapture;
    handle.releasePointerCapture = releasePointerCapture;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    expect(setPointerCapture).toHaveBeenCalledWith(7);

    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();

    expect(releasePointerCapture).toHaveBeenCalledWith(7);

    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();

    const reopened = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    const reopenSetCapture = jest.fn();
    reopened.setPointerCapture = reopenSetCapture;
    reopened.releasePointerCapture = jest.fn();
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');

    reopened.dispatchEvent(pointerEvent('pointerdown', 100, 9));
    expect(reopenSetCapture).toHaveBeenCalledWith(9);

    reopened.dispatchEvent(pointerEvent('pointerup', 190, 9));
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('emits close exactly once when a drag closes and the trailing click fires', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 11));
    handle.dispatchEvent(pointerEvent('pointerup', 190, 11));
    handle.dispatchEvent(clickEvent(1));

    expect(close).toHaveBeenCalledTimes(1);
  });

  it('does not let a drag swallow a keyboard activation of the handle', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 11));
    handle.dispatchEvent(pointerEvent('pointerup', 190, 11));

    expect(close).toHaveBeenCalledTimes(1);

    handle.dispatchEvent(clickEvent(0));

    expect(close).toHaveBeenCalledTimes(2);
  });

  it('ignores pointerup from a pointer that does not own the active gesture', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    handle.setPointerCapture = jest.fn();
    const releasePointerCapture = jest.fn();
    handle.releasePointerCapture = releasePointerCapture;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    handle.dispatchEvent(pointerEvent('pointerup', 190, 9));

    expect(close).not.toHaveBeenCalled();
    expect(releasePointerCapture).not.toHaveBeenCalled();

    handle.dispatchEvent(pointerEvent('pointerup', 190, 7));

    expect(releasePointerCapture).toHaveBeenCalledWith(7);
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('ignores a second pointerdown while a gesture is already active', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    const setPointerCapture = jest.fn();
    const releasePointerCapture = jest.fn();
    handle.setPointerCapture = setPointerCapture;
    handle.releasePointerCapture = releasePointerCapture;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    handle.dispatchEvent(pointerEvent('pointerdown', 100, 9));

    expect(setPointerCapture).toHaveBeenCalledTimes(1);
    expect(setPointerCapture).toHaveBeenCalledWith(7);

    handle.dispatchEvent(pointerEvent('pointerup', 190, 9));

    expect(close).not.toHaveBeenCalled();
    expect(releasePointerCapture).not.toHaveBeenCalled();

    handle.dispatchEvent(pointerEvent('pointerup', 190, 7));

    expect(releasePointerCapture).toHaveBeenCalledWith(7);
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('ignores pointercancel from a pointer that does not own the active gesture', () => {
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;
    handle.setPointerCapture = jest.fn();
    const releasePointerCapture = jest.fn();
    handle.releasePointerCapture = releasePointerCapture;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 3));
    handle.dispatchEvent(pointerEvent('pointercancel', 120, 8));

    expect(releasePointerCapture).not.toHaveBeenCalled();

    handle.dispatchEvent(pointerEvent('pointercancel', 120, 3));

    expect(releasePointerCapture).toHaveBeenCalledWith(3);
  });

  it('claims the touch gesture on the collapse handle so native scrolling cannot hijack the drag', () => {
    expect(ruleBlock(readSource(), '.cart-sheet-collapse')).toMatch(/touch-action:\s*none/);
  });

  it('does not close when the item list is dragged or scrolled down', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const list = fixture.nativeElement.querySelector('.cart-sheet-list') as HTMLElement;

    list.dispatchEvent(pointerEvent('pointerdown', 100));
    list.dispatchEvent(pointerEvent('pointerup', 220));

    expect(close).not.toHaveBeenCalled();
  });

  it('does not close when the sheet body outside the handle is dragged', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;

    sheet.dispatchEvent(pointerEvent('pointerdown', 100));
    sheet.dispatchEvent(pointerEvent('pointerup', 220));

    expect(close).not.toHaveBeenCalled();
  });

  it('clears the drag state when the gesture is cancelled', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100));
    handle.dispatchEvent(pointerEvent('pointercancel', 120));
    handle.dispatchEvent(pointerEvent('pointerup', 220));

    expect(close).not.toHaveBeenCalled();
  });

  it('closes when the backdrop outside the sheet is clicked', () => {
    const close = jest.spyOn(fixture.componentInstance.close, 'emit');
    const backdrop = fixture.nativeElement.querySelector('.cart-sheet-backdrop') as HTMLElement;

    backdrop.click();

    expect(close).toHaveBeenCalled();
  });

  it('disables the next-step button when the cart is empty and shows an add-items hint', () => {
    cart.items.set([]);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('[data-action="next-step"]') as HTMLButtonElement;

    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(fixture.nativeElement.textContent).toContain('Adicione itens');
  });

  it('does not emit next when the cart is empty even if the button is triggered', () => {
    cart.items.set([]);
    fixture.detectChanges();

    const next = jest.spyOn(fixture.componentInstance.next, 'emit');
    (fixture.nativeElement.querySelector('[data-action="next-step"]') as HTMLButtonElement).click();

    expect(next).not.toHaveBeenCalled();
  });

  it('keeps the next-step button enabled with no aria-disabled when the cart has items', () => {
    const button = fixture.nativeElement.querySelector('[data-action="next-step"]') as HTMLButtonElement;

    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-disabled')).toBeNull();
  });

  it('moves focus to the dialog section when the sheet opens', async () => {
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();

    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    await flushMicrotasks();

    const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;

    expect(document.activeElement).toBe(sheet);
  });

  it('restores focus to the previously focused element after closing when it is still connected', async () => {
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();

    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    await flushMicrotasks();

    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();
    await flushMicrotasks();

    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it('does not restore focus to a disconnected element after closing', async () => {
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();

    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();

    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    await flushMicrotasks();

    trigger.remove();

    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();
    await flushMicrotasks();

    expect(document.activeElement).not.toBe(trigger);
  });
});

function readSource(): string {
  return readFileSync(resolve(__dirname, 'cart-sheet.component.ts'), 'utf8');
}

function pointerEvent(type: string, clientY: number, pointerId?: number): MouseEvent {
  const EventCtor = (typeof PointerEvent !== 'undefined' ? PointerEvent : MouseEvent) as typeof MouseEvent;
  const event = new EventCtor(type, { bubbles: true, clientY });
  if (typeof pointerId === 'number') {
    Object.defineProperty(event, 'pointerId', { value: pointerId, configurable: true });
  }
  return event;
}

function clickEvent(detail: number): MouseEvent {
  return new MouseEvent('click', { bubbles: true, detail });
}

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function mediaBlock(source: string, query: string): string {
  const start = source.indexOf(query);
  if (start === -1) return '';
  const open = source.indexOf('{', start);
  if (open === -1) return '';
  let depth = 0;
  for (let index = open; index < source.length; index += 1) {
    if (source[index] === '{') depth += 1;
    else if (source[index] === '}') {
      depth -= 1;
      if (depth === 0) return source.slice(open + 1, index);
    }
  }
  return '';
}

function ruleBlock(source: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = source.match(new RegExp(`${escaped}\\s*\\{([\\s\\S]*?)\\}`));
  return match?.[1] ?? '';
}

describe('CartSheetComponent shell anchoring', () => {
  it('anchors the sheet and backdrop absolutely inside the app shell instead of the viewport', () => {
    const source = readSource();
    const sheet = ruleBlock(source, '.cart-sheet');
    const backdrop = ruleBlock(source, '.cart-sheet-backdrop');

    expect(sheet).not.toBe('');
    expect(backdrop).not.toBe('');
    expect(sheet).toMatch(/position:\s*absolute/);
    expect(sheet).not.toMatch(/position:\s*fixed/);
    expect(backdrop).toMatch(/position:\s*absolute/);
    expect(backdrop).not.toMatch(/position:\s*fixed/);
  });

  it('keeps bottom anchored to the measured footer height without a desktop +28px gutter', () => {
    const source = readSource();
    const sheet = ruleBlock(source, '.cart-sheet');
    const backdrop = ruleBlock(source, '.cart-sheet-backdrop');
    const desktop = mediaBlock(source, '@media (min-width: 900px)');

    expect(sheet).toMatch(/bottom:\s*var\(--footer-height\);/);
    expect(backdrop).toMatch(/bottom:\s*var\(--footer-height\);/);
    expect(source).not.toMatch(/var\(--footer-height\)\s*\+\s*28px/);
    expect(source).not.toMatch(/var\(--footer-height\)\s*-\s*28px/);
    expect(desktop).not.toMatch(/bottom:\s*calc\(/);
  });
});

describe('CartSheetComponent compact list layout', () => {
  it('caps the sheet at a compact height above the footer clearance', () => {
    const sheet = ruleBlock(readSource(), '.cart-sheet');

    expect(sheet).toMatch(/min\(58dvh/);
    expect(sheet).toMatch(/calc\(100dvh - var\(--footer-height\)\)/);
  });

  it('tightens the list rhythm so three items fit without crowding the footer', () => {
    const source = readSource();
    const list = ruleBlock(source, '.cart-sheet-list');
    const item = ruleBlock(source, '.cart-sheet-item');
    const media = ruleBlock(source, '.cart-sheet-item img,\n    .cart-sheet-item-placeholder');

    expect(list).toMatch(/gap:\s*6px/);
    expect(item).toMatch(/min-height:\s*56px/);
    expect(item).toMatch(/padding:\s*4px 0/);
    expect(media).toMatch(/width:\s*40px/);
    expect(media).toMatch(/height:\s*40px/);
  });

  it('keeps the list as the only scroll region with contained overscroll and a static footer', () => {
    const source = readSource();
    const list = ruleBlock(source, '.cart-sheet-list');
    const footer = source.match(/\.cart-sheet-footer\s*\{(?=[^}]*flex-shrink)([^}]*safe-area-inset-bottom[^}]*)\}/)?.[1] ?? '';

    expect(list).toMatch(/flex:\s*1 1 auto/);
    expect(list).toMatch(/min-height:\s*0/);
    expect(list).toMatch(/overflow-y:\s*auto/);
    expect(list).toMatch(/overscroll-behavior:\s*contain/);
    expect(footer).not.toBe('');
    expect(footer).toMatch(/flex-shrink:\s*0/);
    expect(footer).not.toMatch(/overflow(-y)?:\s*(auto|scroll)/);
  });

  it('preserves 44px-plus touch targets on the sheet controls', () => {
    const source = readSource();
    const collapse = ruleBlock(source, '.cart-sheet-collapse');
    const next = ruleBlock(source, '.cart-sheet-next');
    const nextMinHeight = Number(next.match(/min-height:\s*(\d+)px/)?.[1]);

    expect(collapse).toMatch(/height:\s*44px/);
    expect(nextMinHeight).toBeGreaterThanOrEqual(44);
  });
});
