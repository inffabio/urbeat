import { TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';
import { StorePageComponent } from './store-page.component';
import { StoreService } from '../../core/services/store.service';
import { CatalogService } from '../../core/services/catalog.service';
import { CartService } from '../../core/services/cart.service';
import { StoreFilterStateService } from '../../core/services/store-filter-state.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthService } from '../../core/services/auth.service';
import { ActivatedRoute, Router } from '@angular/router';
import { CartItem } from '../../shared/models/cart-item.model';
import { of } from 'rxjs';

describe('StorePageComponent — feature logic', () => {
  let component: StorePageComponent;
  let cartService: CartService;

  const mockStoreService = { getStoreByPath: jest.fn() };
  const mockCatalogService = { getCategories: jest.fn(), getProducts: jest.fn() };
  const mockRouter = { navigate: jest.fn() };
  const mockToastService = { showWarning: jest.fn() };
  const mockRoute = { snapshot: { paramMap: { get: jest.fn() } } };
  const mockAuthService = {
    customerProfile: jest.fn(),
    isLoggedIn: jest.fn().mockReturnValue(false),
    restoreCustomerSession: jest.fn().mockReturnValue(of(null)),
  };

  beforeEach(async () => {
    (globalThis as any).IntersectionObserver = jest.fn().mockImplementation(() => ({
      observe: jest.fn(),
      disconnect: jest.fn(),
    }));

    await TestBed.configureTestingModule({
      imports: [StorePageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        CartService,
        StoreFilterStateService,
        { provide: StoreService, useValue: mockStoreService },
        { provide: CatalogService, useValue: mockCatalogService },
        { provide: ToastService, useValue: mockToastService },
        { provide: AuthService, useValue: mockAuthService },
        { provide: Router, useValue: mockRouter },
        { provide: ActivatedRoute, useValue: mockRoute },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StorePageComponent);
    component = fixture.componentInstance;
    cartService = TestBed.inject(CartService);
    jest.clearAllMocks();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe('cartQuantityFor', () => {
    it('should aggregate quantities by productId', () => {
      cartService.items.set([
        { productId: 'p1', quantity: 2, unitPrice: 10, productName: 'A' } as CartItem,
        { productId: 'p2', quantity: 1, unitPrice: 15, productName: 'B' } as CartItem,
        { productId: 'p1', quantity: 3, unitPrice: 10, productName: 'A' } as CartItem,
      ]);
      expect(component.cartQuantityFor('p1')).toBe(5);
      expect(component.cartQuantityFor('p2')).toBe(1);
    });

    it('should return 0 for unknown product', () => {
      cartService.items.set([]);
      expect(component.cartQuantityFor('unknown')).toBe(0);
    });
  });

  describe('productSections', () => {
    it('should return empty with no products loaded', () => {
      expect(component.productSections()).toEqual([]);
    });

    it('should keep products grouped in category button order', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set([
        { id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1 } as any,
        { id: 'c2', storeId: 's1', name: 'Bebidas', displayOrder: 2 } as any,
      ]);
      component.products.set([
        { id: 'p2', storeId: 's1', categoryId: 'c2', name: 'Suco', displayOrder: 1, isFeatured: false } as any,
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel', displayOrder: 1, isFeatured: false } as any,
      ]);

      expect(component.productSections().map((section) => section.name)).toEqual(['Entradas', 'Bebidas']);
    });

    it('should not create synthetic sections for featured or uncategorized products', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set([
        { id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1 } as any,
      ]);
      component.products.set([
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel', displayOrder: 1, isFeatured: true } as any,
        { id: 'p2', storeId: 's1', categoryId: 'missing', name: 'Produto solto', displayOrder: 2, isFeatured: false } as any,
      ]);

      expect(component.categoryTabs().map((tab) => tab.name)).toEqual(['Todos', 'Entradas']);
      expect(component.productSections().map((section) => section.name)).toEqual(['Entradas']);
      expect(component.productSections()[0].products.map((product) => product.id)).toEqual(['p1']);
    });

    it('should ignore categories that do not belong to the current store', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set([
        { id: 'c1', storeId: 's1', name: 'Da loja', displayOrder: 1 } as any,
        { id: 'c2', storeId: 's2', name: 'Outra loja', displayOrder: 2 } as any,
      ]);
      component.products.set([
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel', displayOrder: 1, isFeatured: false } as any,
        { id: 'p2', storeId: 's1', categoryId: 'c2', name: 'Suco', displayOrder: 1, isFeatured: false } as any,
      ]);

      expect(component.categoryTabs().map((tab) => tab.name)).toEqual(['Todos', 'Da loja']);
      expect(component.productSections().map((section) => section.name)).toEqual(['Da loja']);
      expect(component.productSections()[0].products.map((product) => product.id)).toEqual(['p1']);
    });

    it('should always include Todos as the first category tab', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set([
        { id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1 } as any,
      ]);
      component.products.set([
        { id: 'p1', categoryId: 'c1', storeId: 's1', name: 'Pastel', displayOrder: 1 } as any,
      ]);

      expect(component.categoryTabs()).toEqual([
        { id: 'todos', name: 'Todos' },
        { id: 'c1', name: 'Entradas' },
      ]);
    });

    it('should show only current-store categories that have current-store products', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set([
        { id: 'c1', storeId: 's1', name: 'Com produto', displayOrder: 1 } as any,
        { id: 'c2', storeId: 's1', name: 'Vazia', displayOrder: 2 } as any,
        { id: 'c3', storeId: 's2', name: 'Outra loja', displayOrder: 3 } as any,
      ]);
      component.products.set([
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel', displayOrder: 1 } as any,
        { id: 'p2', storeId: 's1', categoryId: 'c3', name: 'Produto contaminado', displayOrder: 2 } as any,
        { id: 'p3', storeId: 's2', categoryId: 'c1', name: 'Produto de outra loja', displayOrder: 3 } as any,
      ]);

      expect(component.categoryTabs().map((tab) => tab.name)).toEqual(['Todos', 'Com produto']);
      expect(component.productSections().map((section) => section.name)).toEqual(['Com produto']);
      expect(component.productSections()[0].products.map((product) => product.id)).toEqual(['p1']);
    });
  });

  describe('statusText', () => {
    it('should return empty when store is null', () => {
      expect(component.statusText()).toBe('');
    });
  });

  describe('returning customer', () => {
    it('should expose the first customer name for the small storefront greeting', () => {
      mockAuthService.customerProfile.mockReturnValue({ fullName: 'Maria Oliveira' });

      expect((component as any).customerFirstName()).toBe('Maria');
    });
  });

  describe('etaText', () => {
    it('should hide delivery estimate when backend says store is closed now', () => {
      component.store.set({ isOpen: true, isOpenNow: false, initialMinute: 30, finalMinute: 60 } as any);

      expect(component.etaText()).toBe('');
    });
  });

  describe('goToProduct', () => {
    const product = { id: 'p1', name: 'Produto' } as any;

    it('should block product selection and show backend closed message when store is closed now', () => {
      component.store.set({ slug: 'loja', isOpen: true, isOpenNow: false, closedMessage: 'A loja só estará aberta Terça às 18:00.' } as any);

      component.goToProduct(product);

      expect(mockToastService.showWarning).toHaveBeenCalledWith('A loja só estará aberta Terça às 18:00.');
      expect(mockRouter.navigate).not.toHaveBeenCalled();
    });

    it('should navigate to product when backend says store is open now', () => {
      component.store.set({ slug: 'loja', isOpen: true, isOpenNow: true } as any);

      component.goToProduct(product);

      expect(mockRouter.navigate).toHaveBeenCalledWith(['/', 'loja', 'produto', 'p1'], { state: { product } });
    });
  });

  describe('automatic status refresh', () => {
    it('should refresh store status at backend-provided transition time', () => {
      jest.useFakeTimers();
      jest.setSystemTime(new Date('2026-07-28T20:59:59.000Z'));
      mockRoute.snapshot.paramMap.get.mockReturnValue('loja');
      mockCatalogService.getCategories.mockReturnValue(of([]));
      mockCatalogService.getProducts.mockReturnValue(of([]));
      mockStoreService.getStoreByPath
        .mockReturnValueOnce(of({ id: 's1', name: 'Loja', slug: 'loja', logoUrl: '', isOpenNow: true, nextStatusChangeAt: '2026-07-28T21:00:00.000Z' } as any))
        .mockReturnValueOnce(of({ id: 's1', name: 'Loja', slug: 'loja', logoUrl: '', isOpenNow: false, closedMessage: 'A loja só estará aberta Quarta às 18:00.' } as any));

      component.ngOnInit();
      jest.advanceTimersByTime(2000);

      expect(mockStoreService.getStoreByPath).toHaveBeenCalledTimes(2);
      expect(component.store()?.isOpenNow).toBe(false);
      expect(component.store()?.closedMessage).toBe('A loja só estará aberta Quarta às 18:00.');
    });
  });

  describe('activeCategoryId', () => {
    it('should default to todos', () => {
      expect(component.TODOS_ID).toBe('todos');
    });
  });

  describe('onAddToCard', () => {
    it('should add item to cart', () => {
      const product = {
        id: 'p1', storeId: 's1', categoryId: 'c1', categoryName: 'Cat',
        name: 'Test', description: '', price: 10,
        isAvailable: true, isFeatured: false, displayOrder: 1,
        additionals: [], choiceOptions: [], variations: [], optionGroups: [],
      };
      component.onAddToCard(product as any);
      expect(cartService.items().length).toBe(1);
      expect(cartService.items()[0].productName).toBe('Test');
    });
  });

  describe('onRemoveFromCard', () => {
    it('should decrease quantity', () => {
      cartService.items.set([
        { id: 'item1', productId: 'p1', quantity: 2, unitPrice: 10, productName: 'A' } as CartItem,
      ]);
      const product = { id: 'p1' } as any;
      component.onRemoveFromCard(product);
      expect(cartService.items()[0].quantity).toBe(1);
    });

    it('should remove item when quantity reaches 0', () => {
      cartService.items.set([
        { id: 'item1', productId: 'p1', quantity: 1, unitPrice: 10, productName: 'A' } as CartItem,
      ]);
      const product = { id: 'p1' } as any;
      component.onRemoveFromCard(product);
      expect(cartService.items().length).toBe(0);
    });
  });

  describe('clearFilters', () => {
    it('should reset category to todos', () => {
      component.activeCategoryId.set('c1');
      component.clearFilters();
      expect(component.activeCategoryId()).toBe('todos');
    });
  });
});
