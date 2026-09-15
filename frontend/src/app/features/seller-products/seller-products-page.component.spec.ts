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
});
