import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';

import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { StoreService } from '../../core/services/store.service';
import { AuthService } from '../../core/services/auth.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { StorePublicDetails } from '../../shared/models/store.model';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { BackToMenuLinkComponent } from '../../shared/components/back-to-menu-link/back-to-menu-link.component';

@Component({
  selector: 'app-cart-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, BackToMenuLinkComponent],
  templateUrl: './cart-page.component.html',
  styleUrl: './cart-page.component.scss',
})
export class CartPageComponent implements OnInit, OnDestroy {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly storeService = inject(StoreService);
  private readonly auth = inject(AuthService);

  readonly FulfillmentType = FulfillmentType;
  readonly deliveryFee = signal(0);
  readonly minimumOrderValue = signal(15);
  readonly discount = signal(0);
  readonly freeShippingApplied = signal(false);
  readonly showClearConfirm = signal(false);
  readonly store = signal<StorePublicDetails | null>(null);
  readonly storeLoadError = signal(false);
  readonly checkoutPreviewError = signal(false);
  readonly dataLoading = signal(false);
  private statusRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  private storeId = '';

  readonly fulfillment = computed(() => this.checkout.fulfillmentType());
  readonly subtotal = this.cart.subtotal;

  // No carrinho a região ainda é desconhecida (sem endereço) → frete calculado na próxima etapa.
  readonly deliveryFeePending = computed(
    () => this.fulfillment() === FulfillmentType.Delivery && !this.freeShippingApplied(),
  );
  readonly effectiveDeliveryFee = computed(() =>
    this.fulfillment() === FulfillmentType.Delivery ? this.deliveryFee() : 0,
  );
  readonly total = computed(
    () => this.subtotal() + this.effectiveDeliveryFee() - this.discount(),
  );
  readonly belowMinimum = computed(
    () =>
      this.fulfillment() === FulfillmentType.Delivery &&
      this.subtotal() < this.minimumOrderValue(),
  );

  readonly storeOpen = computed(() => this.store()?.isOpenNow ?? true);
  readonly freeShippingThreshold = computed(() => this.store()?.freeShippingThreshold ?? null);

  readonly supportsDelivery = computed(() => this.store()?.supportsDelivery ?? true);
  readonly supportsPickup = computed(() => this.store()?.supportsPickup ?? true);

  readonly etaDelivery = computed(() => {
    const s = this.store();
    if (!s?.isOpenNow) return '';
    const min = s.initialMinute;
    const max = s.finalMinute;
    if (min != null && max != null) return `${min}-${max} min`;
    if (min != null) return `a partir de ${min} min`;
    return '';
  });

  readonly etaPickup = computed(() => {
    const s = this.store();
    if (!s?.isOpenNow) return '';
    const min = s.initialMinute;
    if (min != null) {
      const pickupMin = Math.max(5, Math.floor(min * 0.6));
      return `${pickupMin}-${min} min`;
    }
    return '';
  });

  ngOnInit(): void {
    if (this.cart.isEmpty()) return;
    const storeId = this.cart.storeId();
    if (!storeId) return;
    this.storeId = storeId;

    this.loadStore(storeId);
    this.loadCheckoutPreview(storeId);
    this.restoreCustomerSession();
  }

  ngOnDestroy(): void {
    this.clearStatusRefreshTimer();
  }

  private loadStore(storeId: string): void {
    this.storeLoadError.set(false);
    this.dataLoading.set(true);
    this.storeService.getStoreById(storeId).subscribe({
      next: (store) => {
        this.store.set(store);
        this.scheduleStatusRefresh(store);
        this.storeLoadError.set(false);
        if (!store.supportsDelivery && this.fulfillment() === FulfillmentType.Delivery) {
          this.checkout.fulfillmentType.set(
            store.supportsPickup ? FulfillmentType.PickUp : FulfillmentType.Delivery,
          );
        } else if (!store.supportsPickup && this.fulfillment() === FulfillmentType.PickUp) {
          this.checkout.fulfillmentType.set(FulfillmentType.Delivery);
        }
        this.dataLoading.set(false);
      },
      error: () => {
        this.storeLoadError.set(true);
        this.dataLoading.set(false);
      },
    });
  }

  private loadCheckoutPreview(storeId: string): void {
    this.checkoutPreviewError.set(false);
    this.checkout
      .preview({
        storeId,
        fulfillmentType: this.fulfillment(),
        items: this.cart.toCheckoutItems(),
      })
      .subscribe({
        next: (res) => {
          this.deliveryFee.set(res.deliveryFee);
          this.minimumOrderValue.set(res.minimumOrderValue);
          this.freeShippingApplied.set(res.freeShippingApplied);
          this.checkoutPreviewError.set(false);
        },
        error: (err) => {
          const summary = err?.error?.summary;
          if (summary) {
            this.deliveryFee.set(summary.deliveryFee ?? 0);
            this.minimumOrderValue.set(summary.minimumOrderValue ?? this.minimumOrderValue());
            this.freeShippingApplied.set(summary.freeShippingApplied ?? false);
            this.checkoutPreviewError.set(false);
            return;
          }

          this.checkoutPreviewError.set(true);
        },
      });
  }

  retryLoad(): void {
    const storeId = this.cart.storeId();
    if (!storeId) return;
    this.loadStore(storeId);
    this.loadCheckoutPreview(storeId);
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
    if (!this.storeId) return;

    this.storeService.getStoreById(this.storeId).subscribe({
      next: (store) => {
        this.store.set(store);
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

  onImageError(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.src = 'data:image/svg+xml,' + encodeURIComponent(
      '<svg xmlns="http://www.w3.org/2000/svg" width="112" height="112" fill="%23eadfd6"><rect width="112" height="112" rx="12"/><text x="56" y="60" text-anchor="middle" fill="%236f6f76" font-size="28" font-family="sans-serif">🍽</text></svg>',
    );
  }

  inc(id: string): void {
    const item = this.cart.items().find((i) => i.id === id);
    if (item) this.cart.updateQuantity(id, item.quantity + 1);
  }

  dec(id: string): void {
    const item = this.cart.items().find((i) => i.id === id);
    if (!item) return;
    this.cart.updateQuantity(id, item.quantity - 1);
  }

  remove(id: string): void {
    this.cart.removeItem(id);
  }

  setFulfillment(type: FulfillmentType): void {
    if (type === FulfillmentType.Delivery && !this.supportsDelivery()) return;
    if (type === FulfillmentType.PickUp && !this.supportsPickup()) return;
    this.checkout.fulfillmentType.set(type);
  }

  onBack(): void {
    this.location.back();
  }

  goToMenu(): void {
    this.router.navigate(['/', this.cart.storeName() ? this.guessStorePath() : '']);
  }

  private guessStorePath(): string {
    const routeStorePath = this.route.parent?.snapshot.paramMap.get('storePath');
    if (routeStorePath) return routeStorePath;

    const url = this.router.url;
    const m = url.match(/^\/([^/]+)\//);
    return m?.[1] ?? '';
  }

  continueCheckout(): void {
    if (this.cart.isEmpty() || this.belowMinimum() || !this.storeOpen()) return;
    const restoredAddressId = this.auth.customerProfile()?.primaryAddressId ?? null;
    if (!this.checkout.customerAddressId() && restoredAddressId) {
      this.checkout.customerAddressId.set(restoredAddressId);
    }

    const nextStep = this.auth.isLoggedIn() && this.checkout.customerAddressId() ? 'pagamento' : 'cadastro';
    this.router.navigate(['/', this.guessStorePath(), 'checkout', nextStep]);
  }

  private restoreCustomerSession(): void {
    if (this.auth.isLoggedIn() && this.auth.customerProfile()) {
      const addressId = this.auth.customerProfile()?.primaryAddressId ?? null;
      if (addressId) this.checkout.customerAddressId.set(addressId);
      return;
    }

    this.auth.restoreCustomerSession().subscribe({
      next: (profile) => {
        if (profile?.primaryAddressId) this.checkout.customerAddressId.set(profile.primaryAddressId);
      },
      error: () => undefined,
    });
  }

  promptClear(): void {
    this.showClearConfirm.set(true);
  }

  confirmClear(): void {
    this.cart.clear();
    this.showClearConfirm.set(false);
  }

  cancelClear(): void {
    this.showClearConfirm.set(false);
  }
}
