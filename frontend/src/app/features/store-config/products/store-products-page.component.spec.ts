import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StoreProductsPageComponent } from './store-products-page.component';
import { StoreService } from '../../../core/services/store.service';
import { SubscriptionService } from '../../../core/services/subscription.service';
import { ToastService } from '../../../core/services/toast.service';
import { Router } from '@angular/router';
import { of } from 'rxjs';

describe('StoreProductsPageComponent — option groups', () => {
  let component: StoreProductsPageComponent;
  let consoleErrorSpy: jest.SpyInstance;

  beforeAll(() => {
    consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterAll(() => {
    consoleErrorSpy.mockRestore();
  });

  const storeServiceMock = {
    getMyStore: jest.fn().mockReturnValue(of(null)),
    getStoreCategories: jest.fn().mockReturnValue(of([])),
    getStoreProducts: jest.fn().mockReturnValue(of([])),
    createStoreCategory: jest.fn(),
    deleteStoreCategory: jest.fn(),
    createProduct: jest.fn(),
    updateProduct: jest.fn(),
    deleteProduct: jest.fn(),
    uploadImage: jest.fn(),
    getDeliveryNeighborhoodsByStore: jest.fn(),
    getStoreAddress: jest.fn(),
  };

  const toastMock = { showError: jest.fn(), showSuccess: jest.fn(), showWarning: jest.fn(), showInfo: jest.fn() };
  const routerMock = { navigate: jest.fn() };
  const subscriptionServiceMock = { getMySubscription: jest.fn().mockReturnValue(of({})) };

  beforeEach(async () => {
    jest.clearAllMocks();
    jest.spyOn(window, 'confirm').mockReturnValue(true);

    await TestBed.configureTestingModule({
      imports: [FormsModule, StoreProductsPageComponent],
      schemas: [NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA],
      providers: [
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastMock },
        { provide: Router, useValue: routerMock },
        { provide: SubscriptionService, useValue: subscriptionServiceMock },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StoreProductsPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('addOptionGroup', () => {
    it('should name the first group "Grupo 1"', () => {
      expect(component.optionGroups().length).toBe(0);
      component.addOptionGroup();
      expect(component.optionGroups().length).toBe(1);
      expect(component.optionGroups()[0].name).toBe('Grupo 1');
    });

    it('should name the second group "Grupo 2"', () => {
      component.addOptionGroup();
      component.addOptionGroup();
      expect(component.optionGroups()[1].name).toBe('Grupo 2');
    });

    it('should name the third group "Grupo 3"', () => {
      component.addOptionGroup();
      component.addOptionGroup();
      component.addOptionGroup();
      expect(component.optionGroups()[2].name).toBe('Grupo 3');
    });

    it('should start with empty items array', () => {
      component.addOptionGroup();
      expect(component.optionGroups()[0].items).toEqual([]);
    });

    it('should default to choiceType multiple, min 0, max 3, not required', () => {
      component.addOptionGroup();
      const g = component.optionGroups()[0];
      expect(g.choiceType).toBe('multiple');
      expect(g.minChoices).toBe(0);
      expect(g.maxChoices).toBe(3);
      expect(g.isRequired).toBe(false);
    });

    it('should auto-expand the newly created group', () => {
      component.addOptionGroup();
      const id = component.optionGroups()[0].id;
      expect(component.expandedGroupId()).toBe(id);
    });

    it('should increment group numbers even after removal and re-add', () => {
      component.addOptionGroup();
      component.addOptionGroup();
      const id2 = component.optionGroups()[1].id!;
      component.removeOptionGroup(id2);
      component.addOptionGroup();
      expect(component.optionGroups()[1].name).toBe('Grupo 3');
    });
  });

  describe('updateGroupChoiceType', () => {
    it('should clamp maxChoices to 1 when switching to single', () => {
      component.addOptionGroup();
      const g = component.optionGroups()[0];
      component.updateGroupChoiceType(g.id!, 'single');
      expect(component.optionGroups()[0].maxChoices).toBe(1);
    });

    it('should bump maxChoices to 3 when switching to multiple with max < 2', () => {
      component.addOptionGroup();
      const g = component.optionGroups()[0];
      component.updateGroupChoiceType(g.id!, 'single');
      component.updateGroupChoiceType(g.id!, 'multiple');
      expect(component.optionGroups()[0].maxChoices).toBe(3);
    });
  });

  describe('toggleGroupRequired', () => {
    it('should set minChoices to 1 when making required', () => {
      component.addOptionGroup();
      const g = component.optionGroups()[0];
      component.toggleGroupRequired(g.id!);
      expect(component.optionGroups()[0].minChoices).toBe(1);
      expect(component.optionGroups()[0].isRequired).toBe(true);
    });

    it('should set minChoices to 0 when making optional', () => {
      component.addOptionGroup();
      const g = component.optionGroups()[0];
      component.updateGroupMin(g.id!, 1);
      component.toggleGroupRequired(g.id!);
      expect(component.optionGroups()[0].minChoices).toBe(0);
      expect(component.optionGroups()[0].isRequired).toBe(false);
    });
  });

  describe('formatOptionItemPrice', () => {
    it('should format item price as BRL string', () => {
      component.addOptionGroup();
      const g = component.optionGroups()[0];
      component.addOptionItem(g.id!);
      const item = component.optionGroups()[0].items[0];
      const formatted = component.formatOptionItemPrice(g.id!, item.id!);
      expect(formatted).toBe('0,00');
    });
  });

  describe('sale mode — size variations', () => {
    it('should mark the first size variation as default', () => {
      component.addSizeVariation();
      component.addSizeVariation();
      const list = component.sizeVariations();
      expect(list[0].isDefault).toBe(true);
      expect(list[1].isDefault).toBe(false);
    });

    it('should allow only one default at a time', () => {
      component.addSizeVariation();
      component.addSizeVariation();
      const second = component.sizeVariations()[1];
      component.setSizeDefault(second.uid);
      const list = component.sizeVariations();
      expect(list[0].isDefault).toBe(false);
      expect(list[1].isDefault).toBe(true);
    });

    it('should promote first remaining variation to default when default is removed', () => {
      component.addSizeVariation();
      component.addSizeVariation();
      const first = component.sizeVariations()[0];
      component.removeSizeVariation(first.uid);
      expect(component.sizeVariations()[0].isDefault).toBe(true);
    });
  });

  describe('sale mode — fixed weight', () => {
    it('should compute equivalent price per kg for grams', () => {
      const label = component.equivalentPricePerKg({ uid: 'x', weight: '500', unit: 'g', price: '23,00', isDefault: false, isActive: true });
      expect(label).toBe('R$ 46,00/kg');
    });

    it('should compute equivalent price per kg for kg unit', () => {
      const label = component.equivalentPricePerKg({ uid: 'x', weight: '1', unit: 'kg', price: '40,00', isDefault: false, isActive: true });
      expect(label).toBe('R$ 40,00/kg');
    });

    it('should return zero label when weight or price is missing', () => {
      const label = component.equivalentPricePerKg({ uid: 'x', weight: '', unit: 'g', price: '', isDefault: false, isActive: true });
      expect(label).toBe('R$ 0,00/kg');
    });
  });

  describe('sale mode — variable weight', () => {
    it('should compute the example price for 500g inside limits', () => {
      component.updateWeightConfigField('pricePerKg', '59,90');
      component.updateWeightConfigField('minGrams', '200');
      component.updateWeightConfigField('maxGrams', '2000');
      expect(component.variableWeightSampleGrams()).toBe(500);
      expect(component.variableWeightExamplePrice()).toBe('R$ 29,95');
    });

    it('should clamp the example to minGrams when min > 500', () => {
      component.updateWeightConfigField('pricePerKg', '10,00');
      component.updateWeightConfigField('minGrams', '800');
      component.updateWeightConfigField('maxGrams', '2000');
      expect(component.variableWeightSampleGrams()).toBe(800);
    });
  });

  describe('clearForm on newProduct', () => {
    it('should reset sale mode, variations and groups (no reuse between products)', () => {
      component.setSaleMode('size');
      component.addSizeVariation();
      component.addOptionGroup();
      component.newProduct();
      expect(component.saleMode()).toBe('single');
      expect(component.sizeVariations()).toEqual([]);
      expect(component.optionGroups()).toEqual([]);
    });
  });

  describe('copyProductOptions', () => {
    it('should start a new draft with copied options and blank product identity fields', () => {
      const source = sampleProduct({
        saleMode: 'size',
        categoryId: 'cat1',
        price: 29.9,
        isFeatured: true,
        isBestSeller: true,
        tagPriority: 'destaque,mais_vendido',
        variations: [
          { id: 'v1', name: 'Media', description: '30 cm', price: 39.9, isDefault: true, isActive: true, isRequired: false, displayOrder: 1 },
        ],
        optionGroups: [
          { id: 'g1', name: 'Bordas', isRequired: false, choiceType: 'multiple', minChoices: 0, maxChoices: 2, displayOrder: 1, items: [{ id: 'i1', name: 'Catupiry', price: 5, displayOrder: 1 }] },
        ],
      });

      component.selectProduct(sampleProduct({ id: 'editing', name: 'Produto sendo editado' }));
      component.copyProductOptions(source);

      expect(component.selectedId()).toBeNull();
      expect(component.productName()).toBe('');
      expect(component.productDesc()).toBe('');
      expect(component.productImage()).toBe('');
      expect(component.productCatId()).toBe('');
      expect(component.productPrice()).toBe('0,00');
      expect(component.saleMode()).toBe('size');
      expect(component.sizeVariations()[0]).toEqual(expect.objectContaining({ name: 'Media', description: '30 cm', price: '39,90', isDefault: true, isActive: true }));
      expect(component.sizeVariations()[0].uid).not.toBe('v1');
      expect(component.optionGroups()[0].items[0].name).toBe('Catupiry');
      expect(component.optionGroups()[0].id).not.toBe('g1');
      expect(component.optionGroups()[0].items[0].id).not.toBe('i1');
      expect(component.tagDestaque()).toBe(false);
      expect(component.tagMaisVendido()).toBe(false);
      expect(component.formDirty()).toBe(true);
    });

    it('should copy options into the current draft instead of creating another product when already creating', () => {
      component.newProduct();
      component.productName.set('Rascunho atual');
      component.productDesc.set('Descricao atual');
      component.productImage.set('https://img.test/produto.jpg');
      component.productImagePreview.set('https://img.test/produto.jpg');

      component.copyProductOptions(sampleProduct({ saleMode: 'variable_weight', weightConfig: { id: 'w1', pricePerKg: 59.9, minGrams: 200, maxGrams: 2000, incrementGrams: 100, isEstimated: true } }));

      expect(component.selectedId()).toBeNull();
      expect(component.productName()).toBe('Rascunho atual');
      expect(component.productDesc()).toBe('Descricao atual');
      expect(component.productImage()).toBe('https://img.test/produto.jpg');
      expect(component.saleMode()).toBe('variable_weight');
      expect(component.weightConfig()).toEqual({ pricePerKg: '59,90', minGrams: '200', maxGrams: '2000', incrementGrams: '100', isEstimated: true });
    });
  });

  describe('maskMoney', () => {
    it('should mask digits as BRL', () => {
      expect(component.maskMoney('2990')).toBe('29,90');
    });

    it('should return 0,00 for empty', () => {
      expect(component.maskMoney('')).toBe('0,00');
    });
  });

  function sampleProduct(overrides: Partial<any> = {}): any {
    return {
      id: 'product1',
      storeId: 'store1',
      categoryId: 'cat1',
      categoryName: 'Pizzas',
      name: 'Pizza teste',
      description: 'Descricao',
      price: 20,
      imageUrl: 'https://img.test/pizza.jpg',
      isAvailable: true,
      isFeatured: false,
      isBestSeller: false,
      isNew: false,
      displayOrder: 1,
      saleMode: 'single',
      additionals: [],
      choiceOptions: [],
      variations: [],
      optionGroups: [],
      weightConfig: null,
      ...overrides,
    };
  }
});
