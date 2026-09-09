import { ComponentFixture, TestBed } from '@angular/core/testing';

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
    expect(sheet.style.getPropertyValue('--cart-sheet-height')).toBe('calc(50vh - var(--footer-height))');
    expect(sheet.classList).toContain('cart-sheet-above-footer');
    expect(backdrop.classList).toContain('cart-sheet-backdrop-above-footer');
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
