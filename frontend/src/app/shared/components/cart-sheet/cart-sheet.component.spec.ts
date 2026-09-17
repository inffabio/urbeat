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

  it('opens above the footer and reaches the middle of the viewport', () => {
    const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;
    const backdrop = fixture.nativeElement.querySelector('.cart-sheet-backdrop') as HTMLElement;

    expect(sheet).not.toBeNull();
    expect(sheet.textContent).toContain('Hot dog');
    expect(sheet.textContent?.replace(/\u00a0/g, ' ')).toContain('R$ 36,00');
    expect(sheet.style.getPropertyValue('--footer-height')).toBe('var(--store-footer-clearance, calc(64px + max(8px, env(safe-area-inset-bottom, 0px))))');
    expect(sheet.style.getPropertyValue('--cart-sheet-height')).toBe('min(82dvh, calc(100dvh - var(--footer-height)))');
    expect(sheet.classList).toContain('cart-sheet-above-footer');
    expect(backdrop.classList).toContain('cart-sheet-backdrop-above-footer');
  });

  it('defaults to a viewport-aware height taller than half the viewport', () => {
    const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;
    const height = sheet.style.getPropertyValue('--cart-sheet-height');

    expect(height).toBe('min(82dvh, calc(100dvh - var(--footer-height)))');
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
});
