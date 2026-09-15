import { ComponentFixture, TestBed } from '@angular/core/testing';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { FooterNavComponent, FooterNavItem } from './footer-nav.component';

describe('FooterNavComponent', () => {
  let fixture: ComponentFixture<FooterNavComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FooterNavComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(FooterNavComponent);
    fixture.componentRef.setInput('items', [
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho', badge: 2 } as FooterNavItem,
    ]);
    fixture.detectChanges();
  });

  it('shows the current cart quantity as a badge', () => {
    expect(fixture.nativeElement.querySelector('.footer-nav-badge')?.textContent.trim()).toBe('2');
    expect(fixture.nativeElement.querySelector('ion-icon')?.classList).toContain('cart-has-items');
    expect(fixture.nativeElement.querySelector('.footer-nav-badge')?.classList).toContain('footer-nav-badge-over-icon');
  });

  it('keeps a two-digit quantity inside a wide badge', () => {
    fixture.componentRef.setInput('items', [
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho', badge: 10 } as FooterNavItem,
    ]);
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.footer-nav-badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('10');
    expect(badge.classList).toContain('footer-nav-badge-wide');
  });

  it('keeps the cart before unavailable orders and account items', () => {
    fixture.componentRef.setInput('items', [
      { id: 'menu', icon: 'storefront-outline', label: 'Cardapio', active: true },
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho', badge: 1 },
      { id: 'orders', icon: 'receipt-outline', label: 'Pedidos', disabled: true },
      { id: 'account', icon: 'person-outline', label: 'Conta', disabled: true },
    ] as FooterNavItem[]);
    fixture.detectChanges();

    const items = Array.from(fixture.nativeElement.querySelectorAll('.footer-nav button')) as HTMLElement[];
    expect(items.map((item) => item.textContent?.trim())).toEqual(['Cardapio', '1Carrinho', 'Pedidos', 'Conta']);
    expect(items[2].classList).toContain('disabled');
    expect(items[3].classList).toContain('disabled');
  });

  it('renders actionable items as native buttons so Enter and Space activate them', () => {
    fixture.componentRef.setInput('items', [
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho', badge: 1 } as FooterNavItem,
    ]);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.footer-nav button') as HTMLButtonElement;
    expect(button.tagName).toBe('BUTTON');
    expect(button.getAttribute('type')).toBe('button');
    expect(button.getAttribute('role')).toBeNull();
  });

  it('emits select when an item button is clicked', () => {
    const select = jest.spyOn(fixture.componentInstance.select, 'emit');
    fixture.componentRef.setInput('items', [
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho' } as FooterNavItem,
    ]);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.footer-nav button') as HTMLButtonElement;
    button.click();

    expect(select).toHaveBeenCalledWith('cart');
  });

  it('does not expose navigation while the sheet is open', () => {
    const select = jest.spyOn(fixture.componentInstance.select, 'emit');
    fixture.componentRef.setInput('inert', true);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.footer-nav button') as HTMLElement;
    expect(button.getAttribute('tabindex')).toBe('-1');
    expect(button.getAttribute('aria-hidden')).toBe('true');
    button.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    button.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

    expect(select).not.toHaveBeenCalled();
  });

  it('exposes the account menu control relationship and expanded state', () => {
    fixture.componentRef.setInput('items', [
      { id: 'conta', icon: 'person-circle-outline', label: 'Conta', ariaExpanded: true, ariaControls: 'account-menu' } as FooterNavItem,
    ]);
    fixture.detectChanges();

    const link = fixture.nativeElement.querySelector('.footer-nav button') as HTMLElement;
    expect(link.getAttribute('aria-expanded')).toBe('true');
    expect(link.getAttribute('aria-controls')).toBe('account-menu');
    expect(link.getAttribute('aria-haspopup')).toBe('menu');
    expect(link.getAttribute('data-footer-id')).toBe('conta');
  });

  it('omits menu aria attributes when the item is not a menu trigger', () => {
    fixture.componentRef.setInput('items', [
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho' } as FooterNavItem,
    ]);
    fixture.detectChanges();

    const link = fixture.nativeElement.querySelector('.footer-nav button') as HTMLElement;
    expect(link.hasAttribute('aria-expanded')).toBe(false);
    expect(link.hasAttribute('aria-controls')).toBe(false);
    expect(link.hasAttribute('aria-haspopup')).toBe(false);
  });

  it('renders an enabled Pedidos item in red with its active order count', () => {
    fixture.componentRef.setInput('items', [
      { id: 'pedidos', icon: 'receipt-outline', label: 'Pedidos', badge: 2, badgeLabel: 'pedidos' } as FooterNavItem,
    ]);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.footer-nav button') as HTMLElement;
    const icon = button.querySelector('ion-icon');
    const badge = button.querySelector('.footer-nav-badge') as HTMLElement;

    expect(button.classList).not.toContain('disabled');
    expect(icon?.classList).toContain('cart-has-items');
    expect(badge.textContent?.trim()).toBe('2');
    expect(badge.getAttribute('aria-label')).toBe('2 pedidos');
  });

  it('defaults the badge label to itens for cart items', () => {
    fixture.componentRef.setInput('items', [
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho', badge: 2 } as FooterNavItem,
    ]);
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.footer-nav-badge') as HTMLElement;
    expect(badge.getAttribute('aria-label')).toBe('2 itens');
  });

  it('keeps the footer in the shell flex flow instead of overlaying the viewport and respects the bottom safe area', () => {
    const source = readFileSync(resolve(__dirname, 'footer-nav.component.ts'), 'utf8');

    expect(source).not.toMatch(/\.footer-nav-safe-zone\s*\{[^}]*position:\s*fixed/);
    expect(source).toMatch(/\.footer-nav-safe-zone\s*\{[^}]*position:\s*relative/);
    expect(source).toContain('padding-bottom: max(8px, env(safe-area-inset-bottom, 0px))');
  });

  it('constrains the footer width to the mobile shell so it never overflows the viewport', () => {
    const source = readFileSync(resolve(__dirname, 'footer-nav.component.ts'), 'utf8');

    expect(source).toContain('width: min(430px, 100%)');
    expect(source).toContain('max-width: 100%');
  });

  it('renders the safe-area wrapper around the footer bar', () => {
    expect(fixture.nativeElement.querySelector('.footer-nav-safe-zone')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.footer-nav-safe-zone > footer.footer-nav')).not.toBeNull();
  });
});

class FakeResizeObserver {
  static instances: FakeResizeObserver[] = [];
  static reset(): void {
    FakeResizeObserver.instances = [];
  }
  readonly observed: Element[] = [];
  readonly disconnect = jest.fn();
  constructor(readonly callback: ResizeObserverCallback) {
    FakeResizeObserver.instances.push(this);
  }
  observe(target: Element): void {
    this.observed.push(target);
  }
  unobserve(): void {}
}

describe('FooterNavComponent height reporting', () => {
  let fixture: ComponentFixture<FooterNavComponent>;

  beforeEach(async () => {
    (globalThis as { ResizeObserver?: unknown }).ResizeObserver = FakeResizeObserver;
    await TestBed.configureTestingModule({
      imports: [FooterNavComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(FooterNavComponent);
    fixture.componentRef.setInput('items', [
      { id: 'cart', icon: 'bag-outline', label: 'Carrinho' } as FooterNavItem,
    ]);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    delete (globalThis as { ResizeObserver?: unknown }).ResizeObserver;
    FakeResizeObserver.reset();
  });

  function lastObserver(): FakeResizeObserver {
    return FakeResizeObserver.instances[FakeResizeObserver.instances.length - 1];
  }

  it('observes the fixed safe-zone wrapper so it can report the rendered footer height', () => {
    const safeZone = fixture.nativeElement.querySelector('.footer-nav-safe-zone') as HTMLElement;

    expect(safeZone).not.toBeNull();
    expect(lastObserver().observed).toContain(safeZone);
  });

  it('reports a measured safe-zone height through heightChange', () => {
    const emit = jest.spyOn(fixture.componentInstance.heightChange, 'emit');

    lastObserver().callback(
      [{ contentRect: { height: 104 } } as unknown as ResizeObserverEntry],
      lastObserver() as unknown as ResizeObserver,
    );

    expect(emit).toHaveBeenCalledWith(104);
  });

  it('prefers the border-box size so safe-area padding counts toward the clearance', () => {
    const emit = jest.spyOn(fixture.componentInstance.heightChange, 'emit');

    lastObserver().callback(
      [
        {
          contentRect: { height: 96 },
          borderBoxSize: [{ blockSize: 132, inlineSize: 430 }],
        } as unknown as ResizeObserverEntry,
      ],
      lastObserver() as unknown as ResizeObserver,
    );

    expect(emit).not.toHaveBeenCalledWith(96);
    expect(emit).toHaveBeenCalledWith(132);
  });

  it('reads a single-object borderBoxSize shape as reported by some WebViews', () => {
    const emit = jest.spyOn(fixture.componentInstance.heightChange, 'emit');

    lastObserver().callback(
      [
        {
          contentRect: { height: 96 },
          borderBoxSize: { blockSize: 132, inlineSize: 430 },
        } as unknown as ResizeObserverEntry,
      ],
      lastObserver() as unknown as ResizeObserver,
    );

    expect(emit).not.toHaveBeenCalledWith(96);
    expect(emit).toHaveBeenCalledWith(132);
  });

  it('measures the rendered box/padding height when borderBoxSize is unavailable (legacy WebView fallback)', () => {
    const emit = jest.spyOn(fixture.componentInstance.heightChange, 'emit');
    const safeZone = fixture.nativeElement.querySelector('.footer-nav-safe-zone') as HTMLElement;
    jest.spyOn(safeZone, 'getBoundingClientRect').mockReturnValue({ height: 132 } as DOMRect);

    lastObserver().callback(
      [{ target: safeZone, contentRect: { height: 96 } } as unknown as ResizeObserverEntry],
      lastObserver() as unknown as ResizeObserver,
    );

    expect(emit).not.toHaveBeenCalledWith(96);
    expect(emit).toHaveBeenCalledWith(132);
  });

  it('measures the rendered box/padding height when borderBoxSize is present but empty', () => {
    const emit = jest.spyOn(fixture.componentInstance.heightChange, 'emit');
    const safeZone = fixture.nativeElement.querySelector('.footer-nav-safe-zone') as HTMLElement;
    jest.spyOn(safeZone, 'getBoundingClientRect').mockReturnValue({ height: 132 } as DOMRect);

    lastObserver().callback(
      [{ target: safeZone, contentRect: { height: 104 }, borderBoxSize: [] } as unknown as ResizeObserverEntry],
      lastObserver() as unknown as ResizeObserver,
    );

    expect(emit).not.toHaveBeenCalledWith(104);
    expect(emit).toHaveBeenCalledWith(132);
  });

  it('emits only finite non-negative heights', () => {
    const emit = jest.spyOn(fixture.componentInstance.heightChange, 'emit');

    lastObserver().callback(
      [{ contentRect: { height: Number.NaN } } as unknown as ResizeObserverEntry],
      lastObserver() as unknown as ResizeObserver,
    );
    lastObserver().callback(
      [{ contentRect: { height: -4 } } as unknown as ResizeObserverEntry],
      lastObserver() as unknown as ResizeObserver,
    );
    lastObserver().callback(
      [{ contentRect: { height: 0 } } as unknown as ResizeObserverEntry],
      lastObserver() as unknown as ResizeObserver,
    );

    expect(emit).not.toHaveBeenCalledWith(Number.NaN);
    expect(emit).not.toHaveBeenCalledWith(-4);
    expect(emit).toHaveBeenCalledWith(0);
  });

  it('disconnects the observer when the component is destroyed', () => {
    const observer = lastObserver();

    fixture.destroy();

    expect(observer.disconnect).toHaveBeenCalled();
  });
});
