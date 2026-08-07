import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { forkJoin, switchMap } from 'rxjs';

import { StoreService } from '../../core/services/store.service';
import { CatalogService } from '../../core/services/catalog.service';
import { CartService } from '../../core/services/cart.service';
import { StoreFilterStateService } from '../../core/services/store-filter-state.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthService } from '../../core/services/auth.service';

import { StorePublicDetails } from '../../shared/models/store.model';
import { Product, ProductCategory } from '../../shared/models/product.model';
import { CartItem } from '../../shared/models/cart-item.model';

import { ProductCardComponent } from '../../shared/components/product-card/product-card.component';
import { FloatingCartComponent } from '../../shared/components/floating-cart/floating-cart.component';
import { FooterNavComponent, FooterNavItem } from '../../shared/components/footer-nav/footer-nav.component';
import { StoreMetricsComponent } from '../../shared/components/store-metrics/store-metrics.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { CategoryTabsComponent, CategoryTab } from '../../shared/components/category-tabs/category-tabs.component';

@Component({
  selector: 'app-store-page',
  standalone: true,
  imports: [
    CommonModule, IonIcon,
    ProductCardComponent, FloatingCartComponent, FooterNavComponent,
    StoreMetricsComponent, EmptyStateComponent, CategoryTabsComponent,
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
  readonly loading = signal(true);
  readonly loadError = signal(false);

  readonly TODOS_ID = 'todos';
  private isScrollingToCategory = false;
  private observer: IntersectionObserver | null = null;
  private statusRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  private storePath = '';

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
      ...this.storeCategories().map((category) => ({ id: category.id, name: category.name })),
    ];
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

  readonly productSections = computed(() => {
    const result: { id: string; name: string; products: Product[] }[] = [];
    const products = this.orderedProducts();

    for (const cat of this.storeCategories()) {
      const catProducts = products.filter((p) => p.categoryId === cat.id);
      if (catProducts.length > 0) {
        result.push({ id: cat.id, name: cat.name, products: catProducts });
      }
    }

    return result;
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

  readonly footerItems = computed<FooterNavItem[]>(() => [
    { id: 'cardapio', icon: 'storefront-outline', label: 'Cardapio', active: true },
    { id: 'pedidos', icon: 'receipt-outline', label: 'Pedidos', disabled: true },
    { id: 'carrinho', icon: 'bag-check-outline', label: 'Carrinho' },
    { id: 'conta', icon: 'person-circle-outline', label: 'Conta', disabled: true },
  ]);

  private readonly cartQuantityMap = computed(() => {
    const map = new Map<string, number>();
    for (const item of this.cart.items()) {
      const prev = map.get(item.productId) ?? 0;
      map.set(item.productId, prev + item.quantity);
    }
    return map;
  });

  ngOnInit(): void {
    const storePath = this.route.snapshot.paramMap.get('storePath');
    if (!storePath) {
      this.loadError.set(true);
      this.loading.set(false);
      return;
    }
    this.storePath = storePath;

    if (!this.auth.customerProfile()) {
      this.auth.restoreCustomerSession().subscribe({ error: () => undefined });
    }

    this.storeService.getStoreByPath(storePath).pipe(
      switchMap((store) => {
        this.store.set(store);
        this.scheduleStatusRefresh(store);
        this.cart.setStore(store.id, store.name, store.logoUrl);
        return forkJoin({
          cats: this.catalogService.getCategories(store.id),
          prods: this.catalogService.getProducts(store.id),
        });
      }),
    ).subscribe({
      next: ({ cats, prods }) => {
        this.categories.set(cats);
        this.products.set(prods);
        const saved = this.filterState.restore();
        const tabIds = new Set(this.categoryTabs().map((tab) => tab.id));
        this.activeCategoryId.set(saved.activeCategoryId && tabIds.has(saved.activeCategoryId)
          ? saved.activeCategoryId
          : (this.categoryTabs()[0]?.id ?? this.TODOS_ID));
        this.loading.set(false);
        setTimeout(() => this.setupScrollObserver(), 300);
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  ngOnDestroy(): void {
    this.filterState.save({
      activeCategoryId: this.activeCategoryId(),
      searchTerm: '',
    });
    if (this.observer) {
      this.observer.disconnect();
      this.observer = null;
    }
    this.clearStatusRefreshTimer();
  }

  private setupScrollObserver(): void {
    if (this.observer) this.observer.disconnect();
    const options: IntersectionObserverInit = { rootMargin: '-100px 0px -70% 0px', threshold: 0 };
    this.observer = new IntersectionObserver((entries) => {
      if (this.isScrollingToCategory) return;
      for (const entry of entries) {
        if (entry.isIntersecting && entry.target.id) {
          const catId = entry.target.id.replace('cat-', '');
          this.activeCategoryId.set(catId === 'top' ? this.TODOS_ID : catId);
        }
      }
    }, options);
    const topSentinel = document.getElementById('cat-top');
    if (topSentinel) this.observer.observe(topSentinel);
    for (const sectionData of this.productSections()) {
      const section = document.getElementById(`cat-${sectionData.id}`);
      if (section) this.observer.observe(section);
    }
  }

  scrollToCategory(categoryId: string): void {
    this.activeCategoryId.set(categoryId);
    this.isScrollingToCategory = true;
    const targetId = categoryId === this.TODOS_ID ? 'cat-top' : `cat-${categoryId}`;
    const el = document.getElementById(targetId);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      setTimeout(() => { this.isScrollingToCategory = false; }, 800);
    } else {
      this.isScrollingToCategory = false;
    }
  }

  retryLoad(): void {
    this.loadError.set(false);
    this.loading.set(true);
    this.ngOnInit();
  }

  private scheduleStatusRefresh(store: StorePublicDetails): void {
    this.clearStatusRefreshTimer();
    if (!store.nextStatusChangeAt) return;

    const changeAt = new Date(store.nextStatusChangeAt).getTime();
    if (Number.isNaN(changeAt)) return;

    const delay = Math.min(Math.max(changeAt - Date.now() + 1000, 1000), 2_147_483_647);
    this.statusRefreshTimer = setTimeout(() => this.refreshStoreStatus(), delay);
  }

  private refreshStoreStatus(): void {
    if (!this.storePath) return;

    this.storeService.getStoreByPath(this.storePath).subscribe({
      next: (store) => {
        this.store.set(store);
        this.cart.setStore(store.id, store.name, store.logoUrl);
        this.scheduleStatusRefresh(store);
      },
      error: () => {
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
  }

  onFooterSelect(id: string): void {
    if (id === 'carrinho') this.openCart();
    if (id === 'cardapio') this.scrollToCategory(this.TODOS_ID);
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

  openCart(): void {
    this.filterState.save({ activeCategoryId: this.activeCategoryId(), searchTerm: '' });
    this.router.navigate(['/', this.store()!.slug, 'carrinho']);
  }

}
