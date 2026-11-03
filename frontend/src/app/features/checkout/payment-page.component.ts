import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';

import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { AddressService } from '../../core/services/address.service';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { getStorePathFromUrl } from '../../shared/utils/router.utils';
import { StickyActionBarComponent } from '../../shared/components/sticky-action-bar/sticky-action-bar.component';
import { DeliveryCoverageModalComponent } from '../../shared/components/delivery-coverage-modal/delivery-coverage-modal.component';

/** Storefront choices: online Pix is no longer offered for new orders. */
type PaymentChoice = 'pix-machine' | 'cash' | 'debit' | 'credit';

@Component({
  selector: 'app-payment-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, StickyActionBarComponent, DeliveryCoverageModalComponent],
  templateUrl: './payment-page.component.html',
  styleUrl: './payment-page.component.scss',
})
export class PaymentPageComponent implements OnInit, OnDestroy {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly tracking = inject(CustomerOrderTrackingService);
  private readonly addresses = inject(AddressService);
  private readonly storeService = inject(StoreService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  private destroyed = false;
  private readonly subscriptions = new Subscription();

  readonly selected = signal<PaymentChoice | null>(null);
  /** null = not answered yet, true = customer needs change, false = exact amount. */
  readonly cashNeedsChange = signal<boolean | null>(null);
  readonly cashChangeFor = signal('');
  readonly showDetailsModal = signal(false);
  readonly processing = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly deliveryFee = signal(0);
  readonly freeShippingApplied = signal(false);
  readonly checkoutError = signal(false);
  readonly previewLoading = signal(true);
  readonly previewReady = signal(false);
  readonly previewError = signal(false);
  readonly authoritativeTotal = signal<number | null>(null);
  readonly deliveryCoverageModalOpen = signal(false);
  readonly storeAddress = signal<{ city?: string; state?: string; zipCode?: string; neighborhood?: string } | null>(null);
  readonly customerAddress = signal<{ city?: string; state?: string; cep?: string; neighborhood?: string } | null>(null);
  readonly subtotal = this.cart.subtotal;
  readonly effectiveDeliveryFee = computed(() =>
    this.checkout.fulfillmentType() === FulfillmentType.Delivery ? this.deliveryFee() : 0,
  );
  readonly discount = signal(0);
  readonly total = computed(
    () => this.authoritativeTotal() ?? (this.subtotal() + this.effectiveDeliveryFee() - this.discount()),
  );

  /** Parsed change-for amount, in currency precision. */
  readonly changeForValue = computed<number | null>(() => {
    const raw = this.cashChangeFor().trim();
    if (!raw) return null;
    const normalized = raw.replace(/\./g, '').replace(',', '.');
    const parsed = Number.parseFloat(normalized);
    return Number.isFinite(parsed) ? parsed : null;
  });

  readonly canContinue = computed(() => {
    const choice = this.selected();
    if (!choice || this.processing()) return false;
    if (!this.previewReady() || this.previewLoading() || this.previewError()) return false;
    if (choice !== 'cash') return true;

    const needsChange = this.cashNeedsChange();
    if (needsChange === null) return false;
    if (needsChange === false) return true;

    const value = this.changeForValue();
    return value !== null && value >= this.total();
  });

  readonly actionLabel = computed(() => {
    if (this.previewLoading()) return 'Calculando...';
    if (this.processing()) return 'Processando...';
    return 'Continuar';
  });

  readonly orderRef = computed(() => {
    const id = this.checkout.lastOrderId();
    return id ? `Pedido #${id.slice(-5).toUpperCase()}` : 'Novo pedido';
  });

  ngOnInit(): void {
    const storeId = this.cart.storeId();
    if (!storeId) {
      this.location.back();
      return;
    }
    this.loadDeliveryAddresses(storeId);
    this.loadPreview(storeId);
  }

  retryPreview(): void {
    const storeId = this.cart.storeId();
    if (storeId) this.loadPreview(storeId);
  }

  private loadPreview(storeId: string): void {
    this.previewLoading.set(true);
    this.previewReady.set(false);
    this.previewError.set(false);
    this.checkoutError.set(false);

    this.subscriptions.add(this.checkout
      .preview({
        storeId,
        fulfillmentType: this.checkout.fulfillmentType(),
        customerAddressId: this.checkout.customerAddressId() ?? undefined,
        items: this.cart.toCheckoutItems(),
      })
      .subscribe({
        next: (res) => {
          if (this.destroyed) return;
          this.deliveryFee.set(res.deliveryFee);
          this.freeShippingApplied.set(res.freeShippingApplied);
          // The preview total is authoritative and already includes fees and free shipping.
          this.authoritativeTotal.set(res.total);
          this.previewLoading.set(false);
          this.previewReady.set(true);
        },
        error: (err) => {
          if (this.destroyed) return;
          this.previewLoading.set(false);
          this.previewReady.set(false);
          this.previewError.set(true);
          if (this.isDeliveryAreaError(err)) {
            this.deliveryCoverageModalOpen.set(true);
            return;
          }
          this.checkoutError.set(true);
        },
      }));
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.subscriptions.unsubscribe();
  }

  select(category: PaymentChoice): void {
    this.selected.set(category);
    if (category !== 'cash') {
      this.cashNeedsChange.set(null);
      this.cashChangeFor.set('');
    }
    this.errorMessage.set(null);
  }

  setCashNeedsChange(value: boolean): void {
    this.cashNeedsChange.set(value);
    if (!value) this.cashChangeFor.set('');
    this.errorMessage.set(null);
  }

  onChangeForInput(value: string): void {
    const digits = value.replace(/\D/g, '');
    if (!digits) {
      this.cashChangeFor.set('');
      return;
    }
    const amount = Number.parseInt(digits, 10) / 100;
    this.cashChangeFor.set(amount.toFixed(2).replace('.', ','));
  }

  onBack(): void {
    this.location.back();
  }

  openDetails(): void {
    this.showDetailsModal.set(true);
  }

  closeDetails(): void {
    this.showDetailsModal.set(false);
  }

  closeDeliveryCoverageModal(): void {
    this.deliveryCoverageModalOpen.set(false);
  }

  private loadDeliveryAddresses(storeId: string): void {
    this.subscriptions.add(this.storeService.getStoreById(storeId).subscribe({
      next: (store) => {
        if (this.destroyed) return;
        this.storeAddress.set(store.address ?? null);
      },
      error: () => {
        if (this.destroyed) return;
        this.storeAddress.set(null);
      },
    }));

    const currentAddress = this.checkout.customerAddress();
    if (currentAddress) {
      this.customerAddress.set(currentAddress);
      return;
    }

    const addressId = this.checkout.customerAddressId();
    if (!addressId) return;
    this.subscriptions.add(this.addresses.list().subscribe({
      next: (addresses) => {
        if (this.destroyed) return;
        this.customerAddress.set(addresses.find((address) => address.id === addressId) ?? null);
      },
    }));
  }

  private handleTotalChanged(error: unknown): boolean {
    const response = (error as { status?: number; error?: unknown })?.error;
    const body = response && typeof response === 'object'
      ? response as { error?: unknown; detail?: unknown; total?: unknown; summary?: unknown }
      : null;
    const status = (error as { status?: number })?.status;
    if (status !== 409 || !body || typeof body.total !== 'number') {
      return false;
    }

    const summary = body.summary && typeof body.summary === 'object'
      ? body.summary as {
          deliveryFee?: number;
          freeShippingApplied?: boolean;
          total?: number;
        }
      : null;

    if (summary) {
      if (typeof summary.deliveryFee === 'number') this.deliveryFee.set(summary.deliveryFee);
      if (typeof summary.freeShippingApplied === 'boolean') {
        this.freeShippingApplied.set(summary.freeShippingApplied);
      }
    }
    this.authoritativeTotal.set(summary?.total ?? body.total);
    this.previewReady.set(true);
    this.previewLoading.set(false);
    this.previewError.set(false);

    const message = typeof body.error === 'string' && body.error
      ? body.error
      : typeof body.detail === 'string' && body.detail
        ? body.detail
        : 'O total do pedido mudou. Confira o novo valor e confirme novamente.';
    this.errorMessage.set(message);
    this.toast.showError(message);
    return true;
  }

  private isDeliveryAreaError(error: unknown): boolean {
    const responseError = (error as { error?: unknown })?.error;
    const message = typeof responseError === 'string'
      ? responseError
      : (responseError as { error?: string; message?: string; detail?: string } | undefined)?.error
        ?? (responseError as { message?: string; detail?: string } | undefined)?.message
        ?? (responseError as { detail?: string } | undefined)?.detail
        ?? '';
    return /entregamos.*bairro/i.test(message);
  }

  goToMenu(): void {
    this.router.navigate(['/', getStorePathFromUrl(this.router)]);
  }

  continue(): void {
    const choice = this.selected();
    const storeId = this.cart.storeId();
    if (!choice || !storeId || this.processing() || !this.canContinue()) return;

    const paymentMethod = this.mapPaymentMethod(choice);
    const isCash = choice === 'cash';
    const needsChange = this.cashNeedsChange();
    const changeFor = this.changeForValue();

    this.processing.set(true);
    this.errorMessage.set(null);

    const storePath = getStorePathFromUrl(this.router);
    this.subscriptions.add(this.checkout
      .confirm({
        storeId,
        fulfillmentType: this.checkout.fulfillmentType(),
        customerAddressId: this.checkout.customerAddressId() ?? undefined,
        paymentMethod,
        expectedTotal: this.total(),
        cashWithoutChange: isCash ? needsChange === false : undefined,
        cashChangeForAmount: isCash && needsChange === true ? changeFor ?? undefined : undefined,
        notes: this.checkout.orderNotes(),
        items: this.cart.toCheckoutItems(),
      })
      .subscribe({
        next: (order) => {
          if (this.destroyed) return;
          this.checkout.lastOrderId.set(order.orderId);
          this.checkout.lastOrderCode.set(order.code);
          this.tracking.trackOrder(order.orderId);
          this.cart.clear();
          this.processing.set(false);
          this.toast.showSuccess('Pedido enviado para a loja.');
          this.router.navigate(['/', storePath, 'pedido', order.orderId]);
        },
        error: (err) => {
          if (this.destroyed) return;
          this.processing.set(false);
          if (this.handleTotalChanged(err)) {
            return;
          }
          if (this.isDeliveryAreaError(err)) {
            this.deliveryCoverageModalOpen.set(true);
            return;
          }
          const message = this.extractErrorMessage(err) ?? 'Não foi possível criar o pedido. Tente novamente.';
          this.errorMessage.set(message);
          this.toast.showError(message);
        },
      }));
  }

  get itemsCount(): number {
    return this.cart.totalItems();
  }

  private mapPaymentMethod(choice: PaymentChoice): PaymentMethod {
    switch (choice) {
      case 'pix-machine':
        return PaymentMethod.PixOnDelivery;
      case 'debit':
        return PaymentMethod.DebitOnDelivery;
      case 'credit':
        return PaymentMethod.CreditOnDelivery;
      case 'cash':
      default:
        return PaymentMethod.CashOnDelivery;
    }
  }

  private extractErrorMessage(error: unknown): string | null {    const response = (error as { error?: unknown })?.error;
    if (typeof response === 'string') return response;
    if (response && typeof response === 'object') {
      const candidate = response as { error?: unknown; message?: unknown; detail?: unknown };
      if (typeof candidate.error === 'string' && candidate.error) return candidate.error;
      if (typeof candidate.message === 'string' && candidate.message) return candidate.message;
      if (typeof candidate.detail === 'string' && candidate.detail) return candidate.detail;
    }
    return null;
  }
}
