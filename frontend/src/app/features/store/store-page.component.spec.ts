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
import { BehaviorSubject, Observable, of, Subject, throwError } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

describe('StorePageComponent — feature logic', () => {
  let component: StorePageComponent;
  let cartService: CartService;
  let routeParamMap: BehaviorSubject<{ get: (key: string) => string | null }>;
  let mockRoute: {
    snapshot: { paramMap: { get: jest.Mock } };
    paramMap: Observable<{ get: (key: string) => string | null }>;
  };

  const mockStoreService = { getStoreByPath: jest.fn() };
  const mockCatalogService = { getCategories: jest.fn(), getProducts: jest.fn() };
  const mockRouter = { navigate: jest.fn() };
  const mockToastService = { showWarning: jest.fn() };
  const mockAuthService = {
    customerProfile: jest.fn(),
    isLoggedIn: jest.fn().mockReturnValue(false),
    restoreCustomerSession: jest.fn().mockReturnValue(of(null)),
  };

  beforeEach(async () => {
    routeParamMap = new BehaviorSubject({ get: (key: string) => (key === 'storePath' ? 'loja' : null) });
    mockRoute = {
      snapshot: { paramMap: { get: jest.fn() } },
      paramMap: routeParamMap.asObservable(),
    };

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

  it('keeps the scrolling header above a sticky category row and products', () => {
    const template = readFileSync(resolve(__dirname, 'store-page.component.html'), 'utf8');
    const styles = readFileSync(resolve(__dirname, 'store-page.component.scss'), 'utf8');

    expect(template.indexOf('class="store-header"')).toBeLessThan(template.indexOf('class="store-categories"'));
    expect(template.indexOf('class="store-categories"')).toBeLessThan(template.indexOf('store-products-panel'));
    expect(template).toContain('(scroll)="onStoreScroll()"');
    expect(styles).toMatch(/\.store-categories[\s\S]*position:\s*sticky/);
    expect(styles).toMatch(/\.store-content[\s\S]*overflow-y:\s*auto/);
  });

  it('lays the store content out as a flex column so the products panel can fill the empty space', () => {
    const styles = readFileSync(resolve(__dirname, 'store-page.component.scss'), 'utf8');

    expect(styles).toMatch(/\.store-content[\s\S]*display:\s*flex[\s\S]*flex-direction:\s*column/);
    expect(styles).toMatch(/\.store-products-panel[\s\S]*flex:\s*1 0 auto/);
  });

  it('paints the storefront canvas and loading state with the white surface instead of the warm global canvas', () => {
    const styles = readFileSync(resolve(__dirname, 'store-page.component.scss'), 'utf8');
    const hostBlock = styles.match(/:host\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const contentBlock = styles.match(/\.store-content\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const loadingBlock = styles.match(/\.loading-overlay\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';

    expect(hostBlock).toMatch(/background:\s*var\(--app-surface, #fff\)/);
    expect(contentBlock).toMatch(/background:\s*var\(--app-surface, #fff\)/);
    expect(loadingBlock).toMatch(/background:\s*var\(--app-surface, #fff\)/);
    expect(styles).not.toContain('#ede9e3');
    expect(styles).not.toMatch(/var\(--app-bg[,)]/);
  });

  it('keeps the footer clearance inside the products panel surface, driven by the measured shell variable', () => {
    const styles = readFileSync(resolve(__dirname, 'store-page.component.scss'), 'utf8');

    const contentBlock = styles.match(/\.store-content\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';
    const panelBlock = styles.match(/\.store-products-panel\s*\{([\s\S]*?)\n\}/)?.[1] ?? '';

    expect(contentBlock).not.toContain('padding-bottom');
    expect(panelBlock).toContain('padding-bottom: calc(var(--store-footer-clearance, 72px) + 8px)');
    expect(panelBlock).not.toContain('padding-bottom: calc(64px');
    expect(styles).not.toContain('padding-bottom: calc(64px + max(8px, env(safe-area-inset-bottom, 0px)))');
    expect(styles).toMatch(/\.store-panel\s*\{[\s\S]*background:\s*var\(--app-surface/);
    expect(styles).toMatch(/\.store-products-panel\s*\{[\s\S]*flex:\s*1 0 auto/);
  });

  it('keeps the catalog error on the products surface so it inherits the panel background and footer clearance', () => {
    const template = readFileSync(resolve(__dirname, 'store-page.component.html'), 'utf8');
    const styles = readFileSync(resolve(__dirname, 'store-page.component.scss'), 'utf8');

    const panel = template.indexOf('class="store-panel store-products-panel"');
    const banner = template.indexOf('catalog-error-banner');
    const productList = template.indexOf('class="product-list"');

    expect(panel).toBeGreaterThan(-1);
    expect(banner).toBeGreaterThan(panel);
    expect(productList).toBeGreaterThan(banner);
    expect(styles).toMatch(/\.catalog-error-banner\s*\{[\s\S]*margin:\s*20px 0/);
  });

  it('shows the branded food delivery spinner while the storefront is loading from the server', () => {
    const template = readFileSync(resolve(__dirname, 'store-page.component.html'), 'utf8');
    const spinner = readFileSync(
      resolve(__dirname, '../../shared/components/food-delivery-spinner/food-delivery-spinner.component.ts'),
      'utf8',
    );

    expect(template).toContain('class="loading-overlay"');
    expect(template).toContain('<app-food-delivery-spinner');
    expect(template).toContain('message="Preparando o cardapio..."');
    expect(spinner).toContain('delivery-rider-scene');
    expect(spinner).toContain('burger-assembly');
    expect(spinner).not.toContain('clock-ticks');
    expect(spinner).toContain('burger-top-bun');
    expect(spinner).toContain('burger-lettuce');
    expect(spinner).toContain('burger-tomato');
    expect(spinner).toContain('burger-cheese');
    expect(spinner).toContain('burger-patty');
    expect(spinner).toContain('burger-bottom-bun');
    expect(spinner).not.toContain('clock-active');
  });

  describe('published hero/banner and logo composition', () => {
    function stylesText(): string {
      return readFileSync(resolve(__dirname, 'store-page.component.scss'), 'utf8');
    }

    function ruleBody(selector: string): string {
      return stylesText().match(new RegExp(`${selector}\\s*\\{([\\s\\S]*?)\\n\\}`))?.[1] ?? '';
    }

    function pxValue(block: string, property: RegExp): number {
      return Number(block.match(property)?.[1] ?? '');
    }

    it('sizes the published hero/banner and its skeleton to 286px', () => {
      const heroBlock = ruleBody('\\.hero');
      const skeletonBlock = ruleBody('\\.skeleton-hero');

      expect(heroBlock).toMatch(/height:\s*286px/);
      expect(skeletonBlock).toMatch(/height:\s*286px/);
    });

    it('keeps the banner full-bleed with cover fit and the ink fallback behind it', () => {
      const css = stylesText();

      expect(css).toMatch(/\.hero\s*\{[\s\S]*?background:\s*var\(--app-ink[\s\S]*?img\s*\{[\s\S]*?width:\s*100%[\s\S]*?height:\s*100%[\s\S]*?object-fit:\s*cover/);
    });

    it('draws the logo as a 144px white disc lifted over the hero/banner bottom', () => {
      const css = stylesText();
      const logoBlock = ruleBody('\\.store-logo');

      expect(logoBlock).toMatch(/position:\s*absolute/);
      expect(logoBlock).toMatch(/top:\s*-26px/);
      expect(logoBlock).toMatch(/left:\s*50%/);
      expect(logoBlock).toMatch(/transform:\s*translateX\(-50%\)/);
      expect(logoBlock).toMatch(/width:\s*144px/);
      expect(logoBlock).toMatch(/height:\s*144px/);
      expect(logoBlock).toMatch(/border-radius:\s*50%/);
      expect(logoBlock).toMatch(/background:\s*var\(--app-surface/);
    });

    it('lifts the 144px logo 12px above the centered position over the hero/banner bottom edge', () => {
      const heroBlock = ruleBody('\\.hero');
      const panelBlock = ruleBody('\\.store-panel');
      const logoBlock = ruleBody('\\.store-logo');

      const heroHeight = pxValue(heroBlock, /height:\s*(\d+)px/);
      const panelMargin = pxValue(panelBlock, /margin-top:\s*(-?\d+)px/);
      const logoSize = pxValue(logoBlock, /width:\s*(\d+)px/);
      const logoTop = pxValue(logoBlock, /top:\s*(-?\d+)px/);

      const panelTop = heroHeight + panelMargin;
      const discTop = panelTop + logoTop;
      const discBottom = discTop + logoSize;
      const discCenter = discTop + logoSize / 2;

      expect(panelTop).toBe(heroHeight - 58);
      expect(discTop).toBe(heroHeight - 84);
      expect(discBottom).toBe(heroHeight + 60);
      expect(discCenter).toBe(heroHeight - 12);
    });

    it('fits the logo artwork at 132px with object-fit contain so it is never cropped', () => {
      const css = stylesText();
      const imageBlock = css.match(/\.store-logo\s*\{[\s\S]*?img\s*\{([\s\S]*?)\n\s+\}/)?.[1] ?? '';

      expect(imageBlock).toMatch(/width:\s*132px/);
      expect(imageBlock).toMatch(/height:\s*132px/);
      expect(imageBlock).toMatch(/object-fit:\s*contain/);
      expect(imageBlock).not.toMatch(/object-fit:\s*cover/);
      expect(imageBlock).toMatch(/border-radius:\s*50%/);
    });

    it('keeps the panel pulled over the hero and clears the identity text below the enlarged logo', () => {
      const panelBlock = ruleBody('\\.store-panel');
      const logoBlock = ruleBody('\\.store-logo');

      const panelPaddingTop = pxValue(panelBlock, /padding:\s*(\d+)px/);
      const logoSize = pxValue(logoBlock, /width:\s*(\d+)px/);
      const logoTop = pxValue(logoBlock, /top:\s*(-?\d+)px/);
      const logoBottomOffset = logoTop + logoSize;

      expect(panelBlock).toMatch(/margin-top:\s*-58px/);
      expect(panelBlock).toMatch(/padding:\s*146px 18px 0/);
      expect(panelPaddingTop).toBeGreaterThan(logoBottomOffset);
    });

    it('uses the same hero/logo geometry in the loading skeleton and keeps its text clear', () => {
      const heroBlock = ruleBody('\\.hero');
      const panelBlock = ruleBody('\\.store-panel');
      const logoBlock = ruleBody('\\.store-logo');
      const skeletonHeroBlock = ruleBody('\\.skeleton-hero');
      const skeletonPanelBlock = ruleBody('\\.skeleton-panel');
      const skeletonLogoBlock = ruleBody('\\.skeleton-logo');

      const heroHeight = pxValue(heroBlock, /height:\s*(\d+)px/);
      const panelMargin = pxValue(panelBlock, /margin-top:\s*(-?\d+)px/);
      const logoSize = pxValue(logoBlock, /width:\s*(\d+)px/);
      const logoTop = pxValue(logoBlock, /top:\s*(-?\d+)px/);

      expect(skeletonHeroBlock).toMatch(/height:\s*286px/);
      expect(skeletonPanelBlock).toMatch(/margin-top:\s*-58px/);
      expect(skeletonPanelBlock).toMatch(/padding:\s*146px 18px 18px/);
      expect(skeletonPanelBlock).toMatch(/position:\s*relative/);
      expect(skeletonLogoBlock).toMatch(/position:\s*absolute/);
      expect(skeletonLogoBlock).toMatch(/top:\s*-26px/);
      expect(skeletonLogoBlock).toMatch(/width:\s*144px/);
      expect(skeletonLogoBlock).toMatch(/height:\s*144px/);
      expect(skeletonLogoBlock).toMatch(/border-radius:\s*50%/);

      const skeletonPanelTop = heroHeight + pxValue(skeletonPanelBlock, /margin-top:\s*(-?\d+)px/);
      const skeletonDiscTop = skeletonPanelTop + pxValue(skeletonLogoBlock, /top:\s*(-?\d+)px/);
      const skeletonDiscBottom = skeletonDiscTop + pxValue(skeletonLogoBlock, /width:\s*(\d+)px/);

      expect(skeletonDiscTop).toBe(heroHeight - 84);
      expect(skeletonDiscBottom).toBe(heroHeight + 60);
      expect(skeletonDiscTop + logoSize / 2).toBe(heroHeight - 12);

      const skeletonPaddingTop = pxValue(skeletonPanelBlock, /padding:\s*(\d+)px/);
      expect(skeletonPaddingTop).toBeGreaterThan(skeletonDiscBottom - skeletonPanelTop);
    });
  });

  it('does not expose Destaque as a product classification on the storefront', () => {
    const template = readFileSync(resolve(__dirname, 'store-page.component.html'), 'utf8');

    expect(template).not.toContain('Destaque');
    expect(template).toContain('app-category-tabs');
  });

  it('scrolls a selected category title below the sticky category bar', () => {
    const categoryBar = { getBoundingClientRect: () => ({ height: 52 }) } as HTMLElement;
    const categoryTitle = {
      getBoundingClientRect: () => ({ top: 640 }),
    } as HTMLElement;
    const scrollContainer = {
      scrollTop: 120,
      getBoundingClientRect: () => ({ top: 0 }),
      scrollTo: jest.fn(),
    } as unknown as HTMLElement;
    let frameCallback: FrameRequestCallback | undefined;
    const requestAnimationFrame = jest.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
      frameCallback = callback;
      return 0;
    });
    const getElementById = jest.spyOn(document, 'getElementById')
      .mockImplementation((id) => id === 'cat-c1' ? categoryTitle : id === 'store-categories' ? categoryBar : id === 'store-content' ? scrollContainer : null);

    component.scrollToCategory('c1');
    frameCallback?.(0);

    expect(scrollContainer.scrollTop).toBe(700);
    expect(scrollContainer.scrollTo).toHaveBeenCalledWith({ top: 700, behavior: 'smooth' });

    getElementById.mockRestore();
    requestAnimationFrame.mockRestore();
  });

  it('scrolls the direct container reference after the layout frame', () => {
    const categoryBar = { getBoundingClientRect: () => ({ height: 52 }) } as HTMLElement;
    const scrollContainer = {
      scrollTop: 120,
      getBoundingClientRect: () => ({ top: 0 }),
      scrollTo: jest.fn(),
    } as unknown as HTMLElement;
    const categorySection = {
      nativeElement: {
        id: 'cat-c1',
        getBoundingClientRect: () => ({ top: 640 }),
      },
    };
    jest.spyOn(document, 'getElementById').mockImplementation((id) => id === 'store-categories' ? categoryBar : null);
    let frameCallback: FrameRequestCallback | undefined;
    const requestAnimationFrame = jest.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
      frameCallback = callback;
      return 0;
    });
    (component as any).scrollContent = { nativeElement: scrollContainer };
    (component as any).categorySections = { toArray: () => [categorySection] };

    component.scrollToCategory('c1');
    frameCallback?.(0);

    expect(scrollContainer.scrollTop).toBe(700);
    expect(scrollContainer.scrollTo).toHaveBeenCalledWith({ top: 700, behavior: 'smooth' });
    jest.restoreAllMocks();
  });

  it('highlights the last category whose title crossed the sticky bar without filtering Todos', () => {
    const categoryBar = { getBoundingClientRect: () => ({ height: 52 }) } as HTMLElement;
    const scrollContainer = {
      scrollTop: 400,
      getBoundingClientRect: () => ({ top: 0 }),
      removeEventListener: jest.fn(),
    } as unknown as HTMLElement;
    const firstSection = {
      id: 'cat-c1',
      querySelector: () => ({ getBoundingClientRect: () => ({ top: 30 }) }),
    } as unknown as HTMLElement;
    const secondSection = {
      id: 'cat-c2',
      querySelector: () => ({ getBoundingClientRect: () => ({ top: 80 }) }),
    } as unknown as HTMLElement;
    const thirdSection = {
      id: 'cat-c3',
      querySelector: () => ({ getBoundingClientRect: () => ({ top: 500 }) }),
    } as unknown as HTMLElement;
    const getElementById = jest.spyOn(document, 'getElementById')
      .mockImplementation((id) => {
        if (id === 'store-categories') return categoryBar;
        if (id === 'store-content') return scrollContainer;
        if (id === 'cat-c1') return firstSection;
        if (id === 'cat-c2') return secondSection;
        if (id === 'cat-c3') return thirdSection;
        return null;
      });

    component.store.set({ id: 's1' } as any);
    component.categories.set([
      { id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1 } as any,
      { id: 'c2', storeId: 's1', name: 'Bebidas', displayOrder: 2 } as any,
      { id: 'c3', storeId: 's1', name: 'Sobremesas', displayOrder: 3 } as any,
    ]);
    component.products.set([
      { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel' } as any,
      { id: 'p2', storeId: 's1', categoryId: 'c2', name: 'Suco' } as any,
      { id: 'p3', storeId: 's1', categoryId: 'c3', name: 'Bolo' } as any,
    ]);
    component.activeCategoryId.set('todos');
    (component as any).updateActiveCategoryFromScroll();

    expect(component.activeCategoryId()).toBe('todos');
    expect(component.highlightedCategoryId()).toBe('c1');
    expect(component.productSections()).toHaveLength(3);

    getElementById.mockRestore();
  });

  it('keeps Todos active before the first category reaches the sticky bar', () => {
    const categoryBar = { getBoundingClientRect: () => ({ height: 52 }) } as HTMLElement;
    const scrollContainer = {
      scrollTop: 0,
      getBoundingClientRect: () => ({ top: 0 }),
      removeEventListener: jest.fn(),
    } as unknown as HTMLElement;
    const firstSection = {
      id: 'cat-c1',
      querySelector: () => ({ getBoundingClientRect: () => ({ top: 500 }) }),
    } as unknown as HTMLElement;
    const getElementById = jest.spyOn(document, 'getElementById')
      .mockImplementation((id) => {
        if (id === 'store-categories') return categoryBar;
        if (id === 'store-content') return scrollContainer;
        if (id === 'cat-c1') return firstSection;
        return null;
      });

    component.store.set({ id: 's1' } as any);
    component.categories.set([{ id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1 } as any]);
    component.products.set([{ id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel' } as any]);
    component.activeCategoryId.set('todos');
    (component as any).updateActiveCategoryFromScroll();

    expect(component.activeCategoryId()).toBe('todos');
    expect(component.highlightedCategoryId()).toBe('todos');
    expect(component.productSections()).toHaveLength(1);

    getElementById.mockRestore();
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
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel', displayOrder: 1, specialClassification: null } as any,
        { id: 'p2', storeId: 's1', categoryId: 'missing', name: 'Produto solto', displayOrder: 2, specialClassification: 'Offers' } as any,
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

  describe('special classification sections', () => {
    const categories = [
      { id: 'c-lanches', storeId: 's1', name: 'Lanches', displayOrder: 1, isActive: true },
      { id: 'c-acomp', storeId: 's1', name: 'Acompanhamentos', displayOrder: 2, isActive: true },
      { id: 'c-bebidas', storeId: 's1', name: 'Bebidas', displayOrder: 3, isActive: true },
      { id: 'c-sobremesas', storeId: 's1', name: 'Sobremesas', displayOrder: 4, isActive: true },
    ];

    const products = [
      { id: 'x-bacon', storeId: 's1', categoryId: 'c-lanches', name: 'X-Bacon', displayOrder: 1, specialClassification: 'Offers' },
      { id: 'x-salada', storeId: 's1', categoryId: 'c-lanches', name: 'X-Salada', displayOrder: 2, specialClassification: null },
      { id: 'batata', storeId: 's1', categoryId: 'c-acomp', name: 'Batata', displayOrder: 1, specialClassification: 'BestSeller' },
      { id: 'coca', storeId: 's1', categoryId: 'c-bebidas', name: 'Coca', displayOrder: 1, specialClassification: null },
      { id: 'brownie', storeId: 's1', categoryId: 'c-sobremesas', name: 'Brownie', displayOrder: 1, specialClassification: 'New' },
    ];

    function seedCatalog(): void {
      component.store.set({ id: 's1' } as any);
      component.categories.set(categories as any);
      component.products.set(products as any);
    }

    /**
     * Stubs the storefront scroll geometry: the sticky category bar height, the scroll
     * container origin and the vertical position of each section title.
     */
    function mockScrollTops(tops: Record<string, number>): jest.SpyInstance {
      const categoryBar = { getBoundingClientRect: () => ({ height: 52 }) } as HTMLElement;
      const scrollContainer = {
        scrollTop: 400,
        getBoundingClientRect: () => ({ top: 0 }),
      } as unknown as HTMLElement;

      return jest.spyOn(document, 'getElementById').mockImplementation((id) => {
        if (id === 'store-categories') return categoryBar;
        if (id === 'store-content') return scrollContainer;

        const top = tops[id];
        if (top === undefined) return null;

        return {
          id,
          querySelector: () => ({ getBoundingClientRect: () => ({ top }) }),
        } as unknown as HTMLElement;
      });
    }

    it('orders the pills as Todos, special sections and seller categories', () => {
      seedCatalog();

      expect(component.categoryTabs().map((tab) => tab.name)).toEqual([
        'Todos',
        'Ofertas',
        'Mais vendidos',
        'Novidades',
        'Lanches',
        'Acompanhamentos',
        'Bebidas',
        'Sobremesas',
      ]);
      expect(component.categoryTabs().map((tab) => tab.id)).toEqual([
        'todos',
        'special:Offers',
        'special:BestSeller',
        'special:New',
        'c-lanches',
        'c-acomp',
        'c-bebidas',
        'c-sobremesas',
      ]);
    });

    it('shows special sections before categories in Todos', () => {
      seedCatalog();

      expect(component.productSections().map((section) => section.name)).toEqual([
        'Ofertas',
        'Mais vendidos',
        'Novidades',
        'Lanches',
        'Acompanhamentos',
        'Bebidas',
        'Sobremesas',
      ]);
    });

    it('keeps the same product id in the special section and in its original category', () => {
      seedCatalog();

      const offers = component.productSections().find((section) => section.id === 'special:Offers')!;
      const lanches = component.productSections().find((section) => section.id === 'c-lanches')!;

      expect(offers.products.map((product) => product.id)).toEqual(['x-bacon']);
      expect(lanches.products.map((product) => product.id)).toEqual(['x-bacon', 'x-salada']);
      expect(offers.products[0].id).toBe(lanches.products[0].id);
      expect(offers.products[0]).toBe(lanches.products[0]);
    });

    it('filters only the selected special section', () => {
      seedCatalog();
      component.activeCategoryId.set('special:BestSeller');

      expect(component.productSections()).toHaveLength(1);
      expect(component.productSections()[0].id).toBe('special:BestSeller');
      expect(component.productSections()[0].products.map((product) => product.id)).toEqual(['batata']);
    });

    it('keeps an explicit special selection when the user scrolls', () => {
      seedCatalog();
      component.activeCategoryId.set('special:Offers');

      (component as any).updateActiveCategoryFromScroll();

      expect(component.activeCategoryId()).toBe('special:Offers');
    });

    it('keeps every section visible in Todos while the scrollspy highlights a special shelf', () => {
      seedCatalog();
      component.activeCategoryId.set('todos');

      const getElementById = mockScrollTops({
        'cat-special:Offers': 10,
        'cat-special:BestSeller': 220,
        'cat-special:New': 780,
        'cat-c-lanches': 900,
        'cat-c-acomp': 940,
        'cat-c-bebidas': 990,
        'cat-c-sobremesas': 1200,
      });

      (component as any).updateActiveCategoryFromScroll();

      expect(component.activeCategoryId()).toBe('todos');
      expect(component.highlightedCategoryId()).toBe('special:Offers');
      expect(component.productSections().map((section) => section.id)).toEqual([
        'special:Offers',
        'special:BestSeller',
        'special:New',
        'c-lanches',
        'c-acomp',
        'c-bebidas',
        'c-sobremesas',
      ]);
      expect(component.productSections().flatMap((section) => section.products).length).toBeGreaterThan(0);

      getElementById.mockRestore();
    });

    it('keeps every section visible in Todos while the scrollspy highlights a seller category', () => {
      seedCatalog();
      component.activeCategoryId.set('todos');

      const getElementById = mockScrollTops({
        'cat-special:Offers': 0,
        'cat-special:BestSeller': 10,
        'cat-special:New': 20,
        'cat-c-lanches': 30,
        'cat-c-acomp': 40,
        'cat-c-bebidas': 50,
        'cat-c-sobremesas': 1900,
      });

      (component as any).updateActiveCategoryFromScroll();

      expect(component.activeCategoryId()).toBe('todos');
      expect(component.highlightedCategoryId()).toBe('c-bebidas');
      expect(component.productSections()).toHaveLength(7);

      getElementById.mockRestore();
    });

    it('filters and highlights only the explicitly selected special shelf', () => {
      seedCatalog();

      component.scrollToCategory('special:BestSeller');

      expect(component.activeCategoryId()).toBe('special:BestSeller');
      expect(component.highlightedCategoryId()).toBe('special:BestSeller');
      expect(component.productSections()).toHaveLength(1);
      expect(component.productSections()[0].id).toBe('special:BestSeller');
    });

    it('restores every section and the Todos pill when the user returns to Todos', () => {
      seedCatalog();

      component.scrollToCategory('special:BestSeller');
      component.scrollToCategory(component.TODOS_ID);

      expect(component.activeCategoryId()).toBe('todos');
      expect(component.highlightedCategoryId()).toBe('todos');
      expect(component.productSections()).toHaveLength(7);
    });

    it('drives the pill highlight from the scrollspy-aware state, not from the feed filter', () => {
      const template = readFileSync(resolve(__dirname, 'store-page.component.html'), 'utf8');

      expect(template).toContain('[activeId]="highlightedCategoryId()"');
    });

    it('filters only the selected seller category', () => {
      seedCatalog();
      component.activeCategoryId.set('c-bebidas');

      expect(component.productSections()).toHaveLength(1);
      expect(component.productSections()[0].id).toBe('c-bebidas');
      expect(component.productSections()[0].products.map((product) => product.id)).toEqual(['coca']);
    });

    it('hides empty special pills and sections', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set(categories as any);
      component.products.set(products.map((product) => ({
        ...product,
        specialClassification: product.id === 'x-bacon' ? 'Offers' : null,
      })) as any);

      expect(component.categoryTabs().map((tab) => tab.name)).toEqual([
        'Todos',
        'Ofertas',
        'Lanches',
        'Acompanhamentos',
        'Bebidas',
        'Sobremesas',
      ]);
      expect(component.specialSections().map((section) => section.id)).toEqual(['special:Offers']);
    });

    it('does not duplicate the same product inside a special section', () => {
      seedCatalog();

      for (const section of component.productSections()) {
        expect(new Set(section.products.map((product) => product.id)).size).toBe(section.products.length);
      }
    });

    it('keeps an unclassified product only in its category', () => {
      seedCatalog();

      const specialIds = component.specialSections().flatMap((section) => section.products.map((product) => product.id));
      const categoryIds = component.categoryProductSections().flatMap((section) => section.products.map((product) => product.id));

      expect(specialIds).not.toContain('x-salada');
      expect(specialIds).not.toContain('coca');
      expect(categoryIds).toContain('x-salada');
      expect(categoryIds).toContain('coca');
    });

    it('ignores special products outside the active catalog categories', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set(categories as any);
      component.products.set([
        ...products,
        { id: 'lost-offer', storeId: 's1', categoryId: 'missing', name: 'Oferta solta', displayOrder: 1, specialClassification: 'Offers' },
      ] as any);

      const offers = component.specialSections().find((section) => section.id === 'special:Offers')!;
      expect(offers.products.map((product) => product.id)).toEqual(['x-bacon']);
    });

    it('falls back to legacy flags with Offers precedence during the transition', () => {
      component.store.set({ id: 's1' } as any);
      component.categories.set([categories[0]] as any);
      component.products.set([
        { id: 'legacy', storeId: 's1', categoryId: 'c-lanches', name: 'Legado', displayOrder: 1, isFeatured: true, isBestSeller: true, isNew: true },
      ] as any);

      expect(component.specialSections().map((section) => section.id)).toEqual(['special:Offers']);
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

  describe('onBannerImageError', () => {
    it('hides a broken banner so the dark hero falls back instead of showing a broken image', () => {
      const image = { style: { display: '' } } as unknown as HTMLImageElement;

      component.onBannerImageError({ target: image } as Event);

      expect(image.style.display).toBe('none');
    });

    it('keeps the banner image element wired to an error fallback in the template', () => {
      const template = readFileSync(resolve(__dirname, 'store-page.component.html'), 'utf8');

      expect(template).toContain('(error)="onBannerImageError($event)"');
    });
  });

  describe('catalog load', () => {
    const validStore = {
      id: 's1', name: 'Loja Teste', slug: 'loja', logoUrl: '', isOpenNow: true,
    };

    it('renders the menu for a valid store', () => {
      mockStoreService.getStoreByPath.mockReturnValue(of(validStore));
      mockCatalogService.getCategories.mockReturnValue(of([
        { id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1, isActive: true, isFeatured: false },
      ]));
      mockCatalogService.getProducts.mockReturnValue(of([
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel' },
      ]));

      component.ngOnInit();

      expect(component.store()?.name).toBe('Loja Teste');
      expect(component.loadError()).toBe(false);
      expect(component.catalogError()).toBe(false);
      expect(component.loading()).toBe(false);
      expect(component.productSections().map((section) => section.name)).toEqual(['Entradas']);
    });

    it('surfaces an explicit catalog error instead of a full-page load error when the catalog endpoint fails', () => {
      mockStoreService.getStoreByPath.mockReturnValue(of(validStore));
      mockCatalogService.getCategories.mockReturnValue(of([]));
      mockCatalogService.getProducts.mockReturnValue(throwError(() => new Error('catalogo indisponivel')));

      component.ngOnInit();

      expect(component.store()?.name).toBe('Loja Teste');
      expect(component.loadError()).toBe(false);
      expect(component.catalogError()).toBe(true);
      expect(component.loading()).toBe(false);
    });

    it('keeps a full-page load error when the store itself cannot be resolved', () => {
      mockStoreService.getStoreByPath.mockReturnValue(throwError(() => new Error('loja nao encontrada')));

      component.ngOnInit();

      expect(component.store()).toBeNull();
      expect(component.loadError()).toBe(true);
      expect(component.catalogError()).toBe(false);
      expect(component.loading()).toBe(false);
      expect(mockCatalogService.getCategories).not.toHaveBeenCalled();
      expect(mockCatalogService.getProducts).not.toHaveBeenCalled();
    });

    it('recovers via retryLoad and schedules a single status refresh without duplicate timers', () => {
      jest.useFakeTimers();
      jest.setSystemTime(new Date('2026-07-28T20:59:58.000Z'));
      mockCatalogService.getCategories.mockReturnValue(of([]));
      mockCatalogService.getProducts.mockReturnValue(of([]));
      mockStoreService.getStoreByPath
        .mockReturnValueOnce(throwError(() => new Error('falha transitoria')))
        .mockReturnValueOnce(of({
          id: 's1', name: 'Loja', slug: 'loja', logoUrl: '', isOpenNow: true,
          nextStatusChangeAt: '2026-07-28T21:00:00.000Z',
        }))
        .mockReturnValue(of({ id: 's1', name: 'Loja', slug: 'loja', logoUrl: '', isOpenNow: false }));

      component.ngOnInit();
      expect(component.loadError()).toBe(true);

      component.retryLoad();
      expect(component.loadError()).toBe(false);
      expect(component.loading()).toBe(false);

      jest.advanceTimersByTime(3000);

      expect(mockStoreService.getStoreByPath).toHaveBeenCalledTimes(3);
      expect(component.store()?.isOpenNow).toBe(false);
    });

    it('retries only the catalog when the store already resolved', () => {
      mockStoreService.getStoreByPath.mockReturnValue(of(validStore));
      mockCatalogService.getCategories.mockReturnValue(of([]));
      mockCatalogService.getProducts
        .mockReturnValueOnce(throwError(() => new Error('falha transitoria')))
        .mockReturnValueOnce(of([
          { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel' },
        ]));

      component.ngOnInit();
      expect(component.catalogError()).toBe(true);

      component.retryCatalog();

      expect(component.catalogError()).toBe(false);
      expect(component.loading()).toBe(false);
      expect(mockStoreService.getStoreByPath).toHaveBeenCalledTimes(1);
      expect(mockCatalogService.getProducts).toHaveBeenCalledTimes(2);
    });
  });

  describe('storePath reactivation', () => {
    const firstStore = { id: 's1', name: 'Loja', slug: 'loja', logoUrl: '', isOpenNow: true };
    const secondStore = { id: 's2', name: 'Outra Loja', slug: 'outra-loja', logoUrl: '', isOpenNow: true };

    function emitPath(path: string): void {
      routeParamMap.next({ get: (key: string) => (key === 'storePath' ? path : null) });
    }

    it('reloads the store and catalog when the storePath changes', () => {
      mockStoreService.getStoreByPath.mockReturnValue(of(firstStore));
      mockCatalogService.getCategories.mockReturnValue(of([
        { id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1, isActive: true },
      ]));
      mockCatalogService.getProducts.mockReturnValue(of([
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel' },
      ]));

      component.ngOnInit();
      expect(component.store()?.slug).toBe('loja');
      expect(component.productSections().map((s) => s.name)).toEqual(['Entradas']);

      mockStoreService.getStoreByPath.mockReturnValue(of(secondStore));
      mockCatalogService.getCategories.mockReturnValue(of([
        { id: 'c2', storeId: 's2', name: 'Bebidas', displayOrder: 1, isActive: true },
      ]));
      mockCatalogService.getProducts.mockReturnValue(of([
        { id: 'p2', storeId: 's2', categoryId: 'c2', name: 'Suco' },
      ]));

      emitPath('outra-loja');

      expect(component.store()?.id).toBe('s2');
      expect(component.store()?.slug).toBe('outra-loja');
      expect(component.productSections().map((s) => s.name)).toEqual(['Bebidas']);
      expect(mockStoreService.getStoreByPath).toHaveBeenCalledTimes(2);
      expect(mockCatalogService.getProducts).toHaveBeenCalledTimes(2);
    });

    it('clears the previous catalog and store before resolving the new store path', () => {
      mockStoreService.getStoreByPath.mockReturnValue(of(firstStore));
      mockCatalogService.getCategories.mockReturnValue(of([
        { id: 'c1', storeId: 's1', name: 'Entradas', displayOrder: 1, isActive: true },
      ]));
      mockCatalogService.getProducts.mockReturnValue(of([
        { id: 'p1', storeId: 's1', categoryId: 'c1', name: 'Pastel' },
      ]));

      component.ngOnInit();
      expect(component.store()?.id).toBe('s1');
      expect(component.products().length).toBe(1);

      const pending = new Subject<any>();
      mockStoreService.getStoreByPath.mockReturnValue(pending);

      emitPath('outra-loja');

      expect(component.store()).toBeNull();
      expect(component.categories()).toEqual([]);
      expect(component.products()).toEqual([]);
    });

    it('discards an in-flight store resolution when the storePath changes', () => {
      const first = new Subject<any>();
      mockStoreService.getStoreByPath.mockReturnValue(first);
      component.ngOnInit();

      const second = new Subject<any>();
      mockStoreService.getStoreByPath.mockReturnValue(second);
      emitPath('outra-loja');

      first.next(firstStore);
      expect(component.store()).toBeNull();

      second.next(secondStore);
      expect(component.store()?.id).toBe('s2');
    });

    it('surfaces a load error when the new store path fails to resolve', () => {
      mockStoreService.getStoreByPath.mockReturnValue(of(firstStore));
      mockCatalogService.getCategories.mockReturnValue(of([]));
      mockCatalogService.getProducts.mockReturnValue(of([]));

      component.ngOnInit();
      expect(component.loadError()).toBe(false);

      mockStoreService.getStoreByPath.mockReturnValue(throwError(() => new Error('loja nao encontrada')));
      emitPath('outra-loja');

      expect(component.loadError()).toBe(true);
      expect(component.store()).toBeNull();
      expect(component.loading()).toBe(false);
    });
  });

  describe('stale status refresh after store switch', () => {
    const firstStore = {
      id: 's1',
      name: 'Loja',
      slug: 'loja',
      logoUrl: '',
      isOpenNow: true,
      nextStatusChangeAt: '2026-07-28T21:00:00.000Z',
    };
    const secondStore = { id: 's2', name: 'Outra Loja', slug: 'outra-loja', logoUrl: '', isOpenNow: true };

    it('discards a stale status refresh response after switching stores', () => {
      jest.useFakeTimers();
      jest.setSystemTime(new Date('2026-07-28T20:59:59.000Z'));
      mockCatalogService.getCategories.mockReturnValue(of([]));
      mockCatalogService.getProducts.mockReturnValue(of([]));

      const staleStatus = new Subject<any>();

      mockStoreService.getStoreByPath
        .mockReturnValueOnce(of(firstStore))
        .mockReturnValueOnce(staleStatus);

      component.ngOnInit();
      jest.advanceTimersByTime(2000);

      expect(mockStoreService.getStoreByPath).toHaveBeenCalledTimes(2);

      mockStoreService.getStoreByPath.mockReturnValueOnce(of(secondStore));
      routeParamMap.next({ get: (key: string) => (key === 'storePath' ? 'outra-loja' : null) });

      expect(component.store()?.id).toBe('s2');

      staleStatus.next({ ...firstStore, isOpenNow: false });

      expect(component.store()?.id).toBe('s2');
      expect(component.store()?.isOpenNow).toBe(true);
    });

    it('does not reschedule a status refresh from a stale error response', () => {
      jest.useFakeTimers();
      jest.setSystemTime(new Date('2026-07-28T20:59:59.000Z'));
      mockCatalogService.getCategories.mockReturnValue(of([]));
      mockCatalogService.getProducts.mockReturnValue(of([]));

      const staleStatus = new Subject<any>();

      mockStoreService.getStoreByPath
        .mockReturnValueOnce(of(firstStore))
        .mockReturnValueOnce(staleStatus);

      component.ngOnInit();
      jest.advanceTimersByTime(2000);

      mockStoreService.getStoreByPath.mockReturnValueOnce(of(secondStore));
      routeParamMap.next({ get: (key: string) => (key === 'storePath' ? 'outra-loja' : null) });

      const timerCountAfterSwitch = jest.getTimerCount();

      staleStatus.error(new Error('status unavailable'));

      expect(jest.getTimerCount()).toBe(timerCountAfterSwitch);
    });
  });
});
