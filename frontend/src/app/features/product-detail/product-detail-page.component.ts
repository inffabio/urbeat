import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { IonContent, IonIcon } from '@ionic/angular/standalone';

import { CatalogService } from '../../core/services/catalog.service';
import { CartService } from '../../core/services/cart.service';
import { Product, ProductVariation, ProductChoiceOption, ProductAdditional, ProductOptionGroup, ProductOptionItem, ProductWeightConfig } from '../../shared/models/product.model';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { BackToMenuLinkComponent } from '../../shared/components/back-to-menu-link/back-to-menu-link.component';

@Component({
  selector: 'app-product-detail-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, BrlCurrencyPipe, BackToMenuLinkComponent],
  templateUrl: './product-detail-page.component.html',
  styleUrl: './product-detail-page.component.scss',
})
export class ProductDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly catalog = inject(CatalogService);
  private readonly cart = inject(CartService);
  private readonly location = inject(Location);

  readonly product = signal<Product | null>(null);
  readonly quantity = signal(1);
  readonly notes = signal('');
  readonly loading = signal(true);
  readonly toastVisible = signal(false);

  readonly selectedVariation = signal<ProductVariation | null>(null);
  readonly selectedChoice = signal<ProductChoiceOption | null>(null);
  readonly selectedAdditionals = signal<ProductAdditional[]>([]);

  /** groupId -> ids dos itens selecionados */
  readonly groupSelections = signal<Record<string, string[]>>({});

  // Peso variável
  readonly selectedWeightGrams = signal<number>(0);

  readonly optionGroups = computed<ProductOptionGroup[]>(() => {
    const groups = this.product()?.optionGroups ?? [];
    return [...groups]
      .filter(g => (g.items?.length ?? 0) > 0)
      .sort((a, b) => a.displayOrder - b.displayOrder);
  });

  readonly activeVariations = computed(() => {
    const p = this.product();
    if (!p) return [];
    return (p.variations ?? []).filter(v => v.isActive).sort((a, b) => a.displayOrder - b.displayOrder);
  });

  readonly isSizeMode = computed(() => this.product()?.saleMode === 'size');
  readonly isFixedWeightMode = computed(() => this.product()?.saleMode === 'fixed_weight');
  readonly isVariableWeightMode = computed(() => this.product()?.saleMode === 'variable_weight');

  readonly weightConfig = computed<ProductWeightConfig | null>(() => {
    return this.product()?.weightConfig ?? null;
  });

  ngOnInit(): void {
    const nav = this.router.getCurrentNavigation();
    const state = (nav?.extras.state ?? history.state) as { product?: Product };
    if (state?.product) {
      this.product.set(state.product);
      this.initDefaults();
      this.loading.set(false);
      return;
    }
    const storeId = this.cart.storeId();
    const productId = this.route.snapshot.paramMap.get('productId');
    if (!storeId || !productId) {
      this.loading.set(false);
      return;
    }
    this.catalog.getProducts(storeId).subscribe({
      next: (list) => {
        const p = list.find((x) => x.id === productId) ?? null;
        this.product.set(p);
        this.initDefaults();
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private initDefaults(): void {
    const p = this.product();
    if (!p) return;

    // Pré-seleciona variação padrão
    const def = (p.variations ?? []).find(v => v.isDefault && v.isActive);
    if (def) this.selectedVariation.set(def);

    // Peso variável: inicia com o mínimo
    const wc = p.weightConfig;
    if (wc && p.saleMode === 'variable_weight') {
      this.selectedWeightGrams.set(wc.minGrams);
    }
  }

  // ── Variation / size ──────────────────────────────────

  selectVariation(variation: ProductVariation): void {
    this.selectedVariation.set(variation);
  }

  variationPriceLabel(v: ProductVariation): string {
    const replaces = this.isSizeMode() || this.isFixedWeightMode();
    if (v.price <= 0) return '';
    const formatted = `R$ ${v.price.toFixed(2).replace('.', ',')}`;
    return replaces ? formatted : `+ ${formatted}`;
  }

  // ── Variable weight ───────────────────────────────────

  incrementWeight(amount: number): void {
    const wc = this.weightConfig();
    if (!wc) return;
    const step = wc.incrementGrams;
    this.selectedWeightGrams.update(w => {
      const next = w + amount * step;
      return Math.max(wc.minGrams, Math.min(wc.maxGrams, next));
    });
  }

  // ── Option groups ─────────────────────────────────────

  isGroupItemSelected(group: ProductOptionGroup, item: ProductOptionItem): boolean {
    return (this.groupSelections()[group.id ?? ''] ?? []).includes(item.id ?? '');
  }

  private isSingleGroup(group: ProductOptionGroup): boolean {
    return group.choiceType === 'single';
  }

  toggleGroupItem(group: ProductOptionGroup, item: ProductOptionItem): void {
    const gid = group.id ?? '';
    const iid = item.id ?? '';
    this.groupSelections.update(map => {
      const current = map[gid] ?? [];
      if (this.isSingleGroup(group)) {
        return { ...map, [gid]: [iid] };
      }
      if (current.includes(iid)) {
        return { ...map, [gid]: current.filter(x => x !== iid) };
      }
      if (current.length >= group.maxChoices) {
        return map;
      }
      return { ...map, [gid]: [...current, iid] };
    });
  }

  groupSubtitle(group: ProductOptionGroup): string {
    const m = group.minChoices;
    const M = group.maxChoices;
    if (M <= 1) return m >= 1 ? 'Obrigatório — escolha 1 opção' : 'Opcional — escolha 1 opção';
    if (m === 0) return `Opcional — escolha até ${M} opções`;
    if (m === M) return `Escolha ${M} opções`;
    return `Escolha de ${m} a ${M} opções`;
  }

  private selectedItemsOf(group: ProductOptionGroup): ProductOptionItem[] {
    const ids = this.groupSelections()[group.id ?? ''] ?? [];
    return (group.items ?? []).filter(i => ids.includes(i.id ?? ''));
  }

  // ── Price ─────────────────────────────────────────────

  readonly finalUnitPrice = computed(() => {
    const p = this.product();
    if (!p) return 0;

    let base = 0;
    let addTotal = 0;

    const sm = p.saleMode;
    if (sm === 'size' || sm === 'fixed_weight') {
      const v = this.selectedVariation();
      base = v ? v.price : p.price;
    } else if (sm === 'variable_weight') {
      const wc = p.weightConfig;
      const grams = this.selectedWeightGrams();
      base = wc ? Math.round(wc.pricePerKg * grams / 10) / 100 : p.price;
    } else {
      base = p.price;
      const legacyV = this.selectedVariation();
      if (legacyV) addTotal += legacyV.price;
    }

    const c = this.selectedChoice();
    if (c) addTotal += c.price;

    for (const a of this.selectedAdditionals()) {
      addTotal += a.price;
    }

    for (const g of this.optionGroups()) {
      for (const item of this.selectedItemsOf(g)) {
        addTotal += item.price;
      }
    }

    return base + addTotal;
  });

  readonly isSelectionValid = computed(() => {
    const sm = this.product()?.saleMode;
    if (sm === 'size' || sm === 'fixed_weight') {
      if (!this.selectedVariation()) return false;
    }
    if (this.isLegacyVariationMode() && !this.selectedVariation()) return false;
    if (this.hasActiveChoices() && !this.selectedChoice()) return false;
    for (const g of this.optionGroups()) {
      const count = (this.groupSelections()[g.id ?? ''] ?? []).length;
      if (count < g.minChoices) return false;
    }
    return true;
  });

  readonly maxNotes = 250;
  readonly notesLeft = computed(() => this.maxNotes - this.notes().length);

  readonly hasActiveChoices = computed(() => {
    const list = this.product()?.choiceOptions || [];
    return list.some(x => x.isActive);
  });

  readonly hasActiveAdditionals = computed(() => {
    const list = this.product()?.additionals || [];
    return list.some(x => x.isActive);
  });

  readonly hasActiveVariations = computed(() => {
    const p = this.product();
    if (!p) return false;
    const sm = p.saleMode;
    if (sm === 'size' || sm === 'fixed_weight') return true;
    // Legado: produto "single" com variações antigas ainda exige seleção
    return sm !== 'variable_weight' && ((p.variations ?? []).some(v => v.isActive));
  });

  /** Variações legadas em produtos "single" somam ao preço base. */
  readonly isLegacyVariationMode = computed(() => {
    const p = this.product();
    if (!p) return false;
    const sm = p.saleMode;
    return sm !== 'size' && sm !== 'fixed_weight' && sm !== 'variable_weight'
      && ((p.variations ?? []).some(v => v.isActive));
  });

  // ── Actions ───────────────────────────────────────────

  onBack(): void { this.location.back(); }
  inc(): void { this.quantity.update((q) => q + 1); }
  dec(): void { this.quantity.update((q) => Math.max(1, q - 1)); }
  onNotesChange(value: string): void { if (value.length <= this.maxNotes) this.notes.set(value); }

  toggleAdditional(add: ProductAdditional): void {
    this.selectedAdditionals.update(list => {
      if (list.includes(add)) {
        return list.filter(x => x !== add);
      }
      return [...list, add];
    });
  }

  selectChoice(choice: ProductChoiceOption): void {
    this.selectedChoice.set(choice);
  }

  addToCart(): void {
    const p = this.product();
    if (!p) return;
    if (!this.isSelectionValid()) return;

    const selVariation = this.selectedVariation();
    const selChoice = this.selectedChoice();
    const selAdditionals = this.selectedAdditionals();

    const groupSelections = this.optionGroups()
      .map(g => ({
        groupId: g.id ?? '',
        groupName: g.name,
        itemIds: this.selectedItemsOf(g).map(i => i.id ?? ''),
        itemNames: this.selectedItemsOf(g).map(i => i.name),
      }))
      .filter(g => g.itemNames.length > 0);

    const groupItemNames = groupSelections.flatMap(g => g.itemNames);

    const weightGrams = p.saleMode === 'variable_weight' ? this.selectedWeightGrams() : undefined;

    this.cart.addItem({
      productId: p.id,
      productName: p.name,
      productImage: p.imageUrl,
      productDescription: p.description,
      quantity: this.quantity(),
      unitPrice: this.finalUnitPrice(),
      notes: this.notes() || undefined,
      variationId: selVariation?.id,
      variationName: selVariation?.name,
      weightGrams,
      choiceOptionId: selChoice?.id,
      choiceOptionName: selChoice?.name,
      additionalIds: selAdditionals.map(a => a.id),
      additionalNames: [...selAdditionals.map(a => a.name), ...groupItemNames],
      optionGroups: groupSelections,
    });

    this.toastVisible.set(true);
    setTimeout(() => {
      this.toastVisible.set(false);
      this.location.back();
    }, 900);
  }
}
