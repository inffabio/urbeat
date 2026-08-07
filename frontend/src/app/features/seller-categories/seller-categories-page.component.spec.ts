import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { SubscriptionService } from '../../core/services/subscription.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerCategoriesPageComponent } from './seller-categories-page.component';

describe('SellerCategoriesPageComponent', () => {
  let storeServiceMock: {
    getMyStore: jest.Mock;
    getStoreCategories: jest.Mock;
    getStoreProducts: jest.Mock;
    createStoreCategory: jest.Mock;
    updateStoreCategory: jest.Mock;
    deleteStoreCategory: jest.Mock;
    reorderStoreCategories: jest.Mock;
  };

  beforeEach(async () => {
    storeServiceMock = {
      getMyStore: jest.fn().mockReturnValue(of({ id: 'store-1' })),
      getStoreCategories: jest.fn().mockReturnValue(of([
        { id: 'cat-1', name: 'Burgers', displayOrder: 1, isActive: true, description: 'Hambúrgueres artesanais', isFeatured: false },
        { id: 'cat-2', name: 'Batatas', displayOrder: 2, isActive: false, description: '', isFeatured: false },
      ])),
      getStoreProducts: jest.fn().mockReturnValue(of([
        { id: 'product-1', categoryId: 'cat-1' },
        { id: 'product-2', categoryId: 'cat-1' },
        { id: 'product-3', categoryId: 'cat-2' },
      ])),
      createStoreCategory: jest.fn(),
      updateStoreCategory: jest.fn(),
      deleteStoreCategory: jest.fn(),
      reorderStoreCategories: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerCategoriesPageComponent],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: SubscriptionService, useValue: { getMySubscription: jest.fn().mockReturnValue(of({})) } },
        { provide: ToastService, useValue: { showSuccess: jest.fn(), showError: jest.fn() } },
      ],
    }).compileComponents();
  });

  it('renders the documented cardapio categories structure', () => {
    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Categorias');
    expect(fixture.nativeElement.textContent).toContain('Organize as categorias do seu cardápio');
    expect(fixture.nativeElement.textContent).toContain('Categorias cadastradas');
    expect(fixture.nativeElement.textContent).toContain('Arraste para reordenar');
    expect(fixture.nativeElement.textContent).toContain('Nova categoria');
    expect(fixture.nativeElement.textContent).toContain('Descrição opcional');
    expect(fixture.nativeElement.textContent).toContain('Arraste para reordenar');
    expect(fixture.nativeElement.querySelector('.seller-table')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.form-card.content-card')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('textarea')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="category-status-active"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="category-status-inactive"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#categoryOrder').textContent).toContain('Primeira posição');
    expect(fixture.nativeElement.querySelector('#categoryOrder').textContent).toContain('Última posição');
    expect(fixture.nativeElement.textContent).toContain('2');
  });

  it('submits the complete category form to the backend', () => {
    storeServiceMock.createStoreCategory.mockReturnValue(of({
      id: 'cat-3', name: 'Sobremesas', description: 'Doces da casa', displayOrder: 3,
      isActive: false, isFeatured: false,
    }));
    storeServiceMock.reorderStoreCategories.mockReturnValue(of(void 0));
    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.newName.set('Sobremesas');
    fixture.componentInstance.newDescription.set('Doces da casa');
    fixture.componentInstance.newIsActive.set(false);
    fixture.componentInstance.addCategory();

    expect(storeServiceMock.createStoreCategory).toHaveBeenCalledWith('store-1', {
      name: 'Sobremesas', description: 'Doces da casa', displayOrder: 3, isActive: false, isFeatured: false,
    });
  });

  it('persists a newly created category in the selected first position', () => {
    storeServiceMock.createStoreCategory.mockReturnValue(of({
      id: 'cat-3', name: 'Sobremesas', description: '', displayOrder: 1,
      isActive: true, isFeatured: false,
    }));
    storeServiceMock.reorderStoreCategories.mockReturnValue(of(void 0));
    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.newName.set('Sobremesas');
    fixture.componentInstance.newDisplayOrder.set(1);
    fixture.componentInstance.saveCategory();

    expect(storeServiceMock.reorderStoreCategories).toHaveBeenCalledWith('store-1', [
      { id: 'cat-3', displayOrder: 1 },
      { id: 'cat-1', displayOrder: 2 },
      { id: 'cat-2', displayOrder: 3 },
    ]);
  });

  it('edits and cancels a category without losing persisted data', () => {
    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.startEdit(fixture.componentInstance.sortedCategories()[0]);
    expect(fixture.componentInstance.editingCategoryId()).toBe('cat-1');
    expect(fixture.componentInstance.formTitle()).toBe('Editar categoria');

    fixture.componentInstance.cancelEdit();
    expect(fixture.componentInstance.editingCategoryId()).toBeNull();
    expect(fixture.componentInstance.formTitle()).toBe('Nova categoria');
  });

  it('persists category reordering through the backend', () => {
    storeServiceMock.reorderStoreCategories.mockReturnValue(of(void 0));
    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.moveCategory(fixture.componentInstance.sortedCategories()[1], -1);

    expect(storeServiceMock.reorderStoreCategories).toHaveBeenCalledWith('store-1', [
      { id: 'cat-2', displayOrder: 1 },
      { id: 'cat-1', displayOrder: 2 },
    ]);
  });

  it('blocks deletion when the category has associated products', () => {
    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();
    const confirmSpy = jest.spyOn(window, 'confirm');

    fixture.componentInstance.deleteCategory(fixture.componentInstance.sortedCategories()[0]);

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(storeServiceMock.deleteStoreCategory).not.toHaveBeenCalled();
    expect(TestBed.inject(ToastService).showError).toHaveBeenCalledWith(
      'Não é possível excluir "Burgers" porque ela possui 2 produto(s) associado(s).',
    );
    confirmSpy.mockRestore();
  });

  it('confirms and deletes an empty category', () => {
    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();
    const category = fixture.componentInstance.sortedCategories()[1];
    fixture.componentInstance.productCounts.set({ 'cat-1': 2 });
    storeServiceMock.deleteStoreCategory.mockReturnValue(of(void 0));
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(true);

    fixture.componentInstance.deleteCategory(category);

    expect(confirmSpy).toHaveBeenCalledWith('Deseja realmente excluir a categoria "Batatas"?');
    expect(storeServiceMock.deleteStoreCategory).toHaveBeenCalledWith('store-1', 'cat-2');
    confirmSpy.mockRestore();
  });

  it('renders the shared retry state when categories fail to load', () => {
    storeServiceMock.getMyStore.mockReturnValue(throwError(() => new Error('network')));

    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar as categorias.');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-error')).not.toBeNull();
  });

  it('formats the subscription due date in Sao Paulo time', () => {
    TestBed.overrideProvider(SubscriptionService, {
      useValue: { getMySubscription: jest.fn().mockReturnValue(of({ nextDueDateUtc: '2026-08-10T01:30:00.000Z' })) },
    });

    const fixture = TestBed.createComponent(SellerCategoriesPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.subscriptionDueDate()).toBe('09/08/2026');
  });
});
