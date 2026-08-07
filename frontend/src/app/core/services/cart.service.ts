import { Injectable, signal, computed, effect } from '@angular/core';
import { CartItem } from '../../shared/models/cart-item.model';
import { CheckoutItemRequest } from '../../shared/models/checkout.model';

const STORAGE_KEY = 'urbeat_cart';
const STORE_KEY = 'urbeat_cart_store';

interface PersistedCart {
  storeId: string | null;
  storeName: string | null;
  storeLogoUrl: string | null;
  items: CartItem[];
}

@Injectable({ providedIn: 'root' })
export class CartService {
  readonly items = signal<CartItem[]>([]);
  readonly storeId = signal<string | null>(null);
  readonly storeName = signal<string | null>(null);
  readonly storeLogoUrl = signal<string | null>(null);

  readonly totalItems = computed(() =>
    this.items().reduce((sum, item) => sum + item.quantity, 0),
  );
  readonly subtotal = computed(() =>
    this.items().reduce((sum, item) => sum + item.quantity * item.unitPrice, 0),
  );
  readonly isEmpty = computed(() => this.items().length === 0);

  constructor() {
    this.load();
    effect(() => {
      this.persist();
    });
  }

  setStore(storeId: string, storeName: string, logoUrl: string): void {
    if (this.storeId() && this.storeId() !== storeId) {
      this.items.set([]);
    }
    this.storeId.set(storeId);
    this.storeName.set(storeName);
    this.storeLogoUrl.set(logoUrl);
  }

  addItem(item: CartItem): void {
    if (!item.id) {
      item.id = window.crypto.randomUUID ? window.crypto.randomUUID() : Math.random().toString(36).substring(2, 9);
    }
    this.items.update((current) => {
      const existing = current.find(
        (i) => i.productId === item.productId && 
               i.notes === item.notes &&
               i.variationId === item.variationId &&
               i.weightGrams === item.weightGrams &&
               i.choiceOptionId === item.choiceOptionId &&
               JSON.stringify(i.additionalIds) === JSON.stringify(item.additionalIds),
      );
      if (existing) {
        return current.map((i) =>
          i === existing ? { ...i, quantity: i.quantity + item.quantity } : i,
        );
      }
      return [...current, item];
    });
  }

  removeItem(id: string): void {
    this.items.update((items) => items.filter((i) => i.id !== id));
  }

  updateQuantity(id: string, quantity: number): void {
    if (quantity <= 0) {
      this.removeItem(id);
      return;
    }
    this.items.update((items) =>
      items.map((i) => (i.id === id ? { ...i, quantity } : i)),
    );
  }

  clear(): void {
    this.items.set([]);
  }

  /**
   * Converte os itens do carrinho no payload de checkout — apenas ids/seleções.
   * O preço é sempre recomputado no backend (regra de negócio no servidor).
   */
  toCheckoutItems(): CheckoutItemRequest[] {
    return this.items().map((item) => ({
      productId: item.productId,
      quantity: item.quantity,
      notes: item.notes,
      variationId: item.variationId,
      weightGrams: item.weightGrams,
      choiceOptionId: item.choiceOptionId,
      additionalIds: item.additionalIds,
      optionGroups: item.optionGroups?.map((g) => ({
        groupId: g.groupId,
        itemIds: g.itemIds,
      })),
    }));
  }

  clearAll(): void {
    this.items.set([]);
    this.storeId.set(null);
    this.storeName.set(null);
    this.storeLogoUrl.set(null);
    localStorage.removeItem(STORAGE_KEY);
    localStorage.removeItem(STORE_KEY);
  }

  private persist(): void {
    const data: PersistedCart = {
      storeId: this.storeId(),
      storeName: this.storeName(),
      storeLogoUrl: this.storeLogoUrl(),
      items: this.items(),
    };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
  }

  private load(): void {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return;
      const data: PersistedCart = JSON.parse(raw);
      this.storeId.set(data.storeId ?? null);
      this.storeName.set(data.storeName ?? null);
      this.storeLogoUrl.set(data.storeLogoUrl ?? null);
      this.items.set(data.items ?? []);
    } catch {
      // ignore
    }
  }
}
