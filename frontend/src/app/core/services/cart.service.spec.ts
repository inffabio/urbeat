import { TestBed } from '@angular/core/testing';

import { CartService } from './cart.service';
import { CartItem } from '../../shared/models/cart-item.model';

const CART_KEY = 'urbeat_cart';
const LEGACY_STORE_KEY = 'urbeat_cart_store';

function createMemoryStorage(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key: string) => (store.has(key) ? store.get(key)! : null),
    key: (index: number) => Array.from(store.keys())[index] ?? null,
    removeItem: (key: string) => {
      store.delete(key);
    },
    setItem: (key: string, value: string) => {
      store.set(key, String(value));
    },
  } as Storage;
}

function cartItem(overrides: Partial<CartItem> = {}): CartItem {
  return {
    id: 'i1',
    productId: 'p1',
    productName: 'X-burguer',
    quantity: 1,
    unitPrice: 10,
    ...overrides,
  };
}

function createCart(): CartService {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({ providers: [CartService] });
  return TestBed.inject(CartService);
}

describe('CartService tab-scoped persistence', () => {
  let originalSessionStorage: Storage;

  beforeEach(() => {
    originalSessionStorage = window.sessionStorage;
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(() => {
    Object.defineProperty(window, 'sessionStorage', { configurable: true, value: originalSessionStorage });
    localStorage.clear();
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('persists the cart in sessionStorage so it survives a same-tab reload', () => {
    const cart = createCart();
    cart.setStore('store-1', 'Loja 1', 'logo.png');
    cart.addItem(cartItem());
    TestBed.flushEffects();

    expect(sessionStorage.getItem(CART_KEY)).not.toBeNull();

    const reloaded = createCart();

    expect(reloaded.storeId()).toBe('store-1');
    expect(reloaded.storeName()).toBe('Loja 1');
    expect(reloaded.storeLogoUrl()).toBe('logo.png');
    expect(reloaded.items().map((item) => item.id)).toEqual(['i1']);
  });

  it('does not write cart state to shared localStorage', () => {
    const cart = createCart();
    cart.setStore('store-1', 'Loja 1', '');
    cart.addItem(cartItem());
    TestBed.flushEffects();

    expect(localStorage.getItem(CART_KEY)).toBeNull();
    expect(localStorage.getItem(LEGACY_STORE_KEY)).toBeNull();
  });

  it('ignores and removes legacy localStorage cart data instead of migrating it', () => {
    localStorage.setItem(
      CART_KEY,
      JSON.stringify({
        storeId: 'legacy-store',
        storeName: 'Loja Legada',
        storeLogoUrl: null,
        items: [cartItem({ id: 'legacy-item', productId: 'legacy-product' })],
      }),
    );
    localStorage.setItem(LEGACY_STORE_KEY, JSON.stringify({ storeId: 'legacy-store' }));

    const cart = createCart();

    expect(cart.items()).toEqual([]);
    expect(cart.storeId()).toBeNull();
    expect(localStorage.getItem(CART_KEY)).toBeNull();
    expect(localStorage.getItem(LEGACY_STORE_KEY)).toBeNull();
  });

  it('assigns stable distinct ids to persisted items missing one so they can be removed', () => {
    sessionStorage.setItem(
      CART_KEY,
      JSON.stringify({
        storeId: 'store-1',
        storeName: 'Loja 1',
        storeLogoUrl: null,
        items: [
          { productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 10 },
          { productId: 'p2', productName: 'Suco', quantity: 1, unitPrice: 5 },
        ],
      }),
    );

    const cart = createCart();
    const [first, second] = cart.items();

    expect(first.id).toBeTruthy();
    expect(second.id).toBeTruthy();
    expect(first.id).not.toBe(second.id);

    cart.removeItem(first);

    expect(cart.items().map((item) => item.productId)).toEqual(['p2']);
  });

  it('keeps existing persisted ids untouched while filling only the missing ones', () => {
    sessionStorage.setItem(
      CART_KEY,
      JSON.stringify({
        storeId: 'store-1',
        storeName: 'Loja 1',
        storeLogoUrl: null,
        items: [
          { id: 'keep-me', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 10 },
          { productId: 'p2', productName: 'Suco', quantity: 1, unitPrice: 5 },
        ],
      }),
    );

    const cart = createCart();
    const [first, second] = cart.items();

    expect(first.id).toBe('keep-me');
    expect(second.id).toBeTruthy();
    expect(second.id).not.toBe('keep-me');
  });

  it('replaces duplicate persisted ids with fresh distinct ids while preserving unique ones', () => {
    sessionStorage.setItem(
      CART_KEY,
      JSON.stringify({
        storeId: 'store-1',
        storeName: 'Loja 1',
        storeLogoUrl: null,
        items: [
          { id: 'dup', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 10 },
          { id: 'dup', productId: 'p2', productName: 'Suco', quantity: 1, unitPrice: 5 },
          { id: 'keep-me', productId: 'p3', productName: 'Batata', quantity: 1, unitPrice: 8 },
          { productId: 'p4', productName: 'Refri', quantity: 1, unitPrice: 6 },
        ],
      }),
    );

    const cart = createCart();
    const ids = cart.items().map((item) => item.id);

    expect(ids[0]).toBe('dup');
    expect(ids[1]).toBeTruthy();
    expect(ids[1]).not.toBe('dup');
    expect(ids[2]).toBe('keep-me');
    expect(ids[3]).toBeTruthy();
    expect(new Set(ids).size).toBe(4);

    cart.removeItem('dup');

    expect(cart.items().map((item) => item.productId)).toEqual(['p2', 'p3', 'p4']);
  });

  it('keeps two tabs of different stores isolated even though they share the cart key', () => {
    const tabAStorage = sessionStorage;
    const cartA = createCart();
    cartA.setStore('store-a', 'Loja A', '');
    cartA.addItem(cartItem({ id: 'a1', productId: 'pa' }));
    TestBed.flushEffects();

    const tabBStorage = createMemoryStorage();
    Object.defineProperty(window, 'sessionStorage', { configurable: true, value: tabBStorage });
    const cartB = createCart();

    expect(cartB.items()).toEqual([]);
    expect(cartB.storeId()).toBeNull();

    cartB.setStore('store-b', 'Loja B', '');
    cartB.addItem(cartItem({ id: 'b1', productId: 'pb' }));
    TestBed.flushEffects();

    expect(tabAStorage.getItem(CART_KEY)).toContain('store-a');
    expect(tabAStorage.getItem(CART_KEY)).not.toContain('store-b');
    expect(tabBStorage.getItem(CART_KEY)).toContain('store-b');
    expect(tabBStorage.getItem(CART_KEY)).not.toContain('store-a');

    Object.defineProperty(window, 'sessionStorage', { configurable: true, value: tabAStorage });
    const reloadedA = createCart();

    expect(reloadedA.storeId()).toBe('store-a');
    expect(reloadedA.items().map((item) => item.id)).toEqual(['a1']);
  });

  it('clearAll removes the tab-scoped cart without touching shared localStorage', () => {
    const cart = createCart();
    cart.setStore('store-1', 'Loja 1', '');
    cart.addItem(cartItem());
    TestBed.flushEffects();
    expect(sessionStorage.getItem(CART_KEY)).not.toBeNull();

    cart.clearAll();

    expect(sessionStorage.getItem(CART_KEY)).toBeNull();
    expect(localStorage.getItem(CART_KEY)).toBeNull();
  });
});
