import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StoreProductsPageComponent } from './store-products-page.component';
import { ProductOptionGroupTemplate } from '../../../shared/models/product.model';
import { StoreService } from '../../../core/services/store.service';
import { SubscriptionService } from '../../../core/services/subscription.service';
import { ToastService } from '../../../core/services/toast.service';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

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
    getProductOptionGroupTemplates: jest.fn().mockReturnValue(of([])),
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

  describe('category deletion', () => {
    it('deletes an empty category through the icon and modal click flow', () => {
      const fixture = TestBed.createComponent(StoreProductsPageComponent);
      const page = fixture.componentInstance;
      page.storeId.set('store-1');
      page.categories.set([
        { id: 'cat-empty', name: 'Vazia', displayOrder: 1, isActive: true, isFeatured: false },
      ]);
      page.products.set([]);
      storeServiceMock.deleteStoreCategory.mockReturnValue(of(undefined));
      fixture.detectChanges();

      (fixture.nativeElement.querySelector('.cat-delete-btn') as HTMLButtonElement).click();
      fixture.detectChanges();
      (fixture.nativeElement.querySelector('.btn-danger') as HTMLButtonElement).click();

      expect(storeServiceMock.deleteStoreCategory).toHaveBeenCalledWith('store-1', 'cat-empty');
    });

    it('keeps the delete button clickable so blocked deletions can explain the rule', () => {
      const template = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');

      expect(template).toContain('(click)="confirmDeleteCategory()" [disabled]="isDeletingCategory()"');
    });

    it('calls the API and displays the backend rule when the category has products', () => {
      component.storeId.set('store-1');
      component.categories.set([
        { id: 'cat-wrong', name: 'Errada', displayOrder: 1, isActive: true, isFeatured: false },
      ]);
      component.products.set([{ id: 'product-1', categoryId: 'cat-wrong', name: 'Produto', price: 10 } as any]);
      storeServiceMock.deleteStoreCategory.mockReturnValue(throwError(() => ({ error: { detail: 'A categoria possui produtos associados.' } })));
      component.openDeleteCategory(component.categories()[0]);

      component.confirmDeleteCategory();

      expect(storeServiceMock.deleteStoreCategory).toHaveBeenCalledWith('store-1', 'cat-wrong');
      expect((component as any).categoryDialogError()).toBe('A categoria possui produtos associados.');
    });

    it('does not remove a category locally when the API rejects its deletion', () => {
      component.storeId.set('store-1');
      component.categories.set([
        { id: 'cat-wrong', name: 'Errada', displayOrder: 1, isActive: true, isFeatured: false },
        { id: 'cat-right', name: 'Correta', displayOrder: 2, isActive: true, isFeatured: false },
      ]);
      component.products.set([{ id: 'product-1', categoryId: 'cat-wrong', name: 'Produto', price: 10 } as any]);
      storeServiceMock.deleteStoreCategory.mockReturnValue(of(undefined));

      component.openDeleteCategory(component.categories()[0]);
      storeServiceMock.deleteStoreCategory.mockReturnValue(throwError(() => ({ error: { detail: 'A categoria possui produtos associados.' } })));
      component.openDeleteCategory(component.categories()[0]);
      component.confirmDeleteCategory();

      expect(component.categories()).toHaveLength(2);
      expect((component as any).categoryDialogError()).toBe('A categoria possui produtos associados.');
    });

    it('opens only the edit modal after a delete modal state is still active', () => {
      const category = { id: 'cat-1', name: 'Pizzas', displayOrder: 1, isActive: true, isFeatured: false };
      component.categories.set([category]);
      component.openDeleteCategory(category);
      component.openEditCategory(category);

      expect((component as any).categoryDialogMode()).toBe('edit');
    });

    it('opens only the delete modal after an edit modal state is still active', () => {
      const category = { id: 'cat-1', name: 'Pizzas', displayOrder: 1, isActive: true, isFeatured: false };
      component.categories.set([category]);
      component.openEditCategory(category);
      component.openDeleteCategory(category);

      expect((component as any).categoryDialogMode()).toBe('delete');
    });

    it('uses one native dialog with a submit-driven edit form', () => {
      const template = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');

      expect(template).toContain('<dialog');
      expect(template).toContain('(ngSubmit)="saveCategoryDialog()"');
    });

    it('provides an edit action in the product catalog that opens the product editor', () => {
      const template = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');

      expect(template).toContain('class="catalog-edit-btn"');
      expect(template).toContain('selectProduct(product)');
    });

    it('expands the editor layout when a product is selected', () => {
      const template = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, '../../../shared/styles/store-products.shared.scss'), 'utf8');

      expect(template).toContain('[class.editor-focused]="selectedId() !== null"');
      expect(styles).toContain('.products-layout.editor-focused {\n  grid-template-columns: minmax(0, 1fr) minmax(400px, 440px);');
    });

    it('makes the expanded product preview span the full catalog width', () => {
      const styles = readFileSync(resolve(__dirname, '../../../shared/styles/store-products.shared.scss'), 'utf8');

      expect(styles).toContain('.catalog-preview {\n  grid-column: 1 / -1;');
    });

    it('opens the native dialog in edit mode with the category name as draft', () => {
      const category = { id: 'cat-1', name: 'Pizzas', displayOrder: 1, isActive: true, isFeatured: false };

      component.openEditCategory(category);

      expect((component as any).categoryDialogMode()).toBe('edit');
      expect((component as any).categoryDialogDraft()).toBe('Pizzas');
    });

    it('closes the native dialog without calling an API', () => {
      const category = { id: 'cat-1', name: 'Pizzas', displayOrder: 1, isActive: true, isFeatured: false };
      component.openDeleteCategory(category);
      component.closeCategoryDialog();

      expect((component as any).categoryDialogMode()).toBe(null);
      expect(storeServiceMock.deleteStoreCategory).not.toHaveBeenCalled();
    });
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

  describe('reusable option group templates', () => {
    const template = (overrides: Partial<ProductOptionGroupTemplate> = {}): ProductOptionGroupTemplate => ({
      id: 'tpl-molho',
      name: 'Escolha um molho',
      isRequired: false,
      choiceType: 'multiple',
      minChoices: 0,
      maxChoices: 2,
      displayOrder: 1,
      items: [
        { id: 'tpl-item-1', name: 'Molho 1', price: 5, displayOrder: 1 },
        { id: 'tpl-item-2', name: 'Molho 2', price: 5.5, displayOrder: 2 },
      ],
      ...overrides,
    });

    it('starts with no saved group selected on a new product', () => {
      component.availableOptionGroups.set([template()]);
      expect(component.isOptionGroupTemplateSelected('tpl-molho')).toBe(false);
    });

    it('marks associated templates as selected when editing an existing product', () => {
      component.availableOptionGroups.set([template()]);
      component.selectProduct(sampleProduct({
        optionGroups: [
          {
            id: 'g1',
            templateId: 'tpl-molho',
            name: 'Escolha um molho',
            isRequired: false,
            choiceType: 'multiple',
            minChoices: 0,
            maxChoices: 2,
            displayOrder: 1,
            items: [{ id: 'i1', name: 'Molho 1', price: 5, displayOrder: 1 }],
          },
        ],
      }));

      expect(component.isOptionGroupTemplateSelected('tpl-molho')).toBe(true);
    });

    it('copies rules, items and prices with fresh local ids when selected', () => {
      component.toggleOptionGroupTemplate(template());
      const group = component.optionGroups()[0];
      expect(group.templateId).toBe('tpl-molho');
      expect(group.name).toBe('Escolha um molho');
      expect(group.choiceType).toBe('multiple');
      expect(group.minChoices).toBe(0);
      expect(group.maxChoices).toBe(2);
      expect(group.items.map(i => i.name)).toEqual(['Molho 1', 'Molho 2']);
      expect(group.items.map(i => i.price)).toEqual([5, 5.5]);
      expect(group.items[0].id).not.toBe('tpl-item-1');
      expect(component.isOptionGroupTemplateSelected('tpl-molho')).toBe(true);
      expect(component.formDirty()).toBe(true);
    });

    it('removes only the product snapshot when deselected and keeps the reusable group', () => {
      component.availableOptionGroups.set([template()]);
      component.toggleOptionGroupTemplate(template());
      component.toggleOptionGroupTemplate(template());
      expect(component.optionGroups()).toHaveLength(0);
      expect(component.availableOptionGroups()).toHaveLength(1);
    });

    it('does not duplicate the snapshot for the same template', () => {
      component.toggleOptionGroupTemplate(template());
      component.toggleOptionGroupTemplate(template({ id: 'tpl-borda', name: 'Borda' }));
      expect(component.optionGroups().filter(g => g.templateId === 'tpl-molho')).toHaveLength(1);
    });

    it('keeps manually created groups selected with no template id', () => {
      component.addOptionGroup();
      expect(component.optionGroups()).toHaveLength(1);
      expect(component.optionGroups()[0].templateId).toBeUndefined();
    });

    it('sends only selected groups with their template id on save', () => {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.availableOptionGroups.set([template(), template({ id: 'tpl-borda', name: 'Borda' })]);
      component.toggleOptionGroupTemplate(template());
      storeServiceMock.createProduct.mockReturnValue(of({ id: 'p1', optionGroups: [] }));

      component.saveProduct();

      const body = storeServiceMock.createProduct.mock.calls[0][1];
      expect(body.optionGroups).toHaveLength(1);
      expect(body.optionGroups[0].templateId).toBe('tpl-molho');
    });

    it('makes a newly persisted authored group available without reloading the page', () => {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.addOptionGroup();
      component.updateGroupName(component.optionGroups()[0].id!, 'Escolha um molho');
      component.addOptionItem(component.optionGroups()[0].id!);
      component.updateOptionItemName(component.optionGroups()[0].id!, component.optionGroups()[0].items[0].id!, 'Molho 1');
      storeServiceMock.createProduct.mockReturnValue(of({
        id: 'p1',
        optionGroups: [{
          templateId: 'tpl-new',
          name: 'Escolha um molho',
          isRequired: false,
          choiceType: 'multiple',
          minChoices: 0,
          maxChoices: 2,
          displayOrder: 1,
          items: [{ name: 'Molho 1', price: 5, displayOrder: 1 }],
        }],
      }));

      component.saveProduct();

      expect(component.availableOptionGroups().map(group => group.id)).toContain('tpl-new');
    });

    it('renders a checkbox before each saved group and the lower add-group button', () => {
      const html = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');
      expect(html).toContain('saved-group-item');
      expect(html).toContain('toggleOptionGroupTemplate(saved)');
      expect(html).toContain('add-group-btn');
      expect(html).toContain('+ Adicionar grupo');
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

    it('should ignore an inactive variation when setting the default', () => {
      component.addSizeVariation();
      component.addSizeVariation();
      const [first, second] = component.sizeVariations();
      component.setSizeActive(first.uid, false);

      component.setSizeDefault(first.uid);

      const list = component.sizeVariations();
      expect(list.find(v => v.uid === first.uid)?.isDefault).toBe(false);
      expect(list.find(v => v.uid === second.uid)?.isDefault).toBe(true);
    });

    it('should promote the first remaining active variation when the default is removed', () => {
      component.addSizeVariation();
      component.addSizeVariation();
      component.addSizeVariation();
      const [first, second, third] = component.sizeVariations();
      component.setSizeActive(second.uid, false);

      component.removeSizeVariation(first.uid);

      const list = component.sizeVariations();
      expect(list.find(v => v.uid === second.uid)?.isDefault).toBe(false);
      expect(list.find(v => v.uid === third.uid)?.isDefault).toBe(true);
    });

    it('should leave no default when the removed default has no active remaining variation', () => {
      component.addSizeVariation();
      component.addSizeVariation();
      const [first, second] = component.sizeVariations();
      component.setSizeActive(second.uid, false);

      component.removeSizeVariation(first.uid);

      const list = component.sizeVariations();
      expect(list).toHaveLength(1);
      expect(list[0].uid).toBe(second.uid);
      expect(list[0].isDefault).toBe(false);
    });

    it('should expose accessible labels and disable the default radio for inactive variations', () => {
      const fixture = TestBed.createComponent(StoreProductsPageComponent);
      const page = fixture.componentInstance;
      page.setSaleMode('size');
      page.addSizeVariation();
      page.addSizeVariation();
      page.setSizeActive(page.sizeVariations()[1].uid, false);
      fixture.detectChanges();

      const rows = fixture.nativeElement.querySelectorAll('.variation-row.size-row') as NodeListOf<HTMLElement>;
      const [nameInput, descriptionInput] = Array.from(rows[0].querySelectorAll('input.var-input')) as HTMLInputElement[];
      const priceInput = rows[0].querySelector('.money-input') as HTMLInputElement;
      const activeInput = rows[0].querySelector('.switch input[type="checkbox"]') as HTMLInputElement;
      const firstRadio = rows[0].querySelector('.default-radio input[type="radio"]') as HTMLInputElement;
      const secondRadio = rows[1].querySelector('.default-radio input[type="radio"]') as HTMLInputElement;

      expect(nameInput.getAttribute('aria-label')).toBe('Nome da variação 1');
      expect(descriptionInput.getAttribute('aria-label')).toBe('Descrição da variação 1');
      expect(priceInput.getAttribute('aria-label')).toBe('Preço da variação 1');
      expect(activeInput.getAttribute('aria-label')).toBe('Ativar variação 1');
      expect(firstRadio.disabled).toBe(false);
      expect(secondRadio.disabled).toBe(true);
    });

    it('promotes the first active variation when the persisted default is inactive', () => {
      component.selectProduct(sampleProduct({
        saleMode: 'size',
        variations: [
          { id: 'v1', name: 'Média', description: '30 cm', price: 39.9, isDefault: true, isActive: false, isRequired: false, displayOrder: 1 },
          { id: 'v2', name: 'Grande', description: '40 cm', price: 49.9, isDefault: false, isActive: true, isRequired: false, displayOrder: 2 },
        ],
      }));

      expect(component.sizeVariations()).toEqual([
        expect.objectContaining({ uid: 'v1', isDefault: false, isActive: false }),
        expect.objectContaining({ uid: 'v2', isDefault: true, isActive: true }),
      ]);
    });

    it('keeps only one active default when the persisted product has multiple defaults', () => {
      component.selectProduct(sampleProduct({
        saleMode: 'size',
        variations: [
          { id: 'v1', name: 'Média', description: '30 cm', price: 39.9, isDefault: true, isActive: true, isRequired: false, displayOrder: 1 },
          { id: 'v2', name: 'Grande', description: '40 cm', price: 49.9, isDefault: true, isActive: true, isRequired: false, displayOrder: 2 },
        ],
      }));

      expect(component.sizeVariations().map(v => v.isDefault)).toEqual([true, false]);
    });

    it('leaves no default when the persisted product has no active variation', () => {
      component.selectProduct(sampleProduct({
        saleMode: 'size',
        variations: [
          { id: 'v1', name: 'Média', description: '30 cm', price: 39.9, isDefault: true, isActive: false, isRequired: false, displayOrder: 1 },
        ],
      }));

      expect(component.sizeVariations().every(v => !v.isDefault)).toBe(true);
    });

    it('marks the form dirty after reordering variations', () => {
      component.addSizeVariation();
      component.addSizeVariation();
      const complete = jest.fn();
      component.formDirty.set(false);

      component.reorderSizeVariations({ detail: { from: 0, to: 1, complete } } as unknown as CustomEvent);

      expect(component.formDirty()).toBe(true);
      expect(complete).toHaveBeenCalled();
    });

    it('labels the variation reorder handle and delete button for assistive technology', () => {
      const fixture = TestBed.createComponent(StoreProductsPageComponent);
      const page = fixture.componentInstance;
      page.setSaleMode('size');
      page.addSizeVariation();
      page.sizeVariations.update(list => list.map(v => ({ ...v, name: 'Família' })));
      fixture.detectChanges();

      const row = fixture.nativeElement.querySelector('.variation-row.size-row') as HTMLElement;
      expect((row.querySelector('.drag-handle') as HTMLElement).getAttribute('aria-label')).toBe('Arrastar variação Família');
      expect((row.querySelector('.v-cell-action button') as HTMLButtonElement).getAttribute('aria-label')).toBe('Remover variação Família');
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

  describe('catalog product row layout', () => {
    const template = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');
    const styles = readFileSync(resolve(__dirname, '../../../shared/styles/store-products.shared.scss'), 'utf8');

    it('shows the price above the product name', () => {
      const priceIdx = template.indexOf('class="catalog-product-price"');
      const nameIdx = template.indexOf('class="catalog-product-name"');
      expect(priceIdx).toBeGreaterThan(-1);
      expect(nameIdx).toBeGreaterThan(-1);
      expect(priceIdx).toBeLessThan(nameIdx);
    });

    it('keeps the product image and the action buttons', () => {
      expect(template).toContain('class="catalog-product-img"');
      expect(template).toContain('class="catalog-edit-btn"');
      expect(template).toContain('class="catalog-copy-btn"');
      expect(template).toContain('class="catalog-toggle-btn"');
      expect(template).toContain('class="catalog-delete-btn"');
    });

    it('stacks the info block vertically with padding-right to clear the actions', () => {
      const infoIdx = styles.indexOf('.catalog-product-info');
      const infoBlock = styles.slice(infoIdx, styles.indexOf('.catalog-product-price', infoIdx + 1));
      expect(infoBlock).toContain('flex-direction: column;');
      expect(infoBlock).toContain('padding-right: 8px;');
    });

    it('renders the name in a small full-width font that truncates with ellipsis', () => {
      const nameIdx = styles.indexOf('.catalog-product-name');
      const nameBlock = styles.slice(nameIdx, styles.indexOf('.catalog-product-category', nameIdx));
      expect(nameBlock).toContain('font-size: 12px');
      expect(nameBlock).toContain('max-width: 100%');
      expect(nameBlock).toContain('text-overflow: ellipsis');
    });

    it('keeps the price value visually larger than the product name', () => {
      const valueIdx = styles.indexOf('.catalog-product-price-value');
      const valueBlock = styles.slice(valueIdx, styles.indexOf('.catalog-product-desc', valueIdx));
      expect(valueBlock).toContain('font-size: 15px');
    });

    it('lays the price out on a single line', () => {
      const priceIdx = styles.indexOf('.catalog-product-price');
      const priceBlock = styles.slice(priceIdx, styles.indexOf('.catalog-product-price-label', priceIdx + 1));
      expect(priceBlock).toContain('flex-direction: row;');
    });
  });

  describe('product image upload', () => {
    const fileEvent = (file: File): Event =>
      ({ target: { files: [file], value: '' } } as unknown as Event);

    const originalImage = window.Image;
    const originalFileReader = window.FileReader;

    beforeEach(() => {
      storeServiceMock.uploadImage.mockReturnValue(of({ url: 'https://img.test/uploaded.png' }));
    });

    afterEach(() => {
      window.Image = originalImage;
      window.FileReader = originalFileReader;
    });

    it('uploads SVG originals without browser compression', async () => {
      const file = new File(['<svg/>'], 'product.svg', { type: 'image/svg+xml' });

      await component.onImageSelected(fileEvent(file));

      expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'products');
    });

    it('uploads AVIF originals without browser compression', async () => {
      const file = new File([new Uint8Array([1, 2, 3])], 'product.avif', { type: 'image/avif' });

      await component.onImageSelected(fileEvent(file));

      expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'products');
    });

    it('rejects unsupported formats before uploading', async () => {
      const file = new File(['x'], 'product.gif', { type: 'image/gif' });

      await component.onImageSelected(fileEvent(file));

      expect(storeServiceMock.uploadImage).not.toHaveBeenCalled();
      expect(toastMock.showError).toHaveBeenCalledWith(expect.stringContaining('Formato'));
    });

    it('rejects files above the documented 6 MB product limit', async () => {
      const file = new File([new Uint8Array(6 * 1024 * 1024 + 1)], 'product.png', { type: 'image/png' });

      await component.onImageSelected(fileEvent(file));

      expect(storeServiceMock.uploadImage).not.toHaveBeenCalled();
      expect(toastMock.showError).toHaveBeenCalledWith(expect.stringContaining('6 MB'));
    });

    it('falls back to the original file when the browser cannot decode it', async () => {
      (window as unknown as { Image: unknown }).Image = class {
        onerror: (() => void) | null = null;
        onload: (() => void) | null = null;
        width = 0;
        height = 0;
        set src(_value: string) {
          this.onerror?.();
        }
      };
      const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'product.png', { type: 'image/png' });

      await component.onImageSelected(fileEvent(file));

      expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'products');
    });

    it('falls back to the original file when FileReader fails', async () => {
      (window as unknown as { FileReader: unknown }).FileReader = class {
        onerror: (() => void) | null = null;
        onload: (() => void) | null = null;
        result: string | null = null;
        readAsDataURL(_file: File) {
          this.onerror?.();
        }
      };
      const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'product.png', { type: 'image/png' });

      await component.onImageSelected(fileEvent(file));

      expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'products');
    });

    it('surfaces the backend message when the upload is rejected', async () => {
      storeServiceMock.uploadImage.mockReturnValue(
        throwError(() => ({ status: 400, error: { error: 'O arquivo de imagem é inválido ou está corrompido.' } })),
      );
      const file = new File(['<svg/>'], 'product.svg', { type: 'image/svg+xml' });

      await component.onImageSelected(fileEvent(file));

      expect(toastMock.showError).toHaveBeenCalledWith('O arquivo de imagem é inválido ou está corrompido.');
    });

    it('does not double-report 413 size errors because the interceptor owns the message', async () => {
      storeServiceMock.uploadImage.mockReturnValue(throwError(() => ({ status: 413 })));
      const file = new File(['<svg/>'], 'product.svg', { type: 'image/svg+xml' });

      await component.onImageSelected(fileEvent(file));

      expect(toastMock.showError).not.toHaveBeenCalled();
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
