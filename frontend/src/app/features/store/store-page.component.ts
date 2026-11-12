import { Component, OnInit, OnDestroy, QueryList, ViewChild, ViewChildren, ElementRef, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { distinctUntilChanged, forkJoin, map, Subscription } from 'rxjs';

import { StoreService } from '../../core/services/store.service';
import { CatalogService } from '../../core/services/catalog.service';
import { CartService } from '../../core/services/cart.service';
import { StoreFilterStateService } from '../../core/services/store-filter-state.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthService } from '../../core/services/auth.service';

import { StorePublicDetails } from '../../shared/models/store.model';
import { Product, ProductCategory, ProductSpecialClassification } from '../../shared/models/product.model';
import { CartItem } from '../../shared/models/cart-item.model';
import { resolveProductSpecialClassification } from '../../shared/utils/product-special-classification.utils';

import { ProductCardComponent } from '../../shared/components/product-card/product-card.component';
import { StoreMetricsComponent } from '../../shared/components/store-metrics/store-metrics.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { CategoryTabsComponent, CategoryTab } from '../../shared/components/category-tabs/category-tabs.component';
import { FoodDeliverySpinnerComponent } from '../../shared/components/food-delivery-spinner/food-delivery-spinner.component';

interface StoreProductSection {
  id: string;
  name: string;
  products: Product[];
}

const SPECIAL_SECTION_CONFIG: ReadonlyArray<{ value: ProductSpecialClassification; name: string }> = [
  { value: 'Offers', name: 'Ofertas' },
  { value: 'BestSeller', name: 'Mais vendidos' },
  { value: 'New', name: 'Novidades' },
];

@Component({
  selector: 'app-store-page',
  standalone: true,
  imports: [
    CommonModule, IonIcon,
    ProductCardComponent,
    StoreMetricsComponent, EmptyStateComponent, CategoryTabsComponent,
    FoodDeliverySpinnerComponent,
  ],
  templateUrl: './store-page.component.html',
  styleUrl: './store-page.component.scss',
})
export class StorePageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly storeService = inject(StoreService);
  private readonly catalogService = inject(CatalogService);
  readonly cart = inject(CartService);
  private readonly filterState = inject(StoreFilterStateService);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  readonly store = signal<StorePublicDetails | null>(null);
  readonly categories = signal<ProductCategory[]>([]);
  readonly products = signal<Product[]>([]);
  readonly activeCategoryId = signal<string | null>(null);
  /**
   * Seção destacada pela rolagem. Separada de `activeCategoryId` porque no modo `Todos`
   * o scrollspy só deve mover a pílula, sem filtrar o feed.
   */
  private readonly scrollSpySectionId = signal<string | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly catalogError = signal(false);

  @ViewChild('scrollContent') private scrollContent?: ElementRef<HTMLElement>;
  @ViewChildren('categorySection', { read: ElementRef }) private categorySections!: QueryList<ElementRef<HTMLElement>>;

  readonly TODOS_ID = 'todos';
  private readonly categoryScrollGap = 8;
  onStoreScroll(): void {
    this.updateActiveCategoryFromScroll();
  }
  private statusRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  private statusRefreshSubscription?: Subscription;
  private statusGeneration = 0;
  private storePath = '';
  private routeSubscription?: Subscription;
  private loadSubscription?: Subscription;
  private catalogSubscription?: Subscription;

  private readonly storeCategories = computed(() => {
    const storeId = this.store()?.id;
    const validCategoryIds = new Set(this.validProducts().map((product) => product.categoryId));
    return this.categories().filter((category) =>
      (!storeId || category.storeId === storeId) && validCategoryIds.has(category.id),
    );
  });

  readonly categoryTabs = computed<CategoryTab[]>(() => {
    return [
      { id: this.TODOS_ID, name: 'Todos' },
      ...this.specialSections().map((section) => ({ id: section.id, name: section.name })),
      ...this.storeCategories().map((category) => ({ id: category.id, name: category.name })),
    ];
  });

  /**
   * Pílula visual: uma seleção explícita sempre manda; em `Todos` o scrollspy
   * acompanha a seção visível sem alterar o filtro do feed.
   */
  readonly highlightedCategoryId = computed(() => {
    const selectedId = this.activeCategoryId();
    if (selectedId && selectedId !== this.TODOS_ID) return selectedId;
    return this.scrollSpySectionId() ?? this.TODOS_ID;
  });

  private readonly orderedProducts = computed(() => {
    return [...this.validProducts()].sort((a, b) => {
      const byOrder = (a.displayOrder ?? 0) - (b.displayOrder ?? 0);
      if (byOrder !== 0) return byOrder;
      return a.name.localeCompare(b.name, 'pt-BR');
    });
  });

  private readonly validProducts = computed(() => {
    const storeId = this.store()?.id;
    const categoryIds = new Set(
      this.categories()
        .filter((category) => (!storeId || category.storeId === storeId) && category.isActive !== false)
        .map((category) => category.id),
    );

    return this.products().filter((product) =>
      (!storeId || product.storeId === storeId) && categoryIds.has(product.categoryId),
    );
  });

  readonly categoryProductSections = computed<StoreProductSection[]>(() => {
    const result: StoreProductSection[] = [];
    const products = this.orderedProducts();

    for (const cat of this.storeCategories()) {
      const catProducts = products.filter((p) => p.categoryId === cat.id);
      if (catProducts.length > 0) {
        result.push({ id: cat.id, name: cat.name, products: catProducts });
      }
    }

    return result;
  });

  /**
   * Vitrines especiais derivadas da mesma lista de produtos já carregada. Cada seção
   * mantém seu próprio array; o mesmo produto pode aparecer na vitrine e na categoria.
   */
  readonly specialSections = computed<StoreProductSection[]>(() => {
    const products = this.orderedProducts();
    return SPECIAL_SECTION_CONFIG
      .map((config) => ({
        id: `special:${config.value}`,
        name: config.name,
        products: products.filter((product) => resolveProductSpecialClassification(product) === config.value),
      }))
      .filter((section) => section.products.length > 0);
  });

  /** `Todos` mostra as vitrines especiais disponíveis antes das categorias do lojista. */
  readonly allSections = computed<StoreProductSection[]>(() => [
    ...this.specialSections(),
    ...this.categoryProductSections(),
  ]);

  readonly productSections = computed<StoreProductSection[]>(() => {
    const activeId = this.activeCategoryId();
    if (!activeId || activeId === this.TODOS_ID) return this.allSections();
    if (activeId.startsWith('special:')) {
      return this.specialSections().filter((section) => section.id === activeId);
    }
    return this.categoryProductSections().filter((section) => section.id === activeId);
  });

  readonly statusText = computed(() => {
    const s = this.store();
    if (!s) return '';
    if (s.isOpenNow) return 'Aberta';
    if (!s.isOpen) return 'Fechado';
    return 'Fechado no momento';
  });

  readonly etaText = computed(() => {
    const s = this.store();
    if (!s?.isOpenNow) return '';
    const min = s.initialMinute;
    const max = s.finalMinute;
    if (min != null && max != null) return `${min}-${max} min`;
    if (min != null) return `a partir de ${min} min`;
    return '';
  });

  readonly minOrderText = computed(() => {
    const s = this.store();
    if (!s || s.minimumOrderValue <= 0) return 'Sem minimo';
    return `R$ ${s.minimumOrderValue.toFixed(2).replace('.', ',')}`;
  });

  readonly customerFirstName = computed(() => {
    const fullName = this.auth.customerProfile()?.fullName?.trim();
    return fullName ? fullName.split(/\s+/)[0] : '';
  });

  private readonly cartQuantityMap = computed(() => {
    const map = new Map<string, number>();
    for (const item of this.cart.items()) {
      const prev = map.get(item.productId) ?? 0;
      map.set(item.productId, prev + item.quantity);
    }
    return map;
  });

  ngOnInit(): void {
    if (!this.auth.customerProfile()) {
      this.auth.restoreCustomerSession().subscribe({ error: () => undefined });
    }

    this.routeSubscription = this.route.paramMap
      .pipe(
        map((params) => params.get('storePath')),
        distinctUntilChanged(),
      )
      .subscribe((storePath) => {
        this.storePath = storePath ?? '';
        this.loadStore();
      });
  }

  private loadStore(): void {
    this.loadSubscription?.unsubscribe();
    this.loadSubscription = undefined;
    this.catalogSubscription?.unsubscribe();
    this.catalogSubscription = undefined;
    this.clearStatusRefreshTimer();
    this.statusRefreshSubscription?.unsubscribe();
    this.statusRefreshSubscription = undefined;
    this.statusGeneration++;

    this.store.set(null);
    this.categories.set([]);
    this.products.set([]);
    this.activeCategoryId.set(this.TODOS_ID);
    this.scrollSpySectionId.set(null);
    this.loadError.set(false);
    this.catalogError.set(false);

    if (!this.storePath) {
      this.loadError.set(true);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.loadSubscription = this.storeService.getStoreByPath(this.storePath).subscribe({
      next: (store) => {
        this.store.set(store);
        this.scheduleStatusRefresh(store);
        this.cart.setStore(store.id, store.name, store.logoUrl);
        this.loadCatalog(store.id);
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  private loadCatalog(storeId: string): void {
    this.catalogSubscription?.unsubscribe();
    this.catalogError.set(false);

    this.catalogSubscription = forkJoin({
      cats: this.catalogService.getCategories(storeId),
      prods: this.catalogService.getProducts(storeId),
    }).subscribe({
      next: ({ cats, prods }) => {
        this.categories.set(cats);
        this.products.set(prods);
        this.restoreActiveCategory();
        this.loading.set(false);
      },
      error: () => {
        this.catalogError.set(true);
        this.loading.set(false);
      },
    });
  }

  private restoreActiveCategory(): void {
    const saved = this.filterState.restore();
    const tabIds = new Set(this.categoryTabs().map((tab) => tab.id));
    this.activeCategoryId.set(saved.activeCategoryId && tabIds.has(saved.activeCategoryId)
      ? saved.activeCategoryId
      : (this.categoryTabs()[0]?.id ?? this.TODOS_ID));
    this.scrollSpySectionId.set(null);
  }

  ngOnDestroy(): void {
    this.filterState.save({
      activeCategoryId: this.activeCategoryId(),
      searchTerm: '',
    });
    this.clearStatusRefreshTimer();
    this.statusRefreshSubscription?.unsubscribe();
    this.routeSubscription?.unsubscribe();
    this.loadSubscription?.unsubscribe();
    this.catalogSubscription?.unsubscribe();
  }

  private updateActiveCategoryFromScroll(): void {
    // When a pill filters the feed down to a single section, scrolling must not
    // override the explicit selection. The scrollspy only drives Todos.
    const selectedId = this.activeCategoryId();
    if (selectedId && selectedId !== this.TODOS_ID) return;

    const scrollContainer = this.getScrollContainer();
    const containerTop = scrollContainer.getBoundingClientRect().top;
    const activationLine = containerTop + this.getCategoryScrollOffset();
    let activeId = this.TODOS_ID;

    // Todos keeps every shelf and category in the feed, so the spy walks all of them
    // and only moves the pill highlight.
    for (const sectionData of this.allSections()) {
      const section = document.getElementById(`cat-${sectionData.id}`);
      const title = section?.querySelector<HTMLElement>('h2') ?? section;
      if (!title || title.getBoundingClientRect().top > activationLine) break;
      activeId = sectionData.id;
    }

    this.scrollSpySectionId.set(activeId);
  }

  scrollToCategory(categoryId: string): void {
    this.activeCategoryId.set(categoryId);
    // Returning to Todos resets the highlight to the top; the next scroll re-runs the spy.
    if (categoryId === this.TODOS_ID) this.scrollSpySectionId.set(this.TODOS_ID);
    const el = categoryId === this.TODOS_ID
      ? document.getElementById('cat-top')
      : this.findCategorySection(categoryId);
    if (!el) return;

    requestAnimationFrame(() => {
      const scrollContainer = this.getScrollContainer();
      const containerRect = scrollContainer.getBoundingClientRect();
      const top = Math.max(
        0,
        el.getBoundingClientRect().top - containerRect.top + scrollContainer.scrollTop - this.getCategoryScrollOffset(),
      );
      scrollContainer.scrollTop = top;
      scrollContainer.scrollTo({ top, behavior: 'smooth' });
    });
  }

  private getCategoryScrollOffset(): number {
    const categoryBar = document.getElementById('store-categories');
    return (categoryBar?.getBoundingClientRect().height ?? 0) + this.categoryScrollGap;
  }

  private getScrollContainer(): HTMLElement {
    return this.scrollContent?.nativeElement ?? document.getElementById('store-content') ?? document.documentElement;
  }

  private findCategorySection(categoryId: string): HTMLElement | null {
    const directSection = this.categorySections?.toArray()
      .find((section) => section.nativeElement.id === `cat-${categoryId}`)?.nativeElement;
    return directSection ?? document.getElementById(`cat-${categoryId}`);
  }

  retryLoad(): void {
    this.clearStatusRefreshTimer();
    this.loadStore();
  }

  retryCatalog(): void {
    const storeId = this.store()?.id;
    if (!storeId) {
      this.retryLoad();
      return;
    }
    this.loading.set(true);
    this.loadCatalog(storeId);
  }

  private scheduleStatusRefresh(store: StorePublicDetails): void {
    this.clearStatusRefreshTimer();
    if (!store.nextStatusChangeAt) return;

    const changeAt = new Date(store.nextStatusChangeAt).getTime();
    if (Number.isNaN(changeAt)) return;

    const delay = Math.min(Math.max(changeAt - Date.now() + 1000, 1000), 30_000);
    this.statusRefreshTimer = setTimeout(() => this.refreshStoreStatus(), delay);
  }

  private refreshStoreStatus(): void {
    if (!this.storePath) return;

    const generation = this.statusGeneration;
    const storePath = this.storePath;

    this.statusRefreshSubscription?.unsubscribe();
    this.statusRefreshSubscription = this.storeService.getStoreByPath(storePath).subscribe({
      next: (store) => {
        if (generation !== this.statusGeneration || storePath !== this.storePath) return;
        this.store.set(store);
        this.cart.setStore(store.id, store.name, store.logoUrl);
        this.scheduleStatusRefresh(store);
      },
      error: () => {
        if (generation !== this.statusGeneration || storePath !== this.storePath) return;
        this.statusRefreshTimer = setTimeout(() => this.refreshStoreStatus(), 30_000);
      },
    });
  }

  private clearStatusRefreshTimer(): void {
    if (this.statusRefreshTimer) {
      clearTimeout(this.statusRefreshTimer);
      this.statusRefreshTimer = null;
    }
  }

  cartQuantityFor(productId: string): number {
    return this.cartQuantityMap().get(productId) ?? 0;
  }

  onBannerImageError(event: Event): void {
    const image = event.target as HTMLImageElement;
    if (image) {
      image.style.display = 'none';
    }
  }

  onAddToCard(product: Product): void {
    const item: CartItem = {
      productId: product.id,
      productName: product.name,
      productImage: product.imageUrl,
      productDescription: product.description,
      quantity: 1,
      unitPrice: product.price,
    };
    this.cart.addItem(item);
  }

  onRemoveFromCard(product: Product): void {
    const cartItem = this.cart.items().find((i) => i.productId === product.id);
    if (cartItem?.id) {
      this.cart.updateQuantity(cartItem.id, (cartItem.quantity || 1) - 1);
    }
  }

  clearFilters(): void {
    this.activeCategoryId.set(this.TODOS_ID);
    this.scrollSpySectionId.set(null);
  }

  goToProduct(product: Product): void {
    const store = this.store();
    if (!store?.isOpenNow) {
      this.toast.showWarning(store?.closedMessage || 'A loja está fechada no momento.');
      return;
    }

    this.filterState.save({ activeCategoryId: this.activeCategoryId(), searchTerm: '' });
    this.router.navigate(['/', store.slug, 'produto', product.id], { state: { product } });
  }

}
