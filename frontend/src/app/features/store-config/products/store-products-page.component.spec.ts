import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StoreProductsPageComponent } from './store-products-page.component';
import { ProductOptionGroupTemplate } from '../../../shared/models/product.model';
import { StoreService } from '../../../core/services/store.service';
import { SubscriptionService } from '../../../core/services/subscription.service';
import { ToastService } from '../../../core/services/toast.service';
import { Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { pendingChangesGuard } from '../../../core/guards/pending-changes.guard';
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
    function fullProduct(overrides: Partial<any> = {}): any {
      return sampleProduct({
        id: 'source-product',
        name: 'Pizza Margherita',
        description: 'Molho, mussarela e manjericão',
        categoryId: 'cat-pizzas',
        categoryName: 'Pizzas',
        price: 49.9,
        imageUrl: 'https://img.test/margherita.jpg',
        isAvailable: false,
        isFeatured: true,
        isBestSeller: true,
        isNew: true,
        tagPriority: 'destaque,mais_vendido,novidade',
        saleMode: 'size',
        variations: [
          { id: 'var-source-1', name: 'Média', description: '30 cm', price: 49.9, isDefault: true, isActive: true, isRequired: false, displayOrder: 1 },
          { id: 'var-source-2', name: 'Grande', description: '40 cm', price: 59.9, isDefault: false, isActive: true, isRequired: false, displayOrder: 2 },
        ],
        optionGroups: [
          {
            id: 'group-source',
            templateId: 'tpl-bordas',
            name: 'Bordas',
            isRequired: true,
            choiceType: 'single',
            minChoices: 1,
            maxChoices: 1,
            displayOrder: 1,
            items: [{ id: 'item-source', name: 'Catupiry', price: 6, displayOrder: 1 }],
          },
        ],
        additionals: [
          { id: 'additional-source', name: 'Bacon extra', price: 7.5, isActive: true, isRequired: false, displayOrder: 1 },
        ],
        choiceOptions: [
          { id: 'choice-source', name: 'Borda recheada', price: 8, isActive: true, isRequired: false, displayOrder: 1 },
        ],
        ...overrides,
      });
    }

    it('copies every editable field of the persisted product into a new draft', () => {
      const source = fullProduct();
      component.products.set([source]);
      component.selectProduct(sampleProduct({ id: 'editing', name: 'Produto sendo editado' }));

      component.copyProductOptions(source);

      expect(component.selectedId()).toBeNull();
      expect(component.productName()).toBe('Pizza Margherita');
      expect(component.productDesc()).toBe('Molho, mussarela e manjericão');
      expect(component.productPrice()).toBe('49,90');
      expect(component.productCatId()).toBe('cat-pizzas');
      expect(component.productImage()).toBe('https://img.test/margherita.jpg');
      expect(component.productImagePreview()).toBe('https://img.test/margherita.jpg');
      expect(component.isProductActive()).toBe(false);
      expect(component.tagDestaque()).toBe(true);
      expect(component.tagMaisVendido()).toBe(true);
      expect(component.tagNovidade()).toBe(true);
      expect(component.tagPriority()).toEqual(['destaque', 'mais_vendido', 'novidade']);
      expect(component.saleMode()).toBe('size');
      expect(component.sizeVariations().map(v => v.name)).toEqual(['Média', 'Grande']);
      expect(component.sizeVariations()[0]).toEqual(expect.objectContaining({
        description: '30 cm',
        price: '49,90',
        isDefault: true,
        isActive: true,
      }));
      expect(component.optionGroups()).toHaveLength(1);
      expect(component.optionGroups()[0]).toEqual(expect.objectContaining({
        templateId: 'tpl-bordas',
        name: 'Bordas',
        isRequired: true,
        choiceType: 'single',
        minChoices: 1,
        maxChoices: 1,
      }));
      expect(component.optionGroups()[0].items[0]).toEqual(expect.objectContaining({ name: 'Catupiry', price: 6 }));
      expect(component.draftAdditionals()).toEqual([
        expect.objectContaining({ name: 'Bacon extra', price: 7.5, isActive: true, isRequired: false, displayOrder: 1 }),
      ]);
      expect(component.draftChoiceOptions()).toEqual([
        expect.objectContaining({ name: 'Borda recheada', price: 8, isActive: true, isRequired: false, displayOrder: 1 }),
      ]);
      expect(component.expandedGroupId()).toBe(component.optionGroups()[0].id);
      expect(component.formDirty()).toBe(true);
      expect(toastMock.showSuccess).toHaveBeenCalledWith('Produto completo copiado para um novo produto.');
    });

    it('regenerates the nested ids instead of reusing the persisted ones', () => {
      const source = fullProduct();
      component.copyProductOptions(source);

      expect(component.selectedId()).toBeNull();
      expect(component.optionGroups()[0].id).not.toBe('group-source');
      expect(component.optionGroups()[0].id).not.toBe(source.id);
      expect(component.optionGroups()[0].items[0].id).not.toBe('item-source');
      expect(component.sizeVariations()[0].uid).not.toBe('var-source-1');
      expect(component.sizeVariations()[1].uid).not.toBe('var-source-2');
      expect(component.draftAdditionals()[0].id).not.toBe('additional-source');
      expect(component.draftChoiceOptions()[0].id).not.toBe('choice-source');

      const generatedIds = [
        component.optionGroups()[0].id,
        component.optionGroups()[0].items[0].id,
        component.sizeVariations()[0].uid,
        component.sizeVariations()[1].uid,
        component.draftAdditionals()[0].id,
        component.draftChoiceOptions()[0].id,
      ];
      expect(new Set(generatedIds).size).toBe(generatedIds.length);
    });

    it('keeps the copy independent from the source when the draft is edited', () => {
      const source = fullProduct();
      component.products.set([source]);
      component.copyProductOptions(source);

      const groupId = component.optionGroups()[0].id!;
      const itemId = component.optionGroups()[0].items[0].id!;
      component.productName.set('Pizza copiada editada');
      component.productDesc.set('Descrição do rascunho');
      component.productCatId.set('cat-outra');
      component.productImage.set('https://img.test/outra.jpg');
      component.tagDestaque.set(false);
      component.updateGroupName(groupId, 'Bordas novas');
      component.updateOptionItemName(groupId, itemId, 'Cheddar');
      component.sizeVariations.update(list => list.map((v, index) => index === 0 ? { ...v, name: 'Broto' } : v));
      component.draftAdditionals.update(list => list.map(a => ({ ...a, name: 'Bacon do rascunho' })));
      component.draftChoiceOptions.update(list => list.map(c => ({ ...c, name: 'Borda do rascunho' })));

      expect(component.productName()).toBe('Pizza copiada editada');
      expect(component.draftAdditionals()[0].name).toBe('Bacon do rascunho');

      expect(source.name).toBe('Pizza Margherita');
      expect(source.description).toBe('Molho, mussarela e manjericão');
      expect(source.categoryId).toBe('cat-pizzas');
      expect(source.imageUrl).toBe('https://img.test/margherita.jpg');
      expect(source.isFeatured).toBe(true);
      expect(source.optionGroups[0].name).toBe('Bordas');
      expect(source.optionGroups[0].items[0].name).toBe('Catupiry');
      expect(source.variations[0].name).toBe('Média');
      expect(source.additionals[0].name).toBe('Bacon extra');
      expect(source.choiceOptions[0].name).toBe('Borda recheada');
    });

    it('does not share mutable objects with the source when the source changes later', () => {
      const source = fullProduct();
      component.copyProductOptions(source);

      source.additionals[0].name = 'Adicional alterado na origem';
      source.choiceOptions[0].name = 'Opção alterada na origem';
      source.optionGroups[0].name = 'Grupo alterado na origem';
      source.optionGroups[0].items[0].name = 'Item alterado na origem';
      source.variations[0].name = 'Variação alterada na origem';

      expect(component.draftAdditionals()[0].name).toBe('Bacon extra');
      expect(component.draftChoiceOptions()[0].name).toBe('Borda recheada');
      expect(component.optionGroups()[0].name).toBe('Bordas');
      expect(component.optionGroups()[0].items[0].name).toBe('Catupiry');
      expect(component.sizeVariations()[0].name).toBe('Média');
    });

    it('sends the copied additionals and choice options in the create payload with selectedId null', async () => {
      component.storeId.set('store-1');
      component.copyProductOptions(fullProduct());
      storeServiceMock.createProduct.mockReturnValue(of(sampleProduct({ id: 'new-product' })));

      expect(component.selectedId()).toBeNull();
      await component.saveDraft();

      expect(storeServiceMock.updateProduct).not.toHaveBeenCalled();
      expect(storeServiceMock.createProduct).toHaveBeenCalledWith('store-1', expect.objectContaining({
        name: 'Pizza Margherita',
        description: 'Molho, mussarela e manjericão',
        price: 49.9,
        categoryId: 'cat-pizzas',
        imageUrl: 'https://img.test/margherita.jpg',
        isAvailable: false,
        isFeatured: true,
        isBestSeller: true,
        isNew: true,
        tagPriority: 'destaque,mais_vendido,novidade',
        saleMode: 'size',
        additionals: [expect.objectContaining({ name: 'Bacon extra', price: 7.5, isActive: true, isRequired: false, displayOrder: 1 })],
        choiceOptions: [expect.objectContaining({ name: 'Borda recheada', price: 8, isActive: true, isRequired: false, displayOrder: 1 })],
        variations: [
          expect.objectContaining({ name: 'Média', description: '30 cm', price: 49.9, isDefault: true, isActive: true, displayOrder: 1 }),
          expect.objectContaining({ name: 'Grande', description: '40 cm', price: 59.9, isDefault: false, isActive: true, displayOrder: 2 }),
        ],
      }));

      const body = storeServiceMock.createProduct.mock.calls[0][1];
      expect(body.optionGroups[0]).toEqual(expect.objectContaining({ name: 'Bordas', choiceType: 'single', minChoices: 1, maxChoices: 1 }));
      expect(body.optionGroups[0].items[0]).toEqual(expect.objectContaining({ name: 'Catupiry', price: 6, displayOrder: 1 }));
      expect(body.additionals[0].id).not.toBe('additional-source');
      expect(body.choiceOptions[0].id).not.toBe('choice-source');
    });

    it('copies fixed weight variations and the variable weight configuration', () => {
      component.copyProductOptions(fullProduct({
        saleMode: 'fixed_weight',
        variations: [
          { id: 'var-source-1', name: '500 g', weightGrams: 500, price: 25, isDefault: true, isActive: true, isRequired: false, displayOrder: 1 },
        ],
      }));

      expect(component.fixedWeightVariations()).toEqual([
        expect.objectContaining({ weight: '500', unit: 'g', price: '25,00', isDefault: true, isActive: true }),
      ]);
      expect(component.fixedWeightVariations()[0].uid).not.toBe('var-source-1');

      component.copyProductOptions(fullProduct({
        saleMode: 'variable_weight',
        price: 0,
        weightConfig: { id: 'weight-source', pricePerKg: 59.9, minGrams: 200, maxGrams: 2000, incrementGrams: 100, isEstimated: true },
      }));

      expect(component.saleMode()).toBe('variable_weight');
      expect(component.weightConfig()).toEqual({ pricePerKg: '59,90', minGrams: '200', maxGrams: '2000', incrementGrams: '100', isEstimated: true });
    });

    it('replaces the current draft with the complete copy instead of creating another product', () => {
      component.newProduct();
      component.productName.set('Rascunho atual');
      component.productDesc.set('Descricao atual');
      component.productImage.set('https://img.test/produto.jpg');
      component.productImagePreview.set('https://img.test/produto.jpg');

      component.copyProductOptions(fullProduct({
        saleMode: 'variable_weight',
        price: 0,
        weightConfig: { id: 'w1', pricePerKg: 59.9, minGrams: 200, maxGrams: 2000, incrementGrams: 100, isEstimated: true },
      }));

      expect(component.selectedId()).toBeNull();
      expect(component.productName()).toBe('Pizza Margherita');
      expect(component.productDesc()).toBe('Molho, mussarela e manjericão');
      expect(component.productImage()).toBe('https://img.test/margherita.jpg');
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

  describe('wizard header spacing', () => {
    const styles = readFileSync(resolve(__dirname, '../../../shared/styles/store-products.shared.scss'), 'utf8');
    const wizardSelector = '.wizard-content:not(.is-dashboard) .products-layout';

    function readRule(selector: string): string {
      const selectorIdx = styles.indexOf(selector);
      expect(selectorIdx).toBeGreaterThan(-1);
      const openBrace = styles.indexOf('{', selectorIdx);
      return styles.slice(openBrace + 1, styles.indexOf('}', openBrace));
    }

    function classSpecificity(selector: string): number {
      return (selector.match(/\.[\w-]+/g) ?? []).length;
    }

    it('gives the wizard product layout a top offset below the sticky progress header', () => {
      const rule = readRule(wizardSelector);

      expect(rule).toContain('padding-top: 20px');
      expect(rule).toContain('margin-top: 0');
    });

    it('does not let the compact mobile layout rule reset the wizard top offset', () => {
      expect(styles).toContain(wizardSelector);
      expect(classSpecificity(wizardSelector)).toBeGreaterThan(classSpecificity('.products-layout'));
    });

    it('keeps the dashboard mobile spacing untouched', () => {
      const mobileBlock = styles.slice(styles.lastIndexOf('@media (max-width: 500px)'));

      expect(mobileBlock).toContain('.products-layout { padding: 0 10px; margin-top: 20px; }');
    });
  });

  describe('wizard draft persistence', () => {
    function startValidDraft(): void {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.markDirty();
    }

    it('persists a filled editor draft through the product API before reporting it as saved', async () => {
      startValidDraft();
      storeServiceMock.createProduct.mockReturnValue(of(sampleProduct({ id: 'p1', name: 'Pizza' })));

      await component.saveDraft();

      expect(storeServiceMock.createProduct).toHaveBeenCalledTimes(1);
      expect(component.saveStatus()).toBe('saved');
      expect(component.formDirty()).toBe(false);
    });

    it('never reports a save when the API rejects the draft and keeps the editor data', async () => {
      startValidDraft();
      storeServiceMock.createProduct.mockReturnValue(throwError(() => ({ status: 500 })));

      await component.saveDraft();

      expect(component.saveStatus()).toBe('error');
      expect(component.formDirty()).toBe(true);
      expect(component.productName()).toBe('Pizza');
      expect(toastMock.showError).toHaveBeenCalled();
    });

    it('reports an error and skips the API when the draft breaks the existing product validations', async () => {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.markDirty();

      await component.saveDraft();

      expect(storeServiceMock.createProduct).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('error');
      expect(component.formDirty()).toBe(true);
      expect(toastMock.showError).toHaveBeenCalledWith('Selecione uma categoria.');
    });

    it('distinguishes an empty dirty form from an editable draft and never claims a save', async () => {
      component.storeId.set('store-1');
      component.toggleTag('destaque');

      await component.saveDraft();

      expect(storeServiceMock.createProduct).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('idle');
      expect(component.formDirty()).toBe(true);
      expect(toastMock.showInfo).toHaveBeenCalled();
    });

    it('updates an existing product draft instead of creating a duplicate', async () => {
      startValidDraft();
      component.selectProduct(sampleProduct({ id: 'p-existing' }));
      storeServiceMock.updateProduct.mockReturnValue(of(sampleProduct({ id: 'p-existing' })));

      await component.saveDraft();

      expect(storeServiceMock.updateProduct).toHaveBeenCalledWith('store-1', 'p-existing', expect.any(Object));
      expect(storeServiceMock.createProduct).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('saved');
    });
  });

  describe('editor change tracking', () => {
    function fileEvent(file: File): Event {
      return ({ target: { files: [file], value: '' } } as unknown as Event);
    }

    it('marks the form dirty when the price is typed', () => {
      component.formDirty.set(false);

      component.onPriceInput('1990');

      expect(component.productPrice()).toBe('19,90');
      expect(component.formDirty()).toBe(true);
    });

    it('marks the form dirty when a product image upload succeeds', async () => {
      storeServiceMock.uploadImage.mockReturnValue(of({ url: 'https://img.test/uploaded.png' }));
      component.formDirty.set(false);

      await component.onImageSelected(fileEvent(new File(['<svg/>'], 'product.svg', { type: 'image/svg+xml' })));

      expect(component.productImage()).toBe('https://img.test/uploaded.png');
      expect(component.formDirty()).toBe(true);
    });

    it('marks the form dirty when a product tag is toggled', () => {
      component.formDirty.set(false);

      component.toggleTag('novidade');

      expect(component.formDirty()).toBe(true);
    });

    it('marks the form dirty when an option group item is removed', () => {
      component.addOptionGroup();
      const groupId = component.optionGroups()[0].id!;
      component.addOptionItem(groupId);
      component.formDirty.set(false);

      component.removeOptionItem(groupId, component.optionGroups()[0].items[0].id!);

      expect(component.optionGroups()[0].items).toHaveLength(0);
      expect(component.formDirty()).toBe(true);
    });

    it('marks the form dirty when option groups are reordered', () => {
      component.addOptionGroup();
      component.addOptionGroup();
      component.formDirty.set(false);
      const complete = jest.fn();

      component.reorderGroups({ detail: { from: 0, to: 1, complete } } as unknown as CustomEvent);

      expect(component.formDirty()).toBe(true);
      expect(complete).toHaveBeenCalled();
    });

    it('marks the form dirty when the tag priority order changes', () => {
      component.toggleTag('destaque');
      component.toggleTag('mais_vendido');
      component.formDirty.set(false);

      component.movePriorityUp('mais_vendido');

      expect(component.tagPriority()).toEqual(['mais_vendido', 'destaque']);
      expect(component.formDirty()).toBe(true);
    });

    it('marks the form dirty when moving a tag priority down', () => {
      component.toggleTag('destaque');
      component.toggleTag('mais_vendido');
      component.formDirty.set(false);

      component.movePriorityDown('destaque');

      expect(component.tagPriority()).toEqual(['mais_vendido', 'destaque']);
      expect(component.formDirty()).toBe(true);
    });

    it('marks the form dirty when reordered fixed weight variations change the payload order', () => {
      component.addFixedWeightVariation();
      component.addFixedWeightVariation();
      component.formDirty.set(false);
      const complete = jest.fn();

      component.reorderFixedWeightVariations({ detail: { from: 0, to: 1, complete } } as unknown as CustomEvent);

      expect(component.formDirty()).toBe(true);
      expect(complete).toHaveBeenCalled();
    });

    it('binds the remaining editor fields to markDirty', () => {
      const template = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');

      expect(template).toContain('(ngModelChange)="productCatId.set($event); markDirty()"');
      expect(template).toContain('(ngModelChange)="productDesc.set($event); markDirty()"');
      expect(template).toContain('(ngModelChange)="isProductActive.set($event); markDirty()"');
    });
  });

  describe('wizard navigation protection', () => {
    it('saves the pending draft before advancing to the publish step', async () => {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.markDirty();
      storeServiceMock.createProduct.mockReturnValue(of(sampleProduct({ id: 'p1', name: 'Pizza' })));

      await component.goNext();

      expect(storeServiceMock.createProduct).toHaveBeenCalledTimes(1);
      expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja/publicar']);
    });

    it('does not advance or discard the draft when the save fails', async () => {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.markDirty();
      storeServiceMock.createProduct.mockReturnValue(throwError(() => ({ status: 500 })));

      await component.goNext();

      expect(routerMock.navigate).not.toHaveBeenCalled();
      expect(component.productName()).toBe('Pizza');
      expect(component.formDirty()).toBe(true);
    });

    it('does not advance with an empty dirty form that cannot be persisted', async () => {
      component.storeId.set('store-1');
      component.toggleTag('destaque');

      await component.goNext();

      expect(storeServiceMock.createProduct).not.toHaveBeenCalled();
      expect(routerMock.navigate).not.toHaveBeenCalled();
      expect(toastMock.showError).toHaveBeenCalled();
    });
  });

  describe('wizard draft data safety', () => {
    function startValidDraft(): void {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.markDirty();
    }

    it('adopts the created id when the draft changes during the request so the next save updates instead of duplicating', async () => {
      startValidDraft();
      const save$ = new Subject<any>();
      storeServiceMock.createProduct.mockReturnValue(save$);

      const saving = component.saveDraft();

      component.productName.set('Pizza Calabresa');
      component.markDirty();

      save$.next(sampleProduct({ id: 'p-created', name: 'Pizza' }));
      save$.complete();
      await saving;

      expect(component.selectedId()).toBe('p-created');
      expect(component.productName()).toBe('Pizza Calabresa');
      expect(component.formDirty()).toBe(true);
      expect(component.saveStatus()).toBe('idle');
      expect(toastMock.showWarning).toHaveBeenCalled();

      storeServiceMock.updateProduct.mockReturnValue(of(sampleProduct({ id: 'p-created', name: 'Pizza Calabresa' })));
      await component.saveDraft();

      expect(storeServiceMock.createProduct).toHaveBeenCalledTimes(1);
      expect(storeServiceMock.updateProduct).toHaveBeenCalledWith(
        'store-1',
        'p-created',
        expect.objectContaining({ name: 'Pizza Calabresa' }),
      );
      expect(component.saveStatus()).toBe('saved');
      expect(component.formDirty()).toBe(false);
    });

    it('keeps the draft protected while the save request is in flight', async () => {
      startValidDraft();
      const save$ = new Subject<any>();
      storeServiceMock.createProduct.mockReturnValue(save$);
      const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);

      const saving = component.saveDraft();

      expect(component.hasUnsavedChanges()).toBe(true);
      const canLeave = TestBed.runInInjectionContext(() =>
        pendingChangesGuard(component, null as never, null as never, null as never),
      );

      expect(confirmSpy).toHaveBeenCalledTimes(1);
      expect(canLeave).toBe(false);
      expect(component.productName()).toBe('Pizza');

      save$.next(sampleProduct({ id: 'p1' }));
      save$.complete();
      await saving;
    });

    it('does not start a second save or navigate while a save is in flight', async () => {
      startValidDraft();
      const save$ = new Subject<any>();
      storeServiceMock.createProduct.mockReturnValue(save$);

      const saving = component.saveDraft();
      await component.goNext();

      expect(storeServiceMock.createProduct).toHaveBeenCalledTimes(1);
      expect(routerMock.navigate).not.toHaveBeenCalled();
      expect(component.productName()).toBe('Pizza');

      save$.next(sampleProduct({ id: 'p1' }));
      save$.complete();
      await saving;
    });

    it('keeps the same draft available for a retry after the API rejects the save', async () => {
      startValidDraft();
      storeServiceMock.createProduct
        .mockReturnValueOnce(throwError(() => ({ status: 500 })))
        .mockReturnValueOnce(of(sampleProduct({ id: 'p-retry', name: 'Pizza' })));

      await component.saveDraft();

      expect(component.saveStatus()).toBe('error');
      expect(component.selectedId()).toBeNull();
      expect(component.formDirty()).toBe(true);
      expect(component.productName()).toBe('Pizza');

      await component.saveDraft();

      expect(storeServiceMock.createProduct).toHaveBeenCalledTimes(2);
      expect(component.saveStatus()).toBe('saved');
      expect(component.formDirty()).toBe(false);
      expect(component.productName()).toBe('');
    });
  });

  describe('internal draft switches', () => {
    function startDirtyDraft(): void {
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.markDirty();
    }

    it('asks for confirmation before starting another product and cancels the discard', () => {
      startDirtyDraft();
      const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);

      component.newProduct();

      expect(confirmSpy).toHaveBeenCalledTimes(1);
      expect(component.productName()).toBe('Pizza');
      expect(component.selectedId()).toBeNull();
      expect(component.formDirty()).toBe(true);
    });

    it('discards the draft and clears the pending state after confirmation', () => {
      startDirtyDraft();
      jest.spyOn(window, 'confirm').mockReturnValue(true);

      component.newProduct();

      expect(component.productName()).toBe('');
      expect(component.productCatId()).toBe('');
      expect(component.formDirty()).toBe(false);
      expect(component.saveStatus()).toBe('idle');
    });

    it('asks for confirmation before loading another product and keeps the draft on cancel', () => {
      startDirtyDraft();
      const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);

      component.selectProduct(sampleProduct({ id: 'other', name: 'Outro produto' }));

      expect(confirmSpy).toHaveBeenCalledTimes(1);
      expect(component.selectedId()).toBeNull();
      expect(component.productName()).toBe('Pizza');
      expect(component.formDirty()).toBe(true);
    });

    it('loads the selected product and clears the pending state after confirmation', () => {
      startDirtyDraft();
      jest.spyOn(window, 'confirm').mockReturnValue(true);

      component.selectProduct(sampleProduct({ id: 'other', name: 'Outro produto' }));

      expect(component.selectedId()).toBe('other');
      expect(component.productName()).toBe('Outro produto');
      expect(component.formDirty()).toBe(false);
      expect(component.saveStatus()).toBe('idle');
    });

    it('does not switch drafts while a save is in flight', async () => {
      startDirtyDraft();
      const save$ = new Subject<any>();
      storeServiceMock.createProduct.mockReturnValue(save$);
      const confirmSpy = jest.spyOn(window, 'confirm');

      const saving = component.saveDraft();
      component.newProduct();
      component.selectProduct(sampleProduct({ id: 'other' }));

      expect(confirmSpy).not.toHaveBeenCalled();
      expect(component.selectedId()).toBeNull();
      expect(component.productName()).toBe('Pizza');
      expect(component.formDirty()).toBe(true);

      save$.next(sampleProduct({ id: 'p1' }));
      save$.complete();
      await saving;
    });

    it('asks for confirmation before copying the product over a dirty draft and keeps the draft on cancel', () => {
      startDirtyDraft();
      const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);

      component.copyProductOptions(sampleProduct({
        saleMode: 'size',
        variations: [{ id: 'v1', name: 'Grande', price: 39.9, isDefault: true, isActive: true }],
      }));

      expect(confirmSpy).toHaveBeenCalledTimes(1);
      expect(component.productName()).toBe('Pizza');
      expect(component.sizeVariations()).toEqual([]);
      expect(component.formDirty()).toBe(true);
    });

    it('copies the complete product after confirmation and keeps the copied draft pending', () => {
      startDirtyDraft();
      jest.spyOn(window, 'confirm').mockReturnValue(true);

      component.copyProductOptions(sampleProduct({
        name: 'Pizza Margherita',
        description: 'Molho e mussarela',
        imageUrl: 'https://img.test/margherita.jpg',
        saleMode: 'size',
        variations: [{ id: 'v1', name: 'Grande', price: 39.9, isDefault: true, isActive: true }],
        additionals: [{ id: 'a1', name: 'Bacon', price: 5, isActive: true, isRequired: false, displayOrder: 1 }],
        choiceOptions: [{ id: 'c1', name: 'Borda recheada', price: 4, isActive: true, isRequired: false, displayOrder: 1 }],
      }));

      expect(component.selectedId()).toBeNull();
      expect(component.productName()).toBe('Pizza Margherita');
      expect(component.productDesc()).toBe('Molho e mussarela');
      expect(component.productImage()).toBe('https://img.test/margherita.jpg');
      expect(component.productImagePreview()).toBe('https://img.test/margherita.jpg');
      expect(component.sizeVariations()).toHaveLength(1);
      expect(component.draftAdditionals()).toHaveLength(1);
      expect(component.draftChoiceOptions()).toHaveLength(1);
      expect(component.formDirty()).toBe(true);
    });

    it('does not overwrite the editor with a copied product while a save is in flight', async () => {
      startDirtyDraft();
      const save$ = new Subject<any>();
      storeServiceMock.createProduct.mockReturnValue(save$);
      const confirmSpy = jest.spyOn(window, 'confirm');

      const saving = component.saveDraft();
      component.copyProductOptions(sampleProduct({
        saleMode: 'size',
        variations: [{ id: 'v1', name: 'Grande', price: 39.9, isDefault: true, isActive: true }],
      }));

      expect(confirmSpy).not.toHaveBeenCalled();
      expect(toastMock.showInfo).toHaveBeenCalledWith('Aguarde o salvamento do produto em andamento.');
      expect(component.productName()).toBe('Pizza');
      expect(component.sizeVariations()).toEqual([]);

      save$.next(sampleProduct({ id: 'p1' }));
      save$.complete();
      await saving;
    });
  });

  describe('advance validation', () => {
    it('requires an image for variation products before advancing', async () => {
      component.products.set([
        sampleProduct({
          id: 'p-size',
          name: 'Pizza G',
          saleMode: 'size',
          price: 0,
          imageUrl: null,
          variations: [{ id: 'v1', name: 'Grande', price: 39.9, isDefault: true, isActive: true }],
        }),
      ]);

      await component.goNext();

      expect(routerMock.navigate).not.toHaveBeenCalled();
      expect(toastMock.showError).toHaveBeenCalledWith('O produto "Pizza G" precisa de uma imagem.');
    });

    it('keeps accepting variation products without a base price when they have an image', async () => {
      component.products.set([
        sampleProduct({
          id: 'p-size',
          name: 'Pizza G',
          saleMode: 'size',
          price: 0,
          variations: [{ id: 'v1', name: 'Grande', price: 39.9, isDefault: true, isActive: true }],
        }),
      ]);

      await component.goNext();

      expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja/publicar']);
    });
  });

  describe('product image upload', () => {
    const fileEvent = (file: File): Event =>
      ({ target: { files: [file], value: '' } } as unknown as Event);

    const originalImage = window.Image;
    const originalFileReader = window.FileReader;
    const originalCreateElement = document.createElement.bind(document);

    beforeEach(() => {
      storeServiceMock.uploadImage.mockReturnValue(of({ url: 'https://img.test/uploaded.png' }));
    });

    afterEach(() => {
      window.Image = originalImage;
      window.FileReader = originalFileReader;
      jest.restoreAllMocks();
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

    it('rejects files that are still above 1 MB after compression', async () => {
      (window as unknown as { Image: unknown }).Image = class {
        onerror: (() => void) | null = null;
        onload: (() => void) | null = null;
        width = 0;
        height = 0;
        set src(_value: string) {
          this.onerror?.();
        }
      };
      const file = new File([new Uint8Array(2 * 1024 * 1024)], 'product.png', { type: 'image/png' });

      await component.onImageSelected(fileEvent(file));

      expect(storeServiceMock.uploadImage).not.toHaveBeenCalled();
      expect(toastMock.showError).toHaveBeenCalledWith(expect.stringContaining('1 MB'));
    });

    it('uploads the compressed file instead of the oversized original', async () => {
      (window as unknown as { FileReader: unknown }).FileReader = class {
        onerror: (() => void) | null = null;
        onabort: (() => void) | null = null;
        onload: ((event: { target: { result: string } }) => void) | null = null;
        readAsDataURL() {
          this.onload?.({ target: { result: 'data:image/png;base64,AAAA' } });
        }
      };
      (window as unknown as { Image: unknown }).Image = class {
        onerror: (() => void) | null = null;
        onload: (() => void) | null = null;
        naturalWidth = 1600;
        naturalHeight = 900;
        width = 1600;
        height = 900;
        set src(_value: string) {
          this.onload?.();
        }
      };
      const toBlob = jest.fn((callback: BlobCallback, type?: string) => {
        callback(new Blob([new Uint8Array(500)], { type }));
      });
      jest.spyOn(document, 'createElement').mockImplementation((tagName: string) => {
        if (tagName === 'canvas') {
          return {
            width: 0,
            height: 0,
            getContext: jest.fn(() => ({
              drawImage: jest.fn(),
              clearRect: jest.fn(),
              fillRect: jest.fn(),
            })),
            toBlob,
          } as unknown as HTMLElement;
        }
        return originalCreateElement(tagName);
      });
      const file = new File([new Uint8Array(1024 * 1024 + 100)], 'product.png', { type: 'image/png' });

      await component.onImageSelected(fileEvent(file));

      expect(toBlob).toHaveBeenCalled();
      expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(expect.any(File), 'products');
      const uploaded = storeServiceMock.uploadImage.mock.calls[0][0] as File;
      expect(uploaded).not.toBe(file);
      expect(uploaded.size).toBe(500);
      expect(uploaded.size).toBeLessThanOrEqual(1024 * 1024);
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

  describe('wizard product catalog editing layout', () => {
    const template = readFileSync(resolve(__dirname, 'store-products-page.component.html'), 'utf8');
    const styles = readFileSync(resolve(__dirname, '../../../shared/styles/store-products.shared.scss'), 'utf8');

    function styleRule(selector: string): string {
      const selectorIdx = styles.indexOf(`${selector} {`);
      expect(selectorIdx).toBeGreaterThan(-1);
      const openBrace = styles.indexOf('{', selectorIdx);
      return styles.slice(openBrace + 1, styles.indexOf('}', openBrace));
    }

    function ruleFrom(marker: string, selector: string): string {
      const scopeIdx = styles.indexOf(marker);
      expect(scopeIdx).toBeGreaterThan(-1);
      const scoped = styles.slice(scopeIdx);
      const selectorIdx = scoped.indexOf(`${selector} {`);
      expect(selectorIdx).toBeGreaterThan(-1);
      const openBrace = scoped.indexOf('{', selectorIdx);
      return scoped.slice(openBrace + 1, scoped.indexOf('}', openBrace));
    }

    function wizardRule(selector: string): string {
      return ruleFrom('.wizard-content:not(.is-dashboard) {\n  .product-editor {', selector);
    }

    function dashboardRule(selector: string): string {
      return ruleFrom('.wizard-content.is-dashboard {', selector);
    }

    afterEach(() => {
      jest.restoreAllMocks();
      delete (HTMLElement.prototype as any).scrollIntoView;
    });

    it('exposes a stable editor anchor that clears the sticky wizard header', () => {
      expect(template).toContain('#productEditor');
      expect(template).toMatch(/<section[^>]*#productEditor[^>]*class="[^"]*product-editor/);

      const rule = wizardRule('.product-editor');
      expect(rule).toContain('scroll-margin-top');
    });

    it('scrolls to the editor after loading a product into the form', () => {
      const fixture = TestBed.createComponent(StoreProductsPageComponent);
      const page = fixture.componentInstance;
      page.products.set([sampleProduct({ id: 'p-scroll', name: 'Pizza' })]);
      fixture.detectChanges();

      const editor = fixture.nativeElement.querySelector('.product-editor') as HTMLElement;
      expect(editor).not.toBeNull();

      const scrollIntoView = jest.fn();
      HTMLElement.prototype.scrollIntoView = scrollIntoView;
      jest.spyOn(window, 'requestAnimationFrame').mockImplementation((callback: FrameRequestCallback) => {
        callback(0);
        return 0;
      });

      page.selectProduct(page.products()[0]);

      expect(page.selectedId()).toBe('p-scroll');
      expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    });

    it('uses a pencil icon with a contextual accessible label and title to edit', () => {
      const editButton = template.match(/<button class="catalog-edit-btn"[\s\S]*?<\/button>/)?.[0] ?? '';

      expect(editButton).toContain('name="pencil-outline"');
      expect(editButton).toContain('[attr.aria-label]="\'Editar produto \' + (product.name || \'sem nome\')"');
      expect(editButton).toContain('[attr.title]="\'Editar produto \' + (product.name || \'sem nome\')"');
    });

    it('renders the product name in the catalog and falls back only when it is missing', () => {
      const fixture = TestBed.createComponent(StoreProductsPageComponent);
      const page = fixture.componentInstance;
      page.products.set([
        sampleProduct({ id: 'named', name: 'Bolo de cenoura' }),
        sampleProduct({ id: 'unnamed', name: '' }),
      ]);
      fixture.detectChanges();

      const names = Array.from(
        fixture.nativeElement.querySelectorAll('.catalog-product-name') as NodeListOf<HTMLElement>,
      ).map(name => name.textContent?.trim());

      expect(names).toEqual(['Bolo de cenoura', 'Produto sem nome']);
      expect(template).toContain("{{ product.name || 'Produto sem nome' }}");
    });

    it('keeps product info and actions in independent areas and stacks actions on narrow screens', () => {
      expect(wizardRule('.catalog-product')).toContain('grid-template-columns: minmax(0, 1fr) auto');
      expect(styleRule('.catalog-product-main')).toContain('min-width: 0');
      expect(wizardRule('.catalog-product-main')).toContain('overflow: hidden');
      expect(styleRule('.catalog-product-actions')).toContain('min-width: 0');
      expect(wizardRule('.catalog-product-actions')).toContain('flex-wrap: nowrap');

      const narrowCatalog = styles.slice(styles.lastIndexOf('@media (max-width: 720px)'));
      expect(narrowCatalog).toContain('.wizard-content:not(.is-dashboard) {');
      expect(narrowCatalog).toContain('.catalog-product {');
      expect(narrowCatalog).toContain('grid-template-columns: minmax(0, 1fr);');
      expect(narrowCatalog).toContain('.catalog-product-actions {');
      expect(narrowCatalog).toContain('grid-column: 1 / -1;');
    });

    it('lets the product name wrap into two safe lines instead of hiding it', () => {
      const rule = wizardRule('.catalog-product-name');

      expect(rule).toContain('-webkit-line-clamp: 2');
      expect(rule).not.toContain('white-space: nowrap');
      expect(rule).toContain('overflow-wrap: anywhere');
      expect(styleRule('.catalog-product-name')).toContain('text-overflow: ellipsis');
    });

    it('keeps search and category filter inside the catalog card and stacks them when narrow', () => {
      expect(wizardRule('.catalog-search-input')).toContain('min-width: 0');
      expect(wizardRule('.catalog-filter-select')).toContain('min-width: 0');

      const narrowTools = styles.slice(styles.lastIndexOf('@media (max-width: 560px)'));
      expect(narrowTools).toContain('.wizard-content:not(.is-dashboard) .catalog-tools {');
      expect(narrowTools).toContain('grid-template-columns: 1fr;');
    });

    it('renders the expanded preview with compact padding and gaps', () => {
      const rule = wizardRule('.catalog-preview');

      expect(rule).toContain('padding: 10px 12px');
      expect(rule).toContain('gap: 6px');
    });

    it('scopes every catalog refinement to the wizard instead of the shared base rules', () => {
      expect(styleRule('.catalog-tools')).toContain('minmax(200px, 230px)');
      expect(styleRule('.catalog-product')).toContain('gap: 16px');
      expect(styleRule('.catalog-product')).not.toContain('gap: 12px');
      expect(styleRule('.catalog-product-name')).toContain('white-space: nowrap');
      expect(styleRule('.catalog-product-actions')).toContain('flex-wrap: wrap');
      expect(styleRule('.catalog-preview')).toContain('padding: 14px 16px');

      expect(wizardRule('.catalog-tools')).toContain('minmax(170px, 210px)');
      expect(wizardRule('.catalog-product')).toContain('gap: 12px');
      expect(wizardRule('.catalog-preview')).toContain('padding: 10px 12px');
    });

    it('keeps the dashboard catalog on its original layout explicitly', () => {
      expect(dashboardRule('.catalog-tools')).toContain('minmax(200px, 230px)');
      expect(dashboardRule('.catalog-product')).toContain('gap: 16px');
      expect(dashboardRule('.catalog-product')).toContain('padding: 12px 16px');
      expect(dashboardRule('.catalog-product-main')).toContain('gap: 16px');
      expect(dashboardRule('.catalog-product-name')).toContain('white-space: nowrap');
      expect(dashboardRule('.catalog-product-actions')).toContain('flex-wrap: wrap');
      expect(dashboardRule('.catalog-preview')).toContain('padding: 14px 16px');
      expect(dashboardRule('.catalog-preview')).toContain('gap: 10px');

      const dashboardBlock = styles.slice(styles.indexOf('.wizard-content.is-dashboard {'));
      expect(dashboardBlock).toContain('.catalog-edit-btn,');
      expect(dashboardBlock).toContain('.catalog-copy-btn {');
      expect(dashboardBlock).toContain('.catalog-toggle-btn,');
      expect(dashboardBlock).toContain('.catalog-delete-btn {');
      expect(dashboardBlock).toContain('width: 42px');
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
