import { TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ProductDetailPageComponent } from './product-detail-page.component';
import { CatalogService } from '../../core/services/catalog.service';
import { CartService } from '../../core/services/cart.service';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { Product } from '../../shared/models/product.model';

describe('ProductDetailPageComponent', () => {
  let component: ProductDetailPageComponent;
  let cartService: CartService;

  const mockCatalog = { getProducts: jest.fn().mockReturnValue(of([])) };
  const mockRouter = { navigate: jest.fn(), getCurrentNavigation: jest.fn().mockReturnValue({ extras: { state: {} } }) };
  const mockRoute = { snapshot: { paramMap: { get: jest.fn() } } };

  beforeEach(async () => {
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

    const fixture = TestBed.createComponent(ProductDetailPageComponent);
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
    const fixture = TestBed.createComponent(ProductDetailPageComponent);
    fixture.componentInstance.product.set(p);
    fixture.detectChanges();

    const radioInputs = fixture.debugElement.queryAll(By.css('input[type="radio"]'));

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

    const fixture = TestBed.createComponent(ProductDetailPageComponent);
    fixture.componentInstance.product.set(p);
    fixture.detectChanges();

    const checkboxInputs = fixture.debugElement.queryAll(By.css('input[type="checkbox"]'));

    expect(checkboxInputs.length).toBeGreaterThanOrEqual(2);
    expect(checkboxInputs.some(input => input.nativeElement.name === 'additional-a1')).toBe(true);
    expect(checkboxInputs.some(input => input.nativeElement.name === 'option-group-g1')).toBe(true);
  });

  it('should render a hero placeholder instead of a broken image when product image is missing', () => {
    const p = productWithMode('single');
    p.imageUrl = undefined;

    const fixture = TestBed.createComponent(ProductDetailPageComponent);
    fixture.componentInstance.product.set(p);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.hero-placeholder'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.product-hero img'))).toBeNull();
  });
});
