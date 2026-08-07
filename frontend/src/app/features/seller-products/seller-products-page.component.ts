import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IonIcon,
  IonReorder,
  IonReorderGroup,
  IonSpinner,
} from '@ionic/angular/standalone';
import { CardapioMenuTabsComponent } from '../../shared/components/cardapio-menu-tabs/cardapio-menu-tabs.component';
import {
  SubscriptionBannerComponent,
} from '../../shared/components/subscription-banner/subscription-banner.component';
import { StoreProductsPageComponent } from '../store-config/products/store-products-page.component';

@Component({
  selector: 'app-seller-products-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    IonIcon,
    IonSpinner,
    IonReorderGroup,
    IonReorder,
    CardapioMenuTabsComponent,
    SubscriptionBannerComponent,
  ],
  templateUrl: './seller-products-page.component.html',
  styleUrls: [
    '../store-config/products/store-products-page.component.scss',
    './seller-products-page.component.scss',
  ],
})
export class SellerProductsPageComponent extends StoreProductsPageComponent {
  readonly editorOpen = signal(false);
  readonly statusFilter = signal<'all' | 'active' | 'inactive'>('all');
  readonly headerSummary = computed(() => `${this.filteredProducts().length} ${this.filteredProducts().length === 1 ? 'item na lista' : 'itens na lista'}`);
  readonly pageNumbers = computed(() => Array.from({ length: this.totalPages() }, (_, index) => index));

  activeProductsCount(): number {
    return this.products().filter((product) => product.isAvailable).length;
  }

  inactiveProductsCount(): number {
    return this.products().filter((product) => !product.isAvailable).length;
  }

  override newProduct(): void {
    super.newProduct();
    this.editorOpen.set(true);
  }

  override selectProduct(product: import('../../shared/models/product.model').Product): void {
    super.selectProduct(product);
    this.editorOpen.set(true);
  }

  closeEditor(): void {
    const selectedId = this.selectedId();
    const savedProduct = selectedId
      ? this.products().find((product) => product.id === selectedId)
      : undefined;

    if (savedProduct) {
      super.selectProduct(savedProduct);
    } else {
      super.newProduct();
    }

    this.formDirty.set(false);
    this.editorOpen.set(false);
  }

  protected override onProductSaved(): void {
    this.formDirty.set(false);
    this.editorOpen.set(false);
  }

  clearFilters(): void {
    this.searchQuery.set('');
    this.categoryFilterId.set('');
    this.statusFilter.set('all');
    this.currentPage.set(0);
  }

  goToPage(page: number): void {
    this.currentPage.set(page);
  }

  override readonly filteredProducts = computed(() => {
    const q = this.searchQuery().toLowerCase();
    const filter = this.categoryFilterId();
    const status = this.statusFilter();

    return this.products().filter((p) => {
      const matchesSearch = p.name.toLowerCase().includes(q) || (p.categoryName || '').toLowerCase().includes(q);
      const matchesCategory = !filter || p.categoryId === filter;
      const matchesStatus = status === 'all'
        || (status === 'active' && p.isAvailable)
        || (status === 'inactive' && !p.isAvailable);

      return matchesSearch && matchesCategory && matchesStatus;
    });
  });
}
