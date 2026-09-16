import { CUSTOM_ELEMENTS_SCHEMA, NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { SubscriptionService } from '../../core/services/subscription.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerProductsPageComponent } from './seller-products-page.component';

describe('SellerProductsPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn().mockReturnValue(of({ id: 'store-1' })),
    getStoreCategories: jest.fn().mockReturnValue(of([
      { id: 'cat-1', name: 'Hambúrgueres', displayOrder: 1, isActive: true, description: '', isFeatured: false, storeId: 'store-1' },
    ])),
    getProductOptionGroupTemplates: jest.fn().mockReturnValue(of([])),
    getStoreProducts: jest.fn().mockReturnValue(of([
      {
        id: 'product-1',
        storeId: 'store-1',
        categoryId: 'cat-1',
        categoryName: 'Hambúrgueres',
        name: 'Brasa Burger',
        description: 'Pão brioche e burger artesanal.',
        price: 28.9,
        imageUrl: '',
        isAvailable: true,
        isFeatured: false,
        displayOrder: 1,
        additionals: [],
        choiceOptions: [],
        variations: [],
        optionGroups: [],
      },
    ])),
    createStoreCategory: jest.fn(),
    deleteStoreCategory: jest.fn(),
    createProduct: jest.fn(),
    updateProduct: jest.fn(),
    deleteProduct: jest.fn(),
    uploadImage: jest.fn(),
    getDeliveryNeighborhoodsByStore: jest.fn(),
    getStoreAddress: jest.fn(),
  };

  beforeEach(async () => {
    jest.clearAllMocks();
    storeServiceMock.getProductOptionGroupTemplates.mockReturnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [SellerProductsPageComponent],
      schemas: [NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: SubscriptionService, useValue: { getMySubscription: jest.fn().mockReturnValue(of({})) } },
        { provide: ToastService, useValue: { showError: jest.fn(), showSuccess: jest.fn(), showWarning: jest.fn(), showInfo: jest.fn() } },
      ],
    }).compileComponents();
  });

  it('renders the documented products overview structure', () => {
    const fixture = TestBed.createComponent(SellerProductsPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Cardápio');
    expect(fixture.nativeElement.textContent).toContain('Total de produtos');
    expect(fixture.nativeElement.textContent).toContain('Produtos cadastrados');
    expect(fixture.nativeElement.textContent).toContain('Novo produto');
    expect(fixture.nativeElement.textContent).toContain('Mostrando');
    expect(fixture.nativeElement.textContent).toContain('Brasa Burger');
    expect(fixture.nativeElement.querySelector('.metrics-overview')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.product-list-card')).not.toBeNull();
  });

  it('does not render the static Hoje/Semana/Mes period segmented control', () => {
    const fixture = TestBed.createComponent(SellerProductsPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.segmented')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.segmented button').length).toBe(0);
  });

  it('restores the saved product when cancelling the editor', () => {
    const fixture = TestBed.createComponent(SellerProductsPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    component.selectProduct(component.products()[0]);
    component.editorOpen.set(true);
    component.productName.set('Nome alterado');

    component.closeEditor();

    expect(component.editorOpen()).toBe(false);
    expect(component.productName()).toBe('Brasa Burger');
  });

  it('renders only transparent cancel and orange save actions in the editor', () => {
    const fixture = TestBed.createComponent(SellerProductsPageComponent);
    const component = fixture.componentInstance;

    fixture.detectChanges();
    component.selectProduct(component.products()[0]);
    component.editorOpen.set(true);
    fixture.detectChanges();

    const actions = fixture.nativeElement.querySelector('.product-editor-actions');
    expect(actions.querySelectorAll('button')).toHaveLength(2);
    expect(actions.textContent).toContain('Cancelar');
    expect(actions.textContent).toContain('Salvar produto');
    expect(fixture.nativeElement.querySelector('.dashboard-save-bar')).toBeNull();
    expect(fixture.nativeElement.querySelector('.save-row')).toBeNull();
  });

  it('closes the editor after a successful save', () => {
    const fixture = TestBed.createComponent(SellerProductsPageComponent);
    const component = fixture.componentInstance;

    component.editorOpen.set(true);
    (component as unknown as { onProductSaved: () => void }).onProductSaved();

    expect(component.editorOpen()).toBe(false);
  });

  describe('reusable option groups in the dashboard editor', () => {
    const template = {
      id: 'tpl-molho',
      name: 'Escolha um molho',
      isRequired: false,
      choiceType: 'multiple' as const,
      minChoices: 0,
      maxChoices: 2,
      displayOrder: 1,
      items: [
        { id: 'tpl-item-1', name: 'Molho 1', price: 5, displayOrder: 1 },
        { id: 'tpl-item-2', name: 'Molho 2', price: 5.5, displayOrder: 2 },
      ],
    };

    it('renders saved reusable groups with unchecked checkboxes', () => {
      storeServiceMock.getProductOptionGroupTemplates.mockReturnValue(of([template]));
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      const rows = fixture.nativeElement.querySelectorAll('.saved-group-item');
      expect(rows).toHaveLength(1);
      expect(rows[0].textContent).toContain('Escolha um molho');

      const checkbox = rows[0].querySelector('input[type="checkbox"]') as HTMLInputElement;
      expect(checkbox).not.toBeNull();
      expect(checkbox.checked).toBe(false);
      expect(component.isOptionGroupTemplateSelected('tpl-molho')).toBe(false);
    });

    it('selects a saved group through the dashboard checkbox and copies its data', () => {
      storeServiceMock.getProductOptionGroupTemplates.mockReturnValue(of([template]));
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      const checkbox = fixture.nativeElement.querySelector('.saved-group-item input[type="checkbox"]') as HTMLInputElement;
      checkbox.click();
      fixture.detectChanges();

      expect(component.isOptionGroupTemplateSelected('tpl-molho')).toBe(true);
      expect(component.optionGroups()).toHaveLength(1);
      expect(component.optionGroups()[0].templateId).toBe('tpl-molho');
      expect(component.optionGroups()[0].items.map((item) => item.name)).toEqual(['Molho 1', 'Molho 2']);
    });

    it('renders the lower add-group button and creates a new selected group', () => {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      const lowerButton = fixture.nativeElement.querySelector('.add-group-btn') as HTMLButtonElement;
      expect(lowerButton).not.toBeNull();
      expect(lowerButton.textContent).toContain('+ Adicionar grupo');

      lowerButton.click();
      fixture.detectChanges();

      expect(component.optionGroups()).toHaveLength(1);
      expect(component.optionGroups()[0].templateId).toBeUndefined();
    });

    it('keeps the editor open and registers no template when a save fails', () => {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      const toast = TestBed.inject(ToastService);
      component.editorOpen.set(true);
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.addOptionGroup();
      storeServiceMock.createProduct.mockReturnValue(throwError(() => ({ error: { detail: 'Falha ao salvar' } })));

      component.saveProduct();

      expect(component.isSaving()).toBe(false);
      expect(component.editorOpen()).toBe(true);
      expect(component.optionGroups()).toHaveLength(1);
      expect(component.availableOptionGroups()).toHaveLength(0);
      expect(toast.showError).toHaveBeenCalled();
    });
  });

  describe('free-form product variations in the dashboard editor', () => {
    function renderSizeEditor() {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      component.newProduct();
      component.setSaleMode('size');
      component.editorOpen.set(true);
      fixture.detectChanges();
      return { fixture, component };
    }

    it('renders the free-form variation editor with name, description, price, default, active, reorder, remove and add controls', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      fixture.detectChanges();

      const card = fixture.nativeElement.querySelector('.variation-card') as HTMLElement;
      expect(card).not.toBeNull();

      const section = card.closest('.block-section') as HTMLElement;
      expect(section.textContent).toContain('Variações');
      expect(section.textContent).toContain('Adicionar variação');

      const row = card.querySelector('.variation-row.size-row') as HTMLElement;
      expect(row).not.toBeNull();
      expect(row.querySelector('.drag-handle')).not.toBeNull();
      expect(row.querySelectorAll('input.var-input')).toHaveLength(2);
      expect(row.querySelector('.money-wrap .money-input')).not.toBeNull();
      expect(row.querySelector('.default-radio input[type="radio"]')).not.toBeNull();
      expect(row.querySelector('.switch input[type="checkbox"]')).not.toBeNull();
      expect(row.querySelector('.v-cell-action button')).not.toBeNull();
      expect(card.querySelector('.add-row-btn.block')).not.toBeNull();
    });

    function existingSizeProduct(overrides: Record<string, unknown> = {}) {
      return {
        id: 'product-1',
        storeId: 'store-1',
        categoryId: 'cat-1',
        categoryName: 'Hambúrgueres',
        name: 'Pizza',
        description: 'Pizza artesanal',
        price: 0,
        imageUrl: 'https://img.test/pizza.png',
        isAvailable: true,
        isFeatured: false,
        displayOrder: 1,
        saleMode: 'size' as const,
        additionals: [],
        choiceOptions: [],
        variations: [
          { id: 'v1', name: 'Média', description: '30 cm', price: 39.9, isDefault: true, isActive: true, isRequired: false, displayOrder: 1 },
          { id: 'v2', name: 'Grande', description: '40 cm', price: 49.9, isDefault: false, isActive: true, isRequired: false, displayOrder: 2 },
        ],
        optionGroups: [],
        ...overrides,
      };
    }

    it('loads existing product variations for editing with formatted prices and default flag', () => {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.selectProduct(existingSizeProduct());
      component.editorOpen.set(true);
      fixture.detectChanges();

      expect(component.sizeVariations()).toEqual([
        expect.objectContaining({ name: 'Média', description: '30 cm', price: '39,90', isDefault: true, isActive: true }),
        expect.objectContaining({ name: 'Grande', description: '40 cm', price: '49,90', isDefault: false, isActive: true }),
      ]);

      const rows = fixture.nativeElement.querySelectorAll('.variation-row.size-row') as NodeListOf<HTMLElement>;
      expect(rows).toHaveLength(2);

      const radios = fixture.nativeElement.querySelectorAll('.default-radio input[type="radio"]') as NodeListOf<HTMLInputElement>;
      expect(radios).toHaveLength(2);
      expect(radios[0].checked).toBe(true);
      expect(radios[1].checked).toBe(false);
    });

    it('promotes the first active variation when the persisted default is inactive', () => {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.selectProduct(existingSizeProduct({
        variations: [
          { id: 'v1', name: 'Média', description: '30 cm', price: 39.9, isDefault: true, isActive: false, isRequired: false, displayOrder: 1 },
          { id: 'v2', name: 'Grande', description: '40 cm', price: 49.9, isDefault: false, isActive: true, isRequired: false, displayOrder: 2 },
        ],
      }));

      expect(component.sizeVariations()).toEqual([
        expect.objectContaining({ name: 'Média', isDefault: false, isActive: false }),
        expect.objectContaining({ name: 'Grande', isDefault: true, isActive: true }),
      ]);
    });

    it('keeps only the first active default when the persisted product has multiple defaults', () => {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.selectProduct(existingSizeProduct({
        variations: [
          { id: 'v1', name: 'Média', description: '30 cm', price: 39.9, isDefault: true, isActive: true, isRequired: false, displayOrder: 1 },
          { id: 'v2', name: 'Grande', description: '40 cm', price: 49.9, isDefault: true, isActive: true, isRequired: false, displayOrder: 2 },
          { id: 'v3', name: 'Família', description: '50 cm', price: 59.9, isDefault: false, isActive: true, isRequired: false, displayOrder: 3 },
        ],
      }));

      expect(component.sizeVariations().map((v) => v.isDefault)).toEqual([true, false, false]);
    });

    it('leaves every variation non-default when the persisted product has no active variation', () => {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.selectProduct(existingSizeProduct({
        variations: [
          { id: 'v1', name: 'Média', description: '30 cm', price: 39.9, isDefault: true, isActive: false, isRequired: false, displayOrder: 1 },
          { id: 'v2', name: 'Grande', description: '40 cm', price: 49.9, isDefault: false, isActive: false, isRequired: false, displayOrder: 2 },
        ],
      }));

      expect(component.sizeVariations().some((v) => v.isDefault)).toBe(false);
    });

    it('saves edited existing variations through updateProduct', () => {
      const fixture = TestBed.createComponent(SellerProductsPageComponent);
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.selectProduct(existingSizeProduct());
      component.editorOpen.set(true);
      component.storeId.set('store-1');
      fixture.detectChanges();

      const rows = fixture.nativeElement.querySelectorAll('.variation-row.size-row') as NodeListOf<HTMLElement>;
      const nameInput = rows[1].querySelector('input.var-input') as HTMLInputElement;
      nameInput.value = 'Família';
      nameInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      storeServiceMock.updateProduct.mockReturnValue(of({ id: 'product-1', optionGroups: [] }));

      component.saveProduct();

      expect(storeServiceMock.updateProduct).toHaveBeenCalledWith('store-1', 'product-1', expect.objectContaining({
        saleMode: 'size',
        variations: [
          expect.objectContaining({ name: 'Média', price: 39.9, isDefault: true, isActive: true, displayOrder: 1 }),
          expect.objectContaining({ name: 'Família', price: 49.9, isDefault: false, isActive: true, displayOrder: 2 }),
        ],
      }));
    });

    it('allows only one active variation to be the default', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      component.addSizeVariation();
      fixture.detectChanges();

      const radios = fixture.nativeElement.querySelectorAll('.default-radio input[type="radio"]') as NodeListOf<HTMLInputElement>;
      radios[1].click();
      fixture.detectChanges();

      expect(component.sizeVariations()[0].isDefault).toBe(false);
      expect(component.sizeVariations()[1].isDefault).toBe(true);
    });

    it('disables the default radio for an inactive variation so it cannot become the default', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      component.addSizeVariation();
      fixture.detectChanges();

      const activeCheckboxes = fixture.nativeElement.querySelectorAll('.switch input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
      activeCheckboxes[1].checked = false;
      activeCheckboxes[1].dispatchEvent(new Event('change'));
      fixture.detectChanges();

      const radios = fixture.nativeElement.querySelectorAll('.default-radio input[type="radio"]') as NodeListOf<HTMLInputElement>;
      expect(radios[0].disabled).toBe(false);
      expect(radios[1].disabled).toBe(true);

      radios[1].click();
      fixture.detectChanges();

      expect(component.sizeVariations()[0].isDefault).toBe(true);
      expect(component.sizeVariations()[1].isDefault).toBe(false);
    });

    it('promotes another active variation to default when the current default is deactivated', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      component.addSizeVariation();
      fixture.detectChanges();

      expect(component.sizeVariations()[0].isDefault).toBe(true);

      const activeCheckboxes = fixture.nativeElement.querySelectorAll('.switch input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
      activeCheckboxes[0].checked = false;
      activeCheckboxes[0].dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(component.sizeVariations()[0].isActive).toBe(false);
      expect(component.sizeVariations()[0].isDefault).toBe(false);
      expect(component.sizeVariations()[1].isActive).toBe(true);
      expect(component.sizeVariations()[1].isDefault).toBe(true);
    });

    it('leaves no default when every variation is deactivated', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      component.addSizeVariation();
      fixture.detectChanges();

      const activeCheckboxes = fixture.nativeElement.querySelectorAll('.switch input[type="checkbox"]') as NodeListOf<HTMLInputElement>;
      activeCheckboxes[0].checked = false;
      activeCheckboxes[0].dispatchEvent(new Event('change'));
      activeCheckboxes[1].checked = false;
      activeCheckboxes[1].dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(component.sizeVariations().some((v) => v.isDefault)).toBe(false);
      expect(component.sizeVariations().some((v) => v.isActive)).toBe(false);
    });

    it('labels the variation name, description and price inputs for assistive technology', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      fixture.detectChanges();

      const row = fixture.nativeElement.querySelector('.variation-row.size-row') as HTMLElement;
      const [nameInput, descriptionInput] = Array.from(row.querySelectorAll('input.var-input')) as HTMLInputElement[];
      const priceInput = row.querySelector('.money-input') as HTMLInputElement;

      expect(nameInput.getAttribute('aria-label')).toBe('Nome da variação 1');
      expect(descriptionInput.getAttribute('aria-label')).toBe('Descrição da variação 1');
      expect(priceInput.getAttribute('aria-label')).toBe('Preço da variação 1');
    });

    it('removes a variation and promotes the first remaining one as default', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      component.addSizeVariation();
      fixture.detectChanges();

      const removeButtons = fixture.nativeElement.querySelectorAll('.v-cell-action button') as NodeListOf<HTMLButtonElement>;
      removeButtons[0].click();
      fixture.detectChanges();

      expect(component.sizeVariations()).toHaveLength(1);
      expect(component.sizeVariations()[0].isDefault).toBe(true);
    });

    it('reorders variations when the reorder event fires and marks the form dirty', () => {
      const { component } = renderSizeEditor();
      component.addSizeVariation();
      component.addSizeVariation();
      const [first, second] = component.sizeVariations();
      const complete = jest.fn();
      component.formDirty.set(false);

      component.reorderSizeVariations({ detail: { from: 0, to: 1, complete } } as unknown as CustomEvent);

      expect(component.sizeVariations().map((v) => v.uid)).toEqual([second.uid, first.uid]);
      expect(component.formDirty()).toBe(true);
      expect(complete).toHaveBeenCalled();
    });

    it('labels each variation reorder handle with the variation name', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      component.sizeVariations.update((list) => list.map((v) => ({ ...v, name: 'Família' })));
      fixture.detectChanges();

      const handle = fixture.nativeElement.querySelector('.variation-row.size-row .drag-handle') as HTMLElement;
      expect(handle.getAttribute('aria-label')).toBe('Arrastar variação Família');
    });

    it('labels each variation delete button with the variation name', () => {
      const { fixture, component } = renderSizeEditor();
      component.addSizeVariation();
      component.sizeVariations.update((list) => list.map((v) => ({ ...v, name: 'Família' })));
      fixture.detectChanges();

      const button = fixture.nativeElement.querySelector('.v-cell-action button') as HTMLButtonElement;
      expect(button.getAttribute('aria-label')).toBe('Remover variação Família');
    });

    it('adds a free-form variation named Família and saves it in the create payload', () => {
      const { fixture, component } = renderSizeEditor();
      component.storeId.set('store-1');
      component.productName.set('Pizza');
      component.productCatId.set('cat-1');
      component.productPrice.set('10,00');
      component.productImage.set('https://img.test/pizza.png');
      component.addSizeVariation();
      fixture.detectChanges();

      const row = fixture.nativeElement.querySelector('.variation-row.size-row') as HTMLElement;
      const [nameInput, descriptionInput] = Array.from(row.querySelectorAll('input.var-input')) as HTMLInputElement[];
      nameInput.value = 'Família';
      nameInput.dispatchEvent(new Event('input'));
      descriptionInput.value = 'Serve 3 pessoas';
      descriptionInput.dispatchEvent(new Event('input'));
      const priceInput = row.querySelector('.money-input') as HTMLInputElement;
      priceInput.value = '3990';
      priceInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      storeServiceMock.createProduct.mockReturnValue(of({ id: 'product-new', optionGroups: [] }));

      component.saveProduct();

      expect(storeServiceMock.createProduct).toHaveBeenCalledWith('store-1', expect.objectContaining({
        saleMode: 'size',
        variations: [
          expect.objectContaining({
            name: 'Família',
            description: 'Serve 3 pessoas',
            price: 39.9,
            isActive: true,
            displayOrder: 1,
          }),
        ],
      }));
    });
  });
});

describe('SellerProductsPageComponent wizard separation', () => {
  it('does not inherit wizard navigation from the shared state', () => {
    const component = Object.create(SellerProductsPageComponent.prototype) as unknown as Record<string, unknown>;

    expect(component['goNext']).toBeUndefined();
    expect(component['goBack']).toBeUndefined();
  });
});
