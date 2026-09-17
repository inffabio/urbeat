import { ComponentFixture, TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ProductDetailPageComponent } from './product-detail-page.component';
import { CatalogService } from '../../core/services/catalog.service';
import { CartService } from '../../core/services/cart.service';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { Product } from '../../shared/models/product.model';

describe('ProductDetailPageComponent', () => {
  let component: ProductDetailPageComponent;
  let fixture: ComponentFixture<ProductDetailPageComponent>;
  let cartService: CartService;

  const mockCatalog = { getProducts: jest.fn().mockReturnValue(of([])) };
  const mockRouter = { navigate: jest.fn(), url: '', getCurrentNavigation: jest.fn().mockReturnValue({ extras: { state: {} } }) };
  const mockRoute: { snapshot: { paramMap: { get: jest.Mock } }; parent?: { snapshot: { paramMap: { get: jest.Mock } } } } = {
    snapshot: { paramMap: { get: jest.fn() } },
  };

  beforeEach(async () => {
    mockRouter.navigate.mockClear();
    mockRouter.url = '';
    mockRoute.snapshot.paramMap.get.mockReset();
    mockRoute.parent = undefined;
    await TestBed.configureTestingModule({
      imports: [ProductDetailPageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        CartService,
        { provide: CatalogService, useValue: mockCatalog },
        { provide: Router, useValue: mockRouter },
        { provide: ActivatedRoute, useValue: mockRoute },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductDetailPageComponent);
    component = fixture.componentInstance;
    cartService = TestBed.inject(CartService);
  });

  function productWithMode(saleMode: string): Product {
    return {
      id: 'p1',
      storeId: 's1',
      categoryId: 'c1',
      categoryName: 'Categoria',
      name: 'Teste',
      description: 'Desc',
      price: 10,
      imageUrl: 'img.jpg',
      isAvailable: true,
      isFeatured: false,
      displayOrder: 0,
      additionals: [],
      choiceOptions: [],
      variations: [],
      optionGroups: [],
      saleMode: saleMode as any,
    };
  }

  function renderProduct(p: Product): void {
    component.product.set(p);
    fixture.detectChanges();
  }

  it('should pre-select default variation for size products', () => {
    const p = productWithMode('size');
    p.variations = [
      { id: 'v1', name: 'P', price: 20, isActive: true, isDefault: false, isRequired: true, displayOrder: 1 },
      { id: 'v2', name: 'M', price: 30, isActive: true, isDefault: true, isRequired: true, displayOrder: 2 },
    ];
    component.product.set(p);
    (component as any).initDefaults();
    expect(component.selectedVariation()?.id).toBe('v2');
  });

  it('should init weight grams to config minGrams for variable weight', () => {
    const p = productWithMode('variable_weight');
    p.weightConfig = { pricePerKg: 59.90, minGrams: 200, maxGrams: 2000, incrementGrams: 100, isEstimated: false };
    component.product.set(p);
    (component as any).initDefaults();
    expect(component.selectedWeightGrams()).toBe(200);
  });

  it('should compute final price from variation price for size mode', () => {
    const p = productWithMode('size');
    p.variations = [{ id: 'v1', name: 'G', price: 55, isActive: true, isDefault: true, isRequired: true, displayOrder: 1 }];
    component.product.set(p);
    component.selectVariation(p.variations[0]);
    expect(component.finalUnitPrice()).toBe(55);
  });

  it('should compute final price as pricePerKg * grams / 1000 for variable weight', () => {
    const p = productWithMode('variable_weight');
    p.weightConfig = { pricePerKg: 50.00, minGrams: 300, maxGrams: 2000, incrementGrams: 100, isEstimated: false };
    component.product.set(p);
    component.selectedWeightGrams.set(750);
    expect(component.finalUnitPrice()).toBe(37.50);
  });

  it('should validate selection requiring a variation for size mode', () => {
    const p = productWithMode('size');
    p.variations = [{ id: 'v1', name: 'G', price: 55, isActive: true, isDefault: false, isRequired: true, displayOrder: 1 }];
    component.product.set(p);
    expect(component.isSelectionValid()).toBe(false);
    component.selectVariation(p.variations[0]);
    expect(component.isSelectionValid()).toBe(true);
  });

  it('should include weightGrams in cart when product is variable weight', () => {
    const p = productWithMode('variable_weight');
    p.weightConfig = { pricePerKg: 40.00, minGrams: 400, maxGrams: 2000, incrementGrams: 200, isEstimated: false };
    component.product.set(p);
    component.selectedWeightGrams.set(600);
    const addSpy = jest.spyOn(cartService, 'addItem');
    component.addToCart();
    expect(addSpy).toHaveBeenCalledWith(expect.objectContaining({ weightGrams: 600 }));
  });

  it('adds the product to the cart only once when add is tapped twice', () => {
    const p = productWithMode('single');
    component.product.set(p);
    const addSpy = jest.spyOn(cartService, 'addItem');

    component.addToCart();
    component.addToCart();

    expect(addSpy).toHaveBeenCalledTimes(1);
  });

  it('disables the add button while the add is in flight', () => {
    renderProduct(productWithMode('single'));
    const button = fixture.nativeElement.querySelector('.cart-btn') as HTMLButtonElement;
    expect(button.disabled).toBe(false);

    (component as any).addingToCart.set(true);
    fixture.detectChanges();

    expect(button.disabled).toBe(true);
  });

  it('clears the pending add navigation when the component is destroyed', () => {
    jest.useFakeTimers();
    try {
      const p = productWithMode('single');
      component.product.set(p);
      component.addToCart();

      (component as any).ngOnDestroy();
      jest.advanceTimersByTime(1000);

      expect(mockRouter.navigate).not.toHaveBeenCalled();
    } finally {
      jest.useRealTimers();
    }
  });

  it('should not show a price label for zero-priced variations', () => {
    const p = productWithMode('size');
    component.product.set(p);

    expect(component.variationPriceLabel({
      id: 'v1',
      name: 'Padrão',
      price: 0,
      isActive: true,
      isDefault: false,
      isRequired: true,
      displayOrder: 1,
    })).toBe('');
  });

  it('should render variation and legacy choice options as native radio inputs', () => {
    const p = productWithMode('size');
    p.variations = [{ id: 'v1', name: 'G', price: 55, isActive: true, isDefault: false, isRequired: true, displayOrder: 1 }];
    p.choiceOptions = [{ id: 'c1', name: 'Borda recheada', price: 4, isActive: true, displayOrder: 1 }];

    component.product.set(p);
    const localFixture = TestBed.createComponent(ProductDetailPageComponent);
    localFixture.componentInstance.product.set(p);
    localFixture.detectChanges();

    const radioInputs = localFixture.debugElement.queryAll(By.css('input[type="radio"]'));

    expect(radioInputs.length).toBeGreaterThanOrEqual(2);
    expect(radioInputs.some(input => input.nativeElement.name === 'variation')).toBe(true);
    expect(radioInputs.some(input => input.nativeElement.name === 'choice-option')).toBe(true);
  });

  it('should render additionals and multiple option group items as native checkboxes', () => {
    const p = productWithMode('single');
    p.additionals = [{ id: 'a1', name: 'Bacon', price: 5, isActive: true, displayOrder: 1 }];
    p.optionGroups = [{
      id: 'g1',
      name: 'Molhos',
      minChoices: 0,
      maxChoices: 2,
      choiceType: 'multiple',
      displayOrder: 1,
      items: [{ id: 'i1', name: 'Barbecue', price: 2, isActive: true, displayOrder: 1 }],
    }];

    const localFixture = TestBed.createComponent(ProductDetailPageComponent);
    localFixture.componentInstance.product.set(p);
    localFixture.detectChanges();

    const checkboxInputs = localFixture.debugElement.queryAll(By.css('input[type="checkbox"]'));

    expect(checkboxInputs.length).toBeGreaterThanOrEqual(2);
    expect(checkboxInputs.some(input => input.nativeElement.name === 'additional-a1')).toBe(true);
    expect(checkboxInputs.some(input => input.nativeElement.name === 'option-group-g1')).toBe(true);
  });

  it('should render an identity placeholder instead of a broken image when product image is missing', () => {
    const p = productWithMode('single');
    p.imageUrl = undefined;

    renderProduct(p);

    expect(fixture.debugElement.query(By.css('.product-identity-placeholder'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.product-identity img'))).toBeNull();
  });

  it('swaps the product image for the placeholder when the image fails to load', () => {
    renderProduct(productWithMode('single'));

    const image = fixture.debugElement.query(By.css('.product-identity img'));
    expect(image).not.toBeNull();

    image.triggerEventHandler('error', {});
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.product-identity img'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.product-identity-placeholder'))).not.toBeNull();
  });

  it('resets the image failure flag on initDefaults', () => {
    (component as any).imageFailed.set(true);
    const p = productWithMode('single');
    component.product.set(p);

    (component as any).initDefaults();

    expect((component as any).imageFailed()).toBe(false);
  });

  it('renders the bottom sheet without the separate back-to-menu link', () => {
    const template = readTemplate();
    const tsSource = readComponentSource();

    expect(template).not.toContain('app-back-to-menu-link');
    expect(tsSource).not.toContain('BackToMenuLinkComponent');
    expect(template).toContain('class="product-handle"');
    expect(template).toContain('class="product-detail-body"');
    expect(template).toContain('class="product-fixed"');
    expect(template.indexOf('class="product-handle"')).toBeLessThan(template.indexOf('class="product-detail-body"'));
    expect(template.indexOf('class="product-detail-body"')).toBeLessThan(template.indexOf('class="product-fixed"'));
  });

  it('keeps the options body as the only scroll region above a solid action footer', () => {
    const styles = readStyles();

    const sheet = ruleBlock(styles, '.product-sheet');
    const body = ruleBlock(styles, '.product-detail-body');
    const footer = ruleBlock(styles, '.product-fixed');

    expect(sheet).toMatch(/display:\s*flex/);
    expect(sheet).toMatch(/flex-direction:\s*column/);

    expect(body).toMatch(/flex:\s*1 1 auto/);
    expect(body).toMatch(/min-height:\s*0/);
    expect(body).toMatch(/overflow-y:\s*auto/);
    expect(body).toMatch(/overscroll-behavior:\s*contain/);

    expect(footer).toMatch(/flex-shrink:\s*0/);
    expect(footer).toMatch(/background:\s*var\(--app-surface/);
    expect(footer).not.toMatch(/linear-gradient/);
    expect(footer).not.toMatch(/safe-area-inset-bottom/);
    expect(footer).toMatch(/padding:\s*12px 18px/);
  });

  it('renders a 44px-plus visual handle that dismisses the sheet on click', () => {
    const styles = readStyles();
    const handleRule = ruleBlock(styles, '.product-handle');
    const strokeRule = ruleBlock(styles, '.product-handle-stroke');

    expect(handleRule).toMatch(/height:\s*44px/);
    expect(handleRule).toMatch(/touch-action:\s*none/);
    expect(strokeRule).toMatch(/width:\s*36px/);
    expect(strokeRule).toMatch(/height:\s*4px/);
    expect(strokeRule).toMatch(/border-radius:/);

    mockRouter.url = '/loja/produto/p1';
    renderProduct(productWithMode('single'));

    const handle = fixture.nativeElement.querySelector('[data-action="collapse-product"]') as HTMLButtonElement;

    expect(handle).not.toBeNull();
    expect(handle.getAttribute('aria-label')).toBe('Voltar ao cardápio');
    expect(handle.querySelector('.product-handle-stroke')).not.toBeNull();
    expect(handle.querySelector('ion-icon')).toBeNull();

    handle.click();

    expect(mockRouter.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('dismisses when dragged down past the 80px threshold on the handle', () => {
    mockRouter.url = '/loja/produto/p1';
    renderProduct(productWithMode('single'));
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-product"]') as HTMLElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    handle.dispatchEvent(pointerEvent('pointerup', 190, 7));

    expect(mockRouter.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('does not dismiss on a short downward drag and suppresses the trailing click', () => {
    mockRouter.url = '/loja/produto/p1';
    renderProduct(productWithMode('single'));
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-product"]') as HTMLButtonElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    handle.dispatchEvent(pointerEvent('pointerup', 140, 7));
    handle.dispatchEvent(clickEvent(1));

    expect(mockRouter.navigate).not.toHaveBeenCalled();
  });

  it('dismisses on a plain click without any pointer drag', () => {
    mockRouter.url = '/loja/produto/p1';
    renderProduct(productWithMode('single'));
    const handle = fixture.nativeElement.querySelector('[data-action="collapse-product"]') as HTMLButtonElement;

    handle.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    handle.dispatchEvent(pointerEvent('pointerup', 100, 7));
    handle.dispatchEvent(clickEvent(1));

    expect(mockRouter.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('does not dismiss when the options body is dragged down', () => {
    mockRouter.url = '/loja/produto/p1';
    renderProduct(productWithMode('single'));
    const body = fixture.nativeElement.querySelector('.product-detail-body') as HTMLElement;

    body.dispatchEvent(pointerEvent('pointerdown', 100, 7));
    body.dispatchEvent(pointerEvent('pointerup', 220, 7));

    expect(mockRouter.navigate).not.toHaveBeenCalled();
  });

  it('resolves the back destination without browser history heuristics', () => {
    const source = readComponentSource();

    expect(source).not.toContain('navigationId');
    expect(source).not.toContain('Location');
    expect(source).not.toContain('location.back');
  });

  it('navigates deterministically to the current storefront on back', () => {
    mockRouter.url = '/loja/produto/p1';
    renderProduct(productWithMode('single'));

    component.onBack();

    expect(mockRouter.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('falls back to the parent route storePath when the URL has no store segment', () => {
    mockRouter.url = '/';
    mockRoute.parent = { snapshot: { paramMap: { get: jest.fn().mockReturnValue('loja') } } };
    renderProduct(productWithMode('single'));

    component.onBack();

    expect(mockRouter.navigate).toHaveBeenCalledWith(['/', 'loja']);
  });

  it('navigates to the store after adding to the cart', () => {
    jest.useFakeTimers();
    try {
      mockRouter.url = '/loja/produto/p1';
      component.product.set(productWithMode('single'));

      component.addToCart();
      jest.advanceTimersByTime(1000);

      expect(mockRouter.navigate).toHaveBeenCalledWith(['/', 'loja']);
    } finally {
      jest.useRealTimers();
    }
  });

  it('positions the add toast above the global footer and the local action footer', () => {
    const styles = readStyles();
    const toast = ruleBlock(styles, '.add-toast');

    expect(toast).toMatch(/bottom:\s*calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 88px\)/);
    expect(toast).not.toMatch(/120px/);
  });

  it('keeps long labels from overflowing the product panel', () => {
    const styles = readStyles();

    expect(ruleBlock(styles, '.product-identity-copy h1')).toMatch(/overflow-wrap:\s*anywhere/);
    expect(ruleBlock(styles, '.check-name')).toMatch(/min-width:\s*0/);
    expect(ruleBlock(styles, '.check-name')).toMatch(/overflow-wrap:\s*anywhere/);
    expect(ruleBlock(styles, '.check-price')).toMatch(/flex-shrink:\s*0/);
    expect(ruleBlock(styles, '.section-title')).toMatch(/h2\s*\{[^}]*overflow-wrap:\s*anywhere/);
    expect(ruleBlock(styles, '.product-fixed')).toMatch(/grid-template-columns:\s*minmax\(0,\s*112px\)\s+minmax\(0,\s*1fr\)/);
  });

  it('falls back to two option columns on very narrow viewports', () => {
    const styles = readStyles();
    const narrow = mediaRuleBlock(styles, '(max-width: 360px)', '.og-buttons');

    expect(narrow).toMatch(/grid-template-columns:\s*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
  });
});

function readStyles(): string {
  return readFileSync(resolve(__dirname, 'product-detail-page.component.scss'), 'utf8');
}

function readComponentSource(): string {
  return readFileSync(resolve(__dirname, 'product-detail-page.component.ts'), 'utf8');
}

function readTemplate(): string {
  return readFileSync(resolve(__dirname, 'product-detail-page.component.html'), 'utf8');
}

function ruleBlock(source: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = source.match(new RegExp(`${escaped}\\s*\\{([\\s\\S]*?)\\}`));
  return match?.[1] ?? '';
}

function mediaRuleBlock(source: string, query: string, selector: string): string {
  const start = source.indexOf(`@media ${query}`);
  if (start < 0) return '';
  return ruleBlock(source.slice(start), selector);
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
