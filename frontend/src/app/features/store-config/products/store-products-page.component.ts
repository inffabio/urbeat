import { Component, signal, computed, effect, inject, OnInit, ViewChild, ElementRef, Injector } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { addIcons } from 'ionicons';
import { checkmark, close, reorderTwoOutline, trashOutline, arrowBackOutline, arrowForwardOutline, searchOutline, chevronDownOutline, chevronUpOutline, informationCircleOutline, pencilOutline, cloudUploadOutline } from 'ionicons/icons';
import {
  IonIcon, IonSpinner,
  IonReorderGroup, IonReorder
} from '@ionic/angular/standalone';
import { WizardFooterComponent } from '../../../shared/components/wizard-footer/wizard-footer.component';
import { WizardHeaderComponent } from '../../../shared/components/wizard-header/wizard-header.component';
import { CardapioMenuTabsComponent } from '../../../shared/components/cardapio-menu-tabs/cardapio-menu-tabs.component';
import { SubscriptionBannerComponent, SubscriptionBannerStatus } from '../../../shared/components/subscription-banner/subscription-banner.component';
import { createStepperSteps } from '../../../shared/config/wizard-steps.config';
import { StoreService } from '../../../core/services/store.service';
import { SubscriptionService } from '../../../core/services/subscription.service';
import { ToastService } from '../../../core/services/toast.service';
import { Product, ProductCategory, ProductOptionGroup, ProductOptionItem, ProductVariation, ProductSaleMode, ProductWeightConfig } from '../../../shared/models/product.model';

addIcons({
  'checkmark': checkmark,
  'close': close,
  'reorder-two-outline': reorderTwoOutline,
  'trash-outline': trashOutline,
  'arrow-back-outline': arrowBackOutline,
  'arrow-forward-outline': arrowForwardOutline,
  'search-outline': searchOutline,
  'chevron-down-outline': chevronDownOutline,
  'chevron-up-outline': chevronUpOutline,
  'information-circle-outline': informationCircleOutline,
  'pencil-outline': pencilOutline,
  'cloud-upload-outline': cloudUploadOutline,
});

export interface SizeVariation {
  uid: string;
  name: string;
  description: string;
  price: string;
  isDefault: boolean;
  isActive: boolean;
}

export interface FixedWeightVariation {
  uid: string;
  weight: string;
  unit: 'g' | 'kg';
  price: string;
  isDefault: boolean;
  isActive: boolean;
}

export interface WeightConfigForm {
  pricePerKg: string;
  minGrams: string;
  maxGrams: string;
  incrementGrams: string;
  isEstimated: boolean;
}

@Component({
  selector: 'app-store-products-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonIcon, IonSpinner, IonReorderGroup, IonReorder, WizardHeaderComponent, WizardFooterComponent, CardapioMenuTabsComponent, SubscriptionBannerComponent],
  templateUrl: './store-products-page.component.html',
  styleUrl: './store-products-page.component.scss',
  host: { '[class.urbeat-onboarding]': '!isDashboardView()' },
})
export class StoreProductsPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly subscriptionStatus = signal<SubscriptionBannerStatus>('ok');
  readonly subscriptionDueDate = signal('');
  private readonly injector = inject(Injector);
  readonly stepperSteps = createStepperSteps(3);
  readonly isDashboardView = computed(() => (this.router.url ?? '').startsWith('/app/'));

  @ViewChild('fileInput') fileInput!: ElementRef<HTMLInputElement>;

  readonly storeId = signal<string | null>(null);
  readonly loading = signal(true);
  readonly isSaving = signal(false);
  readonly saveStatus = signal<'idle' | 'saving' | 'saved' | 'error'>('idle');
  readonly formDirty = signal(false);

  markDirty(): void {
    this.formDirty.set(true);
  }

  hasUnsavedChanges(): boolean {
    return this.formDirty() && !this.isSaving();
  }

  readonly products = signal<Product[]>([]);
  readonly categories = signal<ProductCategory[]>([]);

  readonly selectedId = signal<string | null>(null);
  readonly productName = signal('');
  readonly productDesc = signal('');
  readonly productPrice = signal('0,00');
  readonly productCatId = signal('');
  readonly productImage = signal('');
  readonly productImagePreview = signal<string | null>(null);
  readonly uploadingImage = signal(false);
  readonly isProductActive = signal(true);

  readonly tagDestaque = signal(false);
  readonly tagMaisVendido = signal(false);
  readonly tagNovidade = signal(false);
  readonly tagPriority = signal<string[]>([]);

  readonly saleMode = signal<ProductSaleMode>('single');
  readonly sizeVariations = signal<SizeVariation[]>([]);
  readonly fixedWeightVariations = signal<FixedWeightVariation[]>([]);
  readonly weightConfig = signal<WeightConfigForm>({ pricePerKg: '', minGrams: '', maxGrams: '', incrementGrams: '', isEstimated: false });

  readonly optionGroups = signal<ProductOptionGroup[]>([]);
  private groupCounter = 0;

  readonly searchQuery = signal('');
  readonly categoryFilterId = signal('');
  readonly expandedId = signal<string | null>(null);
  readonly currentPage = signal(0);
  readonly pageSize = 5;

  readonly expandedGroupId = signal<string | null>(null);

  readonly editingProduct = computed(() => this.selectedId() !== null);

  // ── Category management ──────────────────────────────────

  readonly newCategoryName = signal('');
  readonly isAddingCategory = signal(false);

  readonly editingCategoryId = signal<string | null>(null);
  readonly editingCategoryName = signal('');
  readonly isEditingCategory = signal(false);

  readonly deletingCategoryId = signal<string | null>(null);
  readonly deletingCategoryName = signal('');
  readonly deletingCategoryProductCount = signal(0);
  readonly reassignCategoryId = signal('');
  readonly isDeletingCategory = signal(false);

  readonly showEditCategoryModal = computed(() => this.editingCategoryId() !== null);
  readonly showDeleteCategoryModal = computed(() => this.deletingCategoryId() !== null);

  readonly isReorderingCategories = signal(false);

  readonly selectedTags = computed(() => {
    const tags: string[] = [];
    if (this.tagDestaque()) tags.push('destaque');
    if (this.tagMaisVendido()) tags.push('mais_vendido');
    if (this.tagNovidade()) tags.push('novidade');
    return tags;
  });

  readonly showTagPriority = computed(() => this.selectedTags().length >= 2);

  readonly orderedPriority = computed(() => {
    const current = this.tagPriority();
    const selected = this.selectedTags();
    return [...current.filter(t => selected.includes(t)), ...selected.filter(t => !current.includes(t))];
  });

  readonly tagLabels: Record<string, { name: string }> = {
    destaque: { name: 'Destaque' },
    mais_vendido: { name: 'Mais vendido' },
    novidade: { name: 'Novidade' },
  };

  readonly categoryProductCounts = computed(() => {
    const counts: Record<string, number> = {};
    for (const p of this.products()) {
      counts[p.categoryId] = (counts[p.categoryId] || 0) + 1;
    }
    return counts;
  });

  readonly sortedCategories = computed(() => {
    return [...this.categories()].sort((a, b) => a.displayOrder - b.displayOrder);
  });

  readonly activeCategories = computed(() => {
    return this.sortedCategories().filter(c => c.isActive);
  });

  readonly filteredProducts = computed(() => {
    const q = this.searchQuery().toLowerCase();
    const filter = this.categoryFilterId();
    return this.products().filter(p => {
      const matchesSearch = p.name.toLowerCase().includes(q) || (p.categoryName || '').toLowerCase().includes(q);
      return matchesSearch && (!filter || p.categoryId === filter);
    });
  });

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.filteredProducts().length / this.pageSize)));

  readonly pagedProducts = computed(() => {
    const start = this.currentPage() * this.pageSize;
    return this.filteredProducts().slice(start, start + this.pageSize);
  });

  readonly categoryOptionsForProduct = computed(() => {
    return this.activeCategories();
  });

  readonly canDeleteCategory = computed(() => {
    const id = this.deletingCategoryId();
    if (!id) return true;
    return (this.categoryProductCounts()[id] || 0) === 0 || this.reassignCategoryId() !== '';
  });

  readonly categoriesForReassign = computed(() => {
    return this.categories().filter(c => c.id !== this.deletingCategoryId());
  });

  constructor() {
  }

  private initPageEffect(): void {
    effect(() => {
      if (this.currentPage() >= this.totalPages()) {
        this.currentPage.set(Math.max(0, this.totalPages() - 1));
      }
    }, { injector: this.injector });
  }

  ngOnInit() {
    this.initPageEffect();
    this.subscriptionService.getMySubscription().subscribe({
      next: (sub) => {
        if (sub.nextDueDateUtc) {
          this.subscriptionDueDate.set(new Date(sub.nextDueDateUtc).toLocaleDateString('pt-BR'));
        }
        if (sub.storeBlocked) {
          this.subscriptionStatus.set('overdue');
        } else if (sub.billingStatus === 2) {
          this.subscriptionStatus.set('due-soon');
        }
      },
    });
    this.storeService.getMyStore().subscribe({
      next: (store) => {
        this.storeId.set(store.id);
        this.loadCategories(store.id);
        this.loadProducts(store.id);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  reloadProducts(): void {
    const storeId = this.storeId();
    if (storeId) this.loadProducts(storeId);
  }

  private loadCategories(storeId: string) {
    this.storeService.getStoreCategories(storeId).subscribe({
      next: (cats) => {
        this.categories.set(cats);
      },
    });
  }

  private loadProducts(storeId: string) {
    this.storeService.getStoreProducts(storeId).subscribe({
      next: (prods) => this.products.set(prods),
    });
  }

  // ── Category CRUD ────────────────────────────────────────

  addCategory() {
    const name = this.newCategoryName().trim();
    if (!name) { this.toast.showError('Informe o nome da categoria.'); return; }
    if (name.length > 80) { this.toast.showError('Nome deve ter no máximo 80 caracteres.'); return; }
    const normalized = name.toLowerCase();
    if (this.categories().some(c => c.name.toLowerCase() === normalized)) {
      this.toast.showError('Já existe uma categoria com esse nome.');
      return;
    }
    const sid = this.storeId();
    if (!sid) return;
    this.isAddingCategory.set(true);
    this.storeService.createStoreCategory(sid, {
      name,
      description: undefined,
      displayOrder: 0,
      isActive: true,
      isFeatured: false,
    }).subscribe({
      next: (created) => {
        this.categories.update(cats => [...cats, created]);
        this.newCategoryName.set('');
        this.isAddingCategory.set(false);
        this.toast.showSuccess(`Categoria "${created.name}" adicionada.`);
      },
      error: (err) => {
        this.isAddingCategory.set(false);
        const detail = err?.error?.detail;
        this.toast.showError(detail || 'Erro ao criar categoria.');
      }
    });
  }

  openEditCategory(cat: ProductCategory) {
    this.editingCategoryId.set(cat.id);
    this.editingCategoryName.set(cat.name);
  }

  closeEditCategory() {
    this.editingCategoryId.set(null);
    this.editingCategoryName.set('');
  }

  saveEditCategory() {
    const name = this.editingCategoryName().trim();
    if (!name) { this.toast.showError('Informe o nome da categoria.'); return; }
    if (name.length > 80) { this.toast.showError('Nome deve ter no máximo 80 caracteres.'); return; }
    const normalized = name.toLowerCase();
    const cat = this.categories().find(c => c.id === this.editingCategoryId());
    if (!cat) return;
    const sid = this.storeId();
    if (!sid) return;
    if (this.categories().some(c => c.id !== cat.id && c.name.toLowerCase() === normalized)) {
      this.toast.showError('Já existe uma categoria com esse nome.');
      return;
    }
    this.isEditingCategory.set(true);
    this.storeService.updateStoreCategory(sid, cat.id, {
      name,
      displayOrder: cat.displayOrder,
      isActive: cat.isActive,
      isFeatured: cat.isFeatured,
    }).subscribe({
      next: (updated) => {
        this.categories.update(cats => cats.map(c => c.id === updated.id ? updated : c));
        this.isEditingCategory.set(false);
        this.closeEditCategory();
        this.refreshProductsIfNeeded();
        this.toast.showSuccess('Categoria atualizada.');
      },
      error: (err) => {
        this.isEditingCategory.set(false);
        const detail = err?.error?.detail;
        this.toast.showError(detail || 'Erro ao atualizar categoria.');
      }
    });
  }

  toggleCategoryActive(cat: ProductCategory) {
    const sid = this.storeId();
    if (!sid) return;
    const updated = { ...cat, isActive: !cat.isActive };
    this.categories.update(cats => cats.map(c => c.id === cat.id ? updated : c));
    this.storeService.updateStoreCategory(sid, cat.id, {
      name: cat.name,
      displayOrder: cat.displayOrder,
      isActive: !cat.isActive,
      isFeatured: cat.isFeatured,
    }).subscribe({
      error: () => {
        this.categories.update(cats => cats.map(c => c.id === cat.id ? cat : c));
        this.toast.showError('Erro ao alterar status da categoria.');
      }
    });
  }

  openDeleteCategory(cat: ProductCategory) {
    const count = this.categoryProductCounts()[cat.id] || 0;
    this.deletingCategoryId.set(cat.id);
    this.deletingCategoryName.set(cat.name);
    this.deletingCategoryProductCount.set(count);
    this.reassignCategoryId.set('');
  }

  closeDeleteCategory() {
    this.deletingCategoryId.set(null);
    this.deletingCategoryName.set('');
    this.deletingCategoryProductCount.set(0);
    this.reassignCategoryId.set('');
  }

  confirmDeleteCategory() {
    if (!this.canDeleteCategory()) return;
    const sid = this.storeId();
    const catId = this.deletingCategoryId();
    if (!sid || !catId) return;
    this.isDeletingCategory.set(true);
    const reassignId = this.reassignCategoryId() || undefined;
    this.storeService.deleteStoreCategory(sid, catId, reassignId).subscribe({
      next: () => {
        this.categories.update(cats => cats.filter(c => c.id !== catId));
        if (this.productCatId() === catId) this.productCatId.set('');
        this.products.update(prods => prods.map(p => p.categoryId === catId ? { ...p, categoryId: reassignId || p.categoryId, categoryName: reassignId ? this.categories().find(c => c.id === reassignId)?.name || p.categoryName : p.categoryName } : p));
        this.isDeletingCategory.set(false);
        this.closeDeleteCategory();
        this.toast.showSuccess('Categoria excluída.');
      },
      error: () => {
        this.isDeletingCategory.set(false);
        this.toast.showError('Erro ao excluir categoria.');
      }
    });
  }

  moveCategoryUp(cat: ProductCategory) {
    const sorted = this.sortedCategories();
    const idx = sorted.findIndex(c => c.id === cat.id);
    if (idx <= 0) return;
    this.swapCategories(sorted, idx, idx - 1);
  }

  moveCategoryDown(cat: ProductCategory) {
    const sorted = this.sortedCategories();
    const idx = sorted.findIndex(c => c.id === cat.id);
    if (idx < 0 || idx >= sorted.length - 1) return;
    this.swapCategories(sorted, idx, idx + 1);
  }

  private swapCategories(sorted: ProductCategory[], idxA: number, idxB: number) {
    const sid = this.storeId();
    if (!sid) return;
    const ids = [sorted[idxA].id, sorted[idxB].id];
    const items = ids.map((id, i) => ({ id, displayOrder: sorted[idxB - (i * 2 - 1)]?.displayOrder ?? 0 }));
    const swapped = [...sorted];
    [swapped[idxA], swapped[idxB]] = [swapped[idxB], swapped[idxA]];
    this.categories.set(swapped.map((c, i) => ({ ...c, displayOrder: i + 1 })));
    this.isReorderingCategories.set(true);
    this.storeService.reorderStoreCategories(sid, items).subscribe({
      next: () => this.isReorderingCategories.set(false),
      error: () => {
        this.loadCategories(sid);
        this.isReorderingCategories.set(false);
        this.toast.showError('Erro ao reordenar categorias.');
      }
    });
  }

  reorderCategories(event: CustomEvent<{ from: number; to: number; complete: (data?: unknown) => void }>) {
    const { from, to, complete } = event.detail;
    const sid = this.storeId();
    if (!sid) { complete(false); return; }
    const sorted = this.sortedCategories();
    const copy = [...sorted];
    const [moved] = copy.splice(from, 1);
    copy.splice(to, 0, moved);
    const reordered = copy.map((c, i) => ({ ...c, displayOrder: i + 1 }));
    this.categories.set(reordered);
    const items = reordered.map(c => ({ id: c.id, displayOrder: c.displayOrder }));
    this.isReorderingCategories.set(true);
    this.storeService.reorderStoreCategories(sid, items).subscribe({
      next: () => { this.isReorderingCategories.set(false); complete(); },
      error: () => {
        this.loadCategories(sid);
        this.isReorderingCategories.set(false);
        complete(false);
        this.toast.showError('Erro ao reordenar categorias.');
      }
    });
  }

  private refreshProductsIfNeeded() {
    const sid = this.storeId();
    if (sid) this.loadProducts(sid);
  }

  // ── Priority tags ────────────────────────────────────────

  movePriorityUp(tag: string) {
    this.tagPriority.update(p => {
      const idx = p.indexOf(tag);
      if (idx <= 0) return p;
      const copy = [...p];
      [copy[idx - 1], copy[idx]] = [copy[idx], copy[idx - 1]];
      return copy;
    });
  }

  movePriorityDown(tag: string) {
    this.tagPriority.update(p => {
      const idx = p.indexOf(tag);
      if (idx < 0 || idx >= p.length - 1) return p;
      const copy = [...p];
      [copy[idx], copy[idx + 1]] = [copy[idx + 1], copy[idx]];
      return copy;
    });
  }

  private syncTagPriority() {
    const current = this.tagPriority();
    const selected = this.selectedTags();
    const cleaned = current.filter(t => selected.includes(t));
    const added = selected.filter(t => !cleaned.includes(t));
    this.tagPriority.set([...cleaned, ...added]);
  }

  // ── Forma de venda ──────────────────────────────────────

  setSaleMode(mode: ProductSaleMode) {
    this.saleMode.set(mode);
    this.markDirty();
  }

  // ── Variações: tamanho ───────────────────────────────────

  addSizeVariation() {
    this.sizeVariations.update(list => [...list, {
      uid: this.uid(),
      name: '',
      description: '',
      price: '',
      isDefault: list.length === 0,
      isActive: true,
    }]);
    this.markDirty();
  }

  removeSizeVariation(uid: string) {
    this.sizeVariations.update(list => {
      const filtered = list.filter(v => v.uid !== uid);
      if (filtered.length && !filtered.some(v => v.isDefault)) filtered[0].isDefault = true;
      return filtered;
    });
    this.markDirty();
  }

  setSizeDefault(uid: string) {
    this.sizeVariations.update(list => list.map(v => ({ ...v, isDefault: v.uid === uid })));
    this.markDirty();
  }

  reorderSizeVariations(event: CustomEvent<{ from: number; to: number; complete: (data?: unknown) => void }>) {
    const { from, to, complete } = event.detail;
    this.sizeVariations.update(list => {
      const copy = [...list];
      const [moved] = copy.splice(from, 1);
      copy.splice(to, 0, moved);
      return copy;
    });
    complete();
  }

  // ── Variações: peso fixo ─────────────────────────────────

  addFixedWeightVariation() {
    this.fixedWeightVariations.update(list => [...list, {
      uid: this.uid(),
      weight: '',
      unit: 'g',
      price: '',
      isDefault: list.length === 0,
      isActive: true,
    }]);
    this.markDirty();
  }

  removeFixedWeightVariation(uid: string) {
    this.fixedWeightVariations.update(list => {
      const filtered = list.filter(v => v.uid !== uid);
      if (filtered.length && !filtered.some(v => v.isDefault)) filtered[0].isDefault = true;
      return filtered;
    });
    this.markDirty();
  }

  setWeightDefault(uid: string) {
    this.fixedWeightVariations.update(list => list.map(v => ({ ...v, isDefault: v.uid === uid })));
    this.markDirty();
  }

  reorderFixedWeightVariations(event: CustomEvent<{ from: number; to: number; complete: (data?: unknown) => void }>) {
    const { from, to, complete } = event.detail;
    this.fixedWeightVariations.update(list => {
      const copy = [...list];
      const [moved] = copy.splice(from, 1);
      copy.splice(to, 0, moved);
      return copy;
    });
    complete();
  }

  equivalentPricePerKg(item: FixedWeightVariation): string {
    const grams = item.unit === 'kg' ? Number(item.weight || 0) * 1000 : Number(item.weight || 0);
    const price = this.parseBRL(item.price);
    if (!grams || !price) return 'R$ 0,00/kg';
    return `R$ ${(price / grams * 1000).toFixed(2).replace('.', ',')}/kg`;
  }

  // ── Peso variável ────────────────────────────────────────

  updateWeightConfigField(field: keyof WeightConfigForm, value: string | boolean) {
    this.weightConfig.update(w => ({ ...w, [field]: value }));
    this.markDirty();
  }

  variableWeightSampleGrams(): number {
    const cfg = this.weightConfig();
    const minG = Number(cfg.minGrams) || 1;
    const maxG = Number(cfg.maxGrams) || 500;
    return Math.min(Math.max(500, minG), Math.max(maxG, minG));
  }

  variableWeightExamplePrice(): string {
    const cfg = this.weightConfig();
    const pricePerKg = this.parseBRL(cfg.pricePerKg);
    const sample = this.variableWeightSampleGrams();
    if (!pricePerKg) return 'R$ 0,00';
    return `R$ ${this.formatBRL(pricePerKg * sample / 1000)}`;
  }

  maskMoney(value: string): string {
    const digits = String(value ?? '').replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) return '0,00';
    return num.toFixed(2).replace('.', ',');
  }

  // ── Novo / Limpar ────────────────────────────────────────

  newProduct() {
    this.selectedId.set(null);
    this.clearForm();
    this.searchQuery.set('');
    this.currentPage.set(0);
  }

  selectProduct(product: Product) {
    this.selectedId.set(product.id);
    this.productName.set(product.name);
    this.productDesc.set(product.description || '');
    this.productPrice.set(this.formatBRL(product.price));
    this.productCatId.set(product.categoryId);
    this.productImage.set(product.imageUrl || '');
    this.productImagePreview.set(product.imageUrl || null);
    this.isProductActive.set(product.isAvailable);
    this.tagDestaque.set(product.isFeatured ?? false);
    this.tagMaisVendido.set(product.isBestSeller ?? false);
    this.tagNovidade.set(product.isNew ?? false);
    this.tagPriority.set(product.tagPriority ? product.tagPriority.split(',').filter(Boolean) : []);

    const sm = product.saleMode as ProductSaleMode || 'single';
    this.saleMode.set(sm);

    if (sm === 'size') {
      this.sizeVariations.set((product.variations ?? []).map(v => ({
        uid: v.id,
        name: v.name,
        description: v.description ?? '',
        price: this.formatBRL(v.price),
        isDefault: v.isDefault ?? false,
        isActive: v.isActive,
      })));
    } else {
      this.sizeVariations.set([]);
    }
    if (sm === 'fixed_weight') {
      this.fixedWeightVariations.set((product.variations ?? []).map(v => ({
        uid: v.id,
        weight: v.weightGrams ? String(v.weightGrams) : '',
        unit: 'g',
        price: this.formatBRL(v.price),
        isDefault: v.isDefault ?? false,
        isActive: v.isActive,
      })));
    } else {
      this.fixedWeightVariations.set([]);
    }

    if (sm === 'variable_weight' && product.weightConfig) {
      const wc = product.weightConfig;
      this.weightConfig.set({
        pricePerKg: this.formatBRL(wc.pricePerKg),
        minGrams: String(wc.minGrams),
        maxGrams: String(wc.maxGrams),
        incrementGrams: String(wc.incrementGrams),
        isEstimated: wc.isEstimated,
      });
    } else {
      this.weightConfig.set({ pricePerKg: '', minGrams: '', maxGrams: '', incrementGrams: '', isEstimated: false });
    }

    this.optionGroups.set((product.optionGroups ?? []).map(g => ({
      ...g,
      choiceType: (g.choiceType as 'single' | 'multiple') || 'single',
    })));
    this.groupCounter = this.optionGroups().reduce((max, g) => {
      const match = g.name.match(/^Grupo\s+(\d+)$/);
      return match ? Math.max(max, parseInt(match[1], 10)) : max;
    }, 0);
  }

  copyProductOptions(product: Product): void {
    if (this.selectedId()) {
      this.selectedId.set(null);
      this.productName.set('');
      this.productDesc.set('');
      this.productPrice.set('0,00');
      this.productCatId.set('');
      this.productImage.set('');
      this.productImagePreview.set(null);
      this.isProductActive.set(true);
      this.tagDestaque.set(false);
      this.tagMaisVendido.set(false);
      this.tagNovidade.set(false);
      this.tagPriority.set([]);
    }

    const saleMode = product.saleMode ?? 'single';
    this.saleMode.set(saleMode);

    this.sizeVariations.set(saleMode === 'size'
      ? (product.variations ?? []).map(v => ({
        uid: this.uid(),
        name: v.name,
        description: v.description ?? '',
        price: this.formatBRL(v.price),
        isDefault: v.isDefault ?? false,
        isActive: v.isActive,
      }))
      : []);

    this.fixedWeightVariations.set(saleMode === 'fixed_weight'
      ? (product.variations ?? []).map(v => ({
        uid: this.uid(),
        weight: v.weightGrams ? String(v.weightGrams) : '',
        unit: 'g',
        price: this.formatBRL(v.price),
        isDefault: v.isDefault ?? false,
        isActive: v.isActive,
      }))
      : []);

    if (saleMode === 'variable_weight' && product.weightConfig) {
      this.weightConfig.set({
        pricePerKg: this.formatBRL(product.weightConfig.pricePerKg),
        minGrams: String(product.weightConfig.minGrams),
        maxGrams: String(product.weightConfig.maxGrams),
        incrementGrams: String(product.weightConfig.incrementGrams),
        isEstimated: product.weightConfig.isEstimated,
      });
    } else {
      this.weightConfig.set({ pricePerKg: '', minGrams: '', maxGrams: '', incrementGrams: '', isEstimated: false });
    }

    const copiedGroups = (product.optionGroups ?? []).map((group, index) => ({
      ...group,
      id: this.uid(),
      choiceType: (group.choiceType as 'single' | 'multiple') || 'single',
      displayOrder: index + 1,
      items: group.items.map((item, itemIndex) => ({ ...item, id: this.uid(), displayOrder: itemIndex + 1 })),
    }));
    this.optionGroups.set(copiedGroups);
    this.groupCounter = copiedGroups.reduce((max, g) => {
      const match = g.name.match(/^Grupo\s+(\d+)$/);
      return match ? Math.max(max, parseInt(match[1], 10)) : max;
    }, 0);
    this.expandedGroupId.set(copiedGroups[0]?.id ?? null);
    this.formDirty.set(true);
    this.toast.showSuccess('Opcoes copiadas para o produto em criacao.');
  }

  // ── Save ─────────────────────────────────────────────────

  saveProduct() {
    if (this.isSaving()) return;

    const name = this.productName().trim();
    if (!name) { this.toast.showError('Informe o nome do produto.'); return; }
    if (name.length > 120) { this.toast.showError('Nome deve ter no máximo 120 caracteres.'); return; }

    const catId = this.productCatId();
    if (!catId) { this.toast.showError('Selecione uma categoria.'); return; }

    const desc = this.productDesc().trim();
    if (desc.length > 500) { this.toast.showError('Descrição deve ter no máximo 500 caracteres.'); return; }

    const sm = this.saleMode();
    const price = this.parseBRL(this.productPrice());
    if (sm === 'single' && (isNaN(price) || price <= 0)) { this.toast.showError('Informe um preço válido maior que zero.'); return; }

    if (!this.productImage()) { this.toast.showError('Adicione uma imagem do produto.'); return; }

    if (sm === 'size') {
      const active = this.sizeVariations().filter(v => v.isActive && this.parseBRL(v.price) > 0 && v.name.trim());
      if (!active.length) { this.toast.showError('Cadastre ao menos um tamanho ativo com preço e nome.'); return; }
    }
    if (sm === 'fixed_weight') {
      const active = this.fixedWeightVariations().filter(v => v.isActive && this.parseBRL(v.price) > 0 && Number(v.weight) > 0);
      if (!active.length) { this.toast.showError('Cadastre ao menos um peso ativo com preço.'); return; }
    }
    if (sm === 'variable_weight') {
      const wc = this.weightConfig();
      const ppk = this.parseBRL(wc.pricePerKg);
      const min = Number(wc.minGrams);
      const max = Number(wc.maxGrams);
      const inc = Number(wc.incrementGrams);
      if (ppk <= 0) { this.toast.showError('Informe o preço por kg.'); return; }
      if (min <= 0) { this.toast.showError('Peso mínimo deve ser maior que zero.'); return; }
      if (max < min) { this.toast.showError('Peso máximo não pode ser menor que o mínimo.'); return; }
      if (inc <= 0) { this.toast.showError('Incremento deve ser maior que zero.'); return; }
    }

    for (const g of this.optionGroups()) {
      const label = g.name?.trim() || 'sem nome';
      if (!g.name?.trim()) { this.toast.showError(`Informe o nome do grupo de opções.`); return; }
      if (g.maxChoices < 1) { this.toast.showError(`Grupo "${label}": o máximo deve ser ao menos 1.`); return; }
      if (g.minChoices === 0 && g.maxChoices === 0) { this.toast.showError(`Grupo "${label}": mínimo e máximo não podem ser ambos zero.`); return; }
      if (g.minChoices > g.maxChoices) { this.toast.showError(`Grupo "${label}": mínimo (${g.minChoices}) não pode ser maior que máximo (${g.maxChoices}).`); return; }
      if (g.items.length === 0 || g.items.some(i => !i.name?.trim())) { this.toast.showError(`Grupo "${label}" está com item vazio. Preencha o nome de todos os itens.`); return; }
    }

    const existingId = this.selectedId();
    const sid = this.storeId();
    if (!sid) return;

    const currentProduct = existingId ? this.products().find(p => p.id === existingId) : null;

    const variations = this.buildVariationsPayload(sm);

    const body: any = {
      categoryId: catId,
      name,
      description: desc,
      price,
      imageUrl: this.productImage() || null,
      isAvailable: this.isProductActive(),
      isFeatured: this.tagDestaque(),
      displayOrder: this.products().length,
      isBestSeller: this.tagMaisVendido(),
      isNew: this.tagNovidade(),
      tagPriority: this.tagPriority().join(','),
      saleMode: sm,
      additionals: currentProduct?.additionals ?? [],
      choiceOptions: currentProduct?.choiceOptions ?? [],
      variations,
      optionGroups: this.optionGroups().map(g => ({
        id: g.id,
        name: g.name,
        isRequired: g.isRequired,
        choiceType: g.choiceType,
        minChoices: g.minChoices,
        maxChoices: g.maxChoices,
        displayOrder: g.displayOrder,
        items: g.items.map(i => ({ id: i.id, name: i.name, price: i.price, displayOrder: i.displayOrder })),
      })),
    };

    if (sm === 'variable_weight') {
      const wc = this.weightConfig();
      body.weightConfig = {
        pricePerKg: this.parseBRL(wc.pricePerKg),
        minGrams: Number(wc.minGrams),
        maxGrams: Number(wc.maxGrams),
        incrementGrams: Number(wc.incrementGrams),
        isEstimated: wc.isEstimated,
      };
    }

    this.isSaving.set(true);
    const req$ = existingId
      ? this.storeService.updateProduct(sid, existingId, body)
      : this.storeService.createProduct(sid, body);

    req$.subscribe({
      next: (created) => {
        this.products.update(list => {
          const idx = list.findIndex(p => p.id === created.id);
          if (idx >= 0) {
            const copy = [...list];
            copy[idx] = created;
            return copy;
          }
          return [...list, created];
        });
        this.clearForm();
        this.isSaving.set(false);
        this.toast.showSuccess('Produto salvo com sucesso!');
        this.onProductSaved();
      },
      error: (err: any) => {
        this.isSaving.set(false);
        const validationErrors = err?.error?.errors;
        const detail = err?.error?.detail || err?.error?.title || '';
        if (validationErrors) {
          const messages: string[] = [];
          for (const key of Object.keys(validationErrors)) {
            const fieldErrors = validationErrors[key];
            if (Array.isArray(fieldErrors)) {
              for (const m of fieldErrors) messages.push(m);
            }
          }
          if (messages.length > 0) { this.toast.showError(messages.join('. ')); return; }
        }
        if (detail) { this.toast.showError(detail); return; }
        this.toast.showError('Erro ao salvar produto. Verifique os dados informados.');
      }
    });
  }

  protected onProductSaved(): void {
    // Seller dashboard overrides this to close its modal after the API confirms the save.
  }

  private buildVariationsPayload(sm: ProductSaleMode): any[] {
    if (sm === 'size') {
      return this.sizeVariations().map((v, idx) => ({
        name: v.name.trim(),
        description: v.description.trim() || null,
        price: this.parseBRL(v.price),
        isDefault: v.isDefault,
        isActive: v.isActive,
        displayOrder: idx + 1,
      }));
    }
    if (sm === 'fixed_weight') {
      return this.fixedWeightVariations().map((v, idx) => {
        const w = Number(v.weight || 0);
        const grams = v.unit === 'kg' ? Math.round(w * 1000) : Math.round(w);
        return {
          name: this.formatWeightLabel(grams),
          weightGrams: grams,
          price: this.parseBRL(v.price),
          isDefault: v.isDefault,
          isActive: v.isActive,
          displayOrder: idx + 1,
        };
      });
    }
    return [];
  }

  private formatWeightLabel(grams: number): string {
    return grams >= 1000 ? `${(grams / 1000).toFixed(2).replace(/\.?0+$/, '').replace('.', ',')} kg` : `${grams} g`;
  }

  // ── Product list helpers ─────────────────────────────────

  productStartingPrice(product: Product): string {
    const sm = product.saleMode as string;
    if (sm === 'size' || sm === 'fixed_weight') {
      const active = (product.variations ?? []).filter(v => v.isActive && v.price > 0);
      if (active.length) return `A partir de R$ ${active.map(v => v.price).reduce((a, b) => a < b ? a : b).toFixed(2).replace('.', ',')}`;
    }
    if (sm === 'variable_weight' && product.weightConfig) {
      const est = product.weightConfig.isEstimated ? ' (estimado)' : '';
      return `R$ ${product.weightConfig.pricePerKg.toFixed(2).replace('.', ',')}/kg${est}`;
    }
    if (product.price > 0) return `R$ ${product.price.toFixed(2).replace('.', ',')}`;
    return 'Sem preço';
  }

  deleteLocalProduct(product: Product) {
    if (!confirm(`Remover "${product.name}"?`)) return;
    const sid = this.storeId();
    if (!sid) return;
    this.storeService.deleteProduct(sid, product.id).subscribe({
      next: () => {
        this.products.update(list => list.filter(p => p.id !== product.id));
        if (this.selectedId() === product.id) this.clearForm();
        if (this.currentPage() >= this.totalPages()) {
          this.currentPage.set(Math.max(0, this.totalPages() - 1));
        }
        this.toast.showSuccess('Produto removido.');
      },
      error: () => this.toast.showError('Erro ao remover produto.')
    });
  }

  private clearForm() {
    this.selectedId.set(null);
    this.productName.set('');
    this.productDesc.set('');
    this.productPrice.set('0,00');
    this.productCatId.set('');
    this.productImage.set('');
    this.productImagePreview.set(null);
    this.isProductActive.set(true);
    this.tagDestaque.set(false);
    this.tagMaisVendido.set(false);
    this.tagNovidade.set(false);
    this.tagPriority.set([]);
    this.saleMode.set('single');
    this.sizeVariations.set([]);
    this.fixedWeightVariations.set([]);
    this.weightConfig.set({ pricePerKg: '', minGrams: '', maxGrams: '', incrementGrams: '', isEstimated: false });
    this.optionGroups.set([]);
    this.groupCounter = 0;
  }

  triggerFileInput() { this.fileInput?.nativeElement?.click(); }

  private async compressImage(file: File, maxWidth: number = 1200, quality: number = 0.75): Promise<File> {
    return new Promise((resolve) => {
      const reader = new FileReader();
      reader.readAsDataURL(file);
      reader.onload = (event) => {
        const img = new Image();
        img.src = event.target?.result as string;
        img.onload = () => {
          const canvas = document.createElement('canvas');
          let width = img.width;
          let height = img.height;
          if (width > maxWidth) { height = Math.round((height * maxWidth) / width); width = maxWidth; }
          canvas.width = width;
          canvas.height = height;
          const ctx = canvas.getContext('2d');
          if (ctx) ctx.drawImage(img, 0, 0, width, height);
          canvas.toBlob(
            (blob) => {
              if (blob) { resolve(new File([blob], file.name, { type: 'image/jpeg', lastModified: Date.now() })); }
              else { resolve(file); }
            },
            'image/jpeg',
            quality
          );
        };
      };
    });
  }

  async onImageSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    const rawFile = input.files?.[0];
    if (!rawFile) return;
    this.uploadingImage.set(true);
    let file = rawFile;
    try { file = await this.compressImage(rawFile, 1200, 0.75); } catch { /* proceed */ }
    this.storeService.uploadImage(file, 'products').subscribe({
      next: (res) => {
        this.productImage.set(res.url);
        this.productImagePreview.set(res.url);
        this.uploadingImage.set(false);
      },
      error: (err) => {
        this.toast.showError('Erro ao enviar imagem.');
        this.uploadingImage.set(false);
        input.value = '';
      }
    });
  }

  onPriceInput(value: string) {
    const digits = value.replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) { this.productPrice.set('0,00'); return; }
    this.productPrice.set(num.toFixed(2).replace('.', ','));
  }

  private formatBRL(num: number): string { return num.toFixed(2).replace('.', ','); }

  private parseBRL(val: string): number { return parseFloat(val.replace(/\./g, '').replace(',', '.')) || 0; }

  prevPage() { if (this.currentPage() > 0) this.currentPage.update(p => p - 1); }
  nextPage() { if (this.currentPage() < this.totalPages() - 1) this.currentPage.update(p => p + 1); }

  togglePreview(productId: string) { this.expandedId.update(id => id === productId ? null : productId); }

  toggleTag(type: string) {
    if (type === 'destaque') this.tagDestaque.update(v => !v);
    if (type === 'mais_vendido') this.tagMaisVendido.update(v => !v);
    if (type === 'novidade') this.tagNovidade.update(v => !v);
    this.syncTagPriority();
  }

  private uid(): string {
    const ms = Date.now();
    const hex = (n: number, len: number): string => n.toString(16).padStart(len, '0');
    const rand = (): number => Math.floor(Math.random() * 256);
    const ts = hex(ms, 12);
    const ver_rand = 0x70 | (rand() & 0x0F);
    const var_rand = 0x80 | (rand() & 0x3F);
    return `${ts.slice(0, 8)}-${ts.slice(8, 12)}-${hex(ver_rand, 2)}${hex(rand(), 2)}-${hex(var_rand, 2)}${hex(rand(), 2)}-${hex(rand(), 2)}${hex(rand(), 2)}${hex(rand(), 2)}${hex(rand(), 2)}${hex(rand(), 2)}${hex(rand(), 2)}`;
  }

  // ── Grupos de Opções ─────────────────────────────────────

  addOptionGroup() {
    const id = this.uid();
    this.groupCounter++;
    const index = this.groupCounter;
    this.optionGroups.update(groups => [...groups, {
      id,
      name: `Grupo ${index}`,
      isRequired: false,
      choiceType: 'multiple' as const,
      minChoices: 0,
      maxChoices: 3,
      displayOrder: groups.length + 1,
      items: [],
    }]);
    this.expandedGroupId.set(id);
  }

  toggleGroupExpanded(groupId: string) { this.expandedGroupId.update(id => id === groupId ? null : groupId); }

  reorderGroups(event: CustomEvent<{ from: number; to: number; complete: (data?: unknown) => void }>) {
    const { from, to, complete } = event.detail;
    this.optionGroups.update(groups => {
      const copy = [...groups];
      const [moved] = copy.splice(from, 1);
      copy.splice(to, 0, moved);
      return copy.map((g, i) => ({ ...g, displayOrder: i + 1 }));
    });
    complete();
  }

  updateGroupName(groupId: string, name: string) {
    this.markDirty();
    this.optionGroups.update(groups => groups.map(g => g.id === groupId ? { ...g, name } : g));
  }

  updateGroupChoiceType(groupId: string, choiceType: 'single' | 'multiple') {
    this.markDirty();
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      const max = choiceType === 'single' ? 1 : (g.maxChoices < 2 ? 3 : g.maxChoices);
      const min = choiceType === 'single' ? Math.min(g.minChoices, 1) : g.minChoices;
      return { ...g, choiceType, maxChoices: max, minChoices: min, isRequired: min >= 1 };
    }));
  }

  toggleGroupRequired(groupId: string) {
    this.markDirty();
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      const isRequired = !(g.minChoices >= 1);
      return { ...g, isRequired, minChoices: isRequired ? Math.max(1, g.minChoices) : 0 };
    }));
  }

  updateGroupMin(groupId: string, value: number | string) {
    this.markDirty();
    const n = Number(value);
    if (isNaN(n)) return;
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      const minChoices = Math.max(0, Math.min(n, g.maxChoices));
      return { ...g, minChoices, isRequired: minChoices >= 1 };
    }));
  }

  updateGroupMax(groupId: string, value: number | string) {
    this.markDirty();
    const n = Number(value);
    if (isNaN(n)) return;
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      const maxChoices = Math.max(1, n);
      const minChoices = Math.min(g.minChoices, maxChoices);
      return { ...g, maxChoices, minChoices, isRequired: minChoices >= 1 };
    }));
  }

  removeOptionGroup(groupId: string) {
    const group = this.optionGroups().find(g => g.id === groupId);
    if (!confirm(`Excluir o grupo "${group?.name || 'sem nome'}"?`)) return;
    this.optionGroups.update(groups => groups.filter(g => g.id !== groupId));
  }

  addOptionItem(groupId: string) {
    this.markDirty();
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      return { ...g, items: [...g.items, { id: this.uid(), name: '', price: 0, displayOrder: g.items.length + 1 }] };
    }));
  }

  removeOptionItem(groupId: string, itemId: string) {
    if (!confirm('Remover este item?')) return;
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      return { ...g, items: g.items.filter(i => i.id !== itemId) };
    }));
  }

  updateOptionItemName(groupId: string, itemId: string, name: string) {
    this.markDirty();
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      return { ...g, items: g.items.map(i => i.id === itemId ? { ...i, name } : i) };
    }));
  }

  updateOptionItemPrice(groupId: string, itemId: string, rawValue: string) {
    this.markDirty();
    const digits = rawValue.replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    const price = isNaN(num) ? 0 : num;
    this.optionGroups.update(groups => groups.map(g => {
      if (g.id !== groupId) return g;
      return { ...g, items: g.items.map(i => i.id === itemId ? { ...i, price } : i) };
    }));
  }

  formatOptionItemPrice(groupId: string, itemId: string): string {
    const group = this.optionGroups().find(g => g.id === groupId);
    const item = group?.items.find(i => i.id === itemId);
    if (!item) return '0,00';
    return this.formatBRL(item.price);
  }

  goBack() { this.router.navigate(['/configurar-loja/entrega']); }

  goNext() {
    const prods = this.products();
    if (prods.length === 0) { this.toast.showError('Cadastre pelo menos um produto com preço e imagem antes de avançar.'); return; }
    for (const product of prods) {
      if (!product.price || product.price <= 0) {
        const sm = product.saleMode as string;
        if (sm === 'size' || sm === 'fixed_weight' || sm === 'variable_weight') continue;
        this.toast.showError(`O produto "${product.name}" precisa de um preço válido maior que zero.`); return;
      }
      if (!product.imageUrl) { this.toast.showError(`O produto "${product.name}" precisa de uma imagem.`); return; }
    }
    this.router.navigate(['/configurar-loja/publicar']);
  }

  saveDraft(): void {
    this.saveStatus.set('saved');
    this.formDirty.set(false);
    setTimeout(() => { if (this.saveStatus() === 'saved') this.saveStatus.set('idle'); }, 2000);
  }
}
