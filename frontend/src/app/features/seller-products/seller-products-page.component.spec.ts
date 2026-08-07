import { CUSTOM_ELEMENTS_SCHEMA, NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
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
});
