import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { StoreService } from '../../core/services/store.service';
import { CatalogService } from '../../core/services/catalog.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { ProductCategory } from '../../shared/models/product.model';

interface ProductAdditionalItem {
  id?: string;
  name: string;
  price: number;
  isActive: boolean;
  displayOrder: number;
}

interface ProductChoiceOptionItem {
  id?: string;
  name: string;
  price: number;
  isActive: boolean;
  displayOrder: number;
}

interface ProductVariationItem {
  id?: string;
  name: string;
  price: number;
  promotionalPrice?: number;
  isActive: boolean;
  displayOrder: number;
}

interface ProductItem {
  id?: string;
  storeId?: string;
  categoryId: string;
  categoryName?: string;
  name: string;
  description: string;
  price: number;
  promotionalPrice?: number;
  imageUrl?: string;
  isAvailable: boolean;
  isFeatured: boolean;
  displayOrder: number;
  createdAtUtc?: string;
  additionals?: ProductAdditionalItem[];
  choiceOptions?: ProductChoiceOptionItem[];
  variations?: ProductVariationItem[];
}

interface CategoryItem {
  id?: string;
  storeId?: string;
  name: string;
  description?: string;
  displayOrder: number;
  isActive: boolean;
  isFeatured?: boolean;
  productCount?: number;
}

@Component({
  selector: 'app-products-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, BrlCurrencyPipe],
  templateUrl: './products-page.component.html',
  styleUrl: './products-page.component.scss',
})
export class ProductsPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  private readonly catalogService = inject(CatalogService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  // ─── Store ────────────────────────────────────────────────
  readonly storeId = signal<string | null>(null);

  // ─── Categories ───────────────────────────────────────────
  readonly categories = signal<CategoryItem[]>([]);
  readonly showCategoryModal = signal(false);
  readonly editingCategory = signal<CategoryItem | null>(null);
  readonly categoryForm = signal<CategoryItem>(this.emptyCategory());

  // ─── Products ─────────────────────────────────────────────
  readonly products = signal<ProductItem[]>([]);
  readonly selectedProduct = signal<ProductItem | null>(null);
  readonly productForm = signal<ProductItem>(this.emptyProduct());
  readonly editingProduct = signal(false);
  readonly searchQuery = signal('');
  readonly saving = signal(false);
  readonly saveStatus = signal<'saved' | 'saving' | 'error' | null>(null);

  readonly filteredProducts = computed(() => {
    const q = this.searchQuery().toLowerCase();
    if (!q) return this.products();
    return this.products().filter((p) =>
      p.name.toLowerCase().includes(q) || (p.categoryName ?? '').toLowerCase().includes(q),
    );
  });

  ngOnInit(): void {
    this.storeService.getMyStore().subscribe({
      next: (store) => {
        this.storeId.set(store.id);
        this.loadCategories();
        this.loadProducts();
      },
      error: () => this.router.navigate(['/cadastro']),
    });
  }

  // ─── Categories ───────────────────────────────────────────
  private emptyCategory(): CategoryItem {
    return { name: '', displayOrder: 0, isActive: true };
  }

  loadCategories(): void {
    const sid = this.storeId();
    if (!sid) return;
    this.api.get<ProductCategory[]>(`/api/stores/${sid}/categories`).subscribe({
      next: (cats) => this.categories.set(cats.map((c: any) => ({
        id: c.id, name: c.name, displayOrder: c.displayOrder, isActive: c.isActive, productCount: 0,
      }))),
    });
  }

  openNewCategory(): void {
    this.editingCategory.set(null);
    this.categoryForm.set(this.emptyCategory());
    this.showCategoryModal.set(true);
  }

  openEditCategory(cat: CategoryItem): void {
    this.editingCategory.set(cat);
    this.categoryForm.set({ ...cat });
    this.showCategoryModal.set(true);
  }

  closeCategoryModal(): void {
    this.showCategoryModal.set(false);
  }

  saveCategory(): void {
    const sid = this.storeId();
    if (!sid) return;
    const form = this.categoryForm();
    const body = { name: form.name.trim(), displayOrder: form.displayOrder, isActive: form.isActive, isFeatured: false };

    const req$ = this.editingCategory()?.id
      ? this.api.put(`/api/stores/${sid}/categories/${this.editingCategory()!.id}`, body)
      : this.api.post(`/api/stores/${sid}/categories`, body);

    req$.subscribe({
      next: () => { this.showCategoryModal.set(false); this.loadCategories(); },
    });
  }

  deleteCategory(cat: CategoryItem): void {
    const sid = this.storeId();
    if (!sid || !cat.id) return;
    this.api.delete(`/api/stores/${sid}/categories/${cat.id}`).subscribe({
      next: () => this.loadCategories(),
    });
  }

  // ─── Products ─────────────────────────────────────────────
  private emptyProduct(): ProductItem {
    return { categoryId: '', name: '', description: '', price: 0, isAvailable: true, isFeatured: false, displayOrder: 0 };
  }

  loadProducts(): void {
    const sid = this.storeId();
    if (!sid) return;
    this.api.get<any[]>(`/api/stores/${sid}/products`).subscribe({
      next: (prods) => this.products.set(prods.map((p: any) => ({
        id: p.id, storeId: p.storeId, categoryId: p.categoryId, categoryName: p.categoryName,
        name: p.name, description: p.description, price: p.price,       promotionalPrice: p.promotionalPrice,
        imageUrl: p.imageUrl, isAvailable: p.isAvailable, isFeatured: p.isFeatured,
        displayOrder: p.displayOrder, createdAtUtc: p.createdAtUtc,
        additionals: p.additionals || [],
        choiceOptions: p.choiceOptions || [],
        variations: p.variations || [],
      }))),
    });
  }

  selectProduct(product: ProductItem): void {
    this.selectedProduct.set(product);
    this.productForm.set({ ...product });
    this.editingProduct.set(true);
  }

  newProduct(): void {
    this.selectedProduct.set(null);
    this.productForm.set(this.emptyProduct());
    this.editingProduct.set(false);
  }

  // ─── Additionals ──────────────────────────────────────────
  readonly showAdditionalModal = signal(false);
  readonly additionalForm = signal<ProductAdditionalItem>({ name: '', price: 0, isActive: true, displayOrder: 0 });

  openNewAdditionalModal(): void {
    this.additionalForm.set({ name: '', price: 0, isActive: true, displayOrder: 0 });
    this.showAdditionalModal.set(true);
  }

  closeAdditionalModal(): void {
    this.showAdditionalModal.set(false);
  }

  onPriceInputAdditional(value: string): void {
    const digits = value.replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) return;
    this.additionalForm.update((f) => ({ ...f, price: num }));
  }

  saveAdditional(): void {
    const form = this.additionalForm();
    if (!form.name.trim()) return;
    const prod = this.productForm();
    const additions = prod.additionals || [];

    if (additions.some((a) => a.name.toLowerCase() === form.name.trim().toLowerCase())) {
      this.toast.showWarning('Este adicional já foi incluído.');
      return;
    }

    additions.push({ ...form, name: form.name.trim() });
    this.productForm.update((p) => ({ ...p, additionals: additions }));
    this.closeAdditionalModal();
  }

  removeAdditional(idx: number): void {
    const prod = this.productForm();
    const additions = [...(prod.additionals || [])];
    additions.splice(idx, 1);
    this.productForm.update((p) => ({ ...p, additionals: additions }));
  }

  // ─── Choice Options ───────────────────────────────────────
  readonly showChoiceOptionModal = signal(false);
  readonly choiceOptionForm = signal<ProductChoiceOptionItem>({ name: '', price: 0, isActive: true, displayOrder: 0 });

  openNewChoiceOptionModal(): void {
    this.choiceOptionForm.set({ name: '', price: 0, isActive: true, displayOrder: 0 });
    this.showChoiceOptionModal.set(true);
  }

  closeChoiceOptionModal(): void {
    this.showChoiceOptionModal.set(false);
  }

  onPriceInputChoiceOption(value: string): void {
    const digits = value.replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) return;
    this.choiceOptionForm.update((f) => ({ ...f, price: num }));
  }

  saveChoiceOption(): void {
    const form = this.choiceOptionForm();
    if (!form.name.trim()) return;
    const prod = this.productForm();
    const opts = prod.choiceOptions || [];

    if (opts.some((o) => o.name.toLowerCase() === form.name.trim().toLowerCase())) {
      this.toast.showWarning('Esta opção já foi incluída.');
      return;
    }

    opts.push({ ...form, name: form.name.trim() });
    this.productForm.update((p) => ({ ...p, choiceOptions: opts }));
    this.closeChoiceOptionModal();
  }

  removeChoiceOption(idx: number): void {
    const prod = this.productForm();
    const opts = [...(prod.choiceOptions || [])];
    opts.splice(idx, 1);
    this.productForm.update((p) => ({ ...p, choiceOptions: opts }));
  }

  onPriceInput(value: string, field: 'price' | 'promotionalPrice'): void {
    const digits = value.replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) return;
    this.productForm.update((p) => ({ ...p, [field]: num }));
  }

  formatPrice(value: number | undefined): string {
    if (value == null || value === 0) return '';
    return value.toFixed(2).replace('.', ',');
  }

  onImageSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const sid = this.storeId();
    const pid = this.selectedProduct()?.id;
    if (!sid || !pid) return;

    const formData = new FormData();
    formData.append('file', file);
    this.api.post<any>(`/api/stores/${sid}/products/${pid}/images`, formData).subscribe({
      next: (res) => {
        this.productForm.update((p) => ({ ...p, imageUrl: res.imageUrl }));
        this.loadProducts();
      },
    });
  }

  saveProduct(): void {
    const sid = this.storeId();
    if (!sid) return;
    this.saving.set(true);
    this.saveStatus.set('saving');

    const form = this.productForm();
    const body: any = {
      categoryId: form.categoryId,
      name: form.name.trim(),
      description: form.description.trim(),
      price: form.price,
      promotionalPrice: form.promotionalPrice ?? null,
      imageUrl: form.imageUrl ?? null,
      isFeatured: form.isFeatured,
      displayOrder: form.displayOrder,
      additionals: form.additionals || [],
      choiceOptions: form.choiceOptions || [],
      variations: form.variations || [],
    };

    const req$ = this.editingProduct() && this.selectedProduct()?.id
      ? this.api.put(`/api/stores/${sid}/products/${this.selectedProduct()!.id}`, { ...body, isAvailable: form.isAvailable })
      : this.api.post(`/api/stores/${sid}/products`, body);

    req$.subscribe({
      next: (res: any) => {
        this.saving.set(false);
        this.saveStatus.set('saved');
        this.selectedProduct.set(res);
        this.editingProduct.set(true);
        this.productForm.set(res);
        this.loadProducts();
        this.toast.showSuccess('Produto salvo com sucesso!');
      },
      error: () => { 
        this.saving.set(false); 
        this.saveStatus.set('error'); 
        this.toast.showError('Não foi possível salvar o produto.');
      },
    });
  }

  onBack(): void { this.router.navigate(['/configurar-loja']); }
}
