import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { finalize, Subscription } from 'rxjs';

import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { PaymentService } from '../../core/services/payment.service';
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

type PaymentChoice = 'pix' | 'receive';

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
  private readonly payments = inject(PaymentService);
  private readonly tracking = inject(CustomerOrderTrackingService);
  private readonly addresses = inject(AddressService);
  private readonly storeService = inject(StoreService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  private destroyed = false;
  private readonly subscriptions = new Subscription();

  readonly selected = signal<PaymentChoice | null>('receive');
  readonly showDetailsModal = signal(false);
  readonly processing = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly deliveryFee = signal(0);
  readonly freeShippingApplied = signal(false);
  readonly checkoutError = signal(false);
  readonly deliveryCoverageModalOpen = signal(false);
  readonly storeAddress = signal<{ city?: string; state?: string; zipCode?: string; neighborhood?: string } | null>(null);
  readonly customerAddress = signal<{ city?: string; state?: string; cep?: string; neighborhood?: string } | null>(null);
  readonly subtotal = this.cart.subtotal;
  readonly effectiveDeliveryFee = computed(() =>
    this.checkout.fulfillmentType() === FulfillmentType.Delivery ? this.deliveryFee() : 0,
  );
  readonly discount = signal(0);
  readonly total = computed(
    () => this.subtotal() + this.effectiveDeliveryFee() - this.discount(),
  );

  readonly orderRef = computed(() => {
    const id = this.checkout.lastOrderId();
    return id ? `Pedido #${id.slice(-5).toUpperCase()}` : 'Novo pedido';
  });

  readonly actionLabel = computed(() => {
    if (this.processing()) return 'Processando...';
    if (this.selected() === 'pix' && this.checkout.lastOrderId()) return 'Tentar novamente';
    return 'Continuar';
  });

  ngOnInit(): void {
    const storeId = this.cart.storeId();
    if (!storeId) {
      this.location.back();
      return;
    }
    this.loadDeliveryAddresses(storeId);
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
        },
        error: (err) => {
          if (this.destroyed) return;
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
    this.errorMessage.set(null);
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
    const sel = this.selected();
    const storeId = this.cart.storeId();
    if (!sel || !storeId || this.processing()) return;

    if (sel === 'pix' && this.checkout.lastOrderId()) {
      this.retryPixPayment();
      return;
    }

    const storePath = getStorePathFromUrl(this.router);
    const paymentMethod = sel === 'pix' ? PaymentMethod.PixOnline : PaymentMethod.CashOnDelivery;

    this.processing.set(true);
    this.errorMessage.set(null);
    this.subscriptions.add(this.checkout
      .confirm({
        storeId,
        fulfillmentType: this.checkout.fulfillmentType(),
        customerAddressId: this.checkout.customerAddressId() ?? undefined,
        paymentMethod,
        notes: this.checkout.orderNotes(),
        items: this.cart.toCheckoutItems(),
      })
      .subscribe({
        next: (order) => {
          if (this.destroyed) return;
          this.checkout.lastOrderId.set(order.orderId);
          this.checkout.lastOrderCode.set(order.code);

          if (paymentMethod === PaymentMethod.PixOnline) {
            this.startPixPayment(order.orderId, storePath);
            return;
          }

          this.tracking.trackOrder(order.orderId);
          this.cart.clear();
          this.processing.set(false);
          this.toast.showSuccess('Pedido enviado para a loja.');
          this.router.navigate(['/', storePath, 'pedido', order.orderId]);
        },
        error: (err) => {
          if (this.destroyed) return;
          this.processing.set(false);
          if (this.isDeliveryAreaError(err)) {
            this.deliveryCoverageModalOpen.set(true);
            return;
          }
          this.errorMessage.set('Não foi possível criar o pedido. Tente novamente.');
          this.toast.showError('Não foi possível criar o pedido. Tente novamente.');
        },
      }));
  }

  private startPixPayment(orderId: string, storePath: string): void {
    this.processing.set(true);
    this.errorMessage.set(null);
    this.subscriptions.add(this.payments.createPayment(orderId)
      .pipe(finalize(() => {
        if (!this.destroyed) this.processing.set(false);
      }))
      .subscribe({
        next: () => {
          if (this.destroyed) return;
          this.router.navigate(['/', storePath, 'checkout', 'pagar']);
        },
        error: () => {
          if (this.destroyed) return;
          this.errorMessage.set('Pedido criado, mas o Pix ainda não foi iniciado. Tente novamente em instantes.');
          this.toast.showError('Não foi possível iniciar o Pix.');
        },
      }));
  }

  retryPixPayment(): void {
    const orderId = this.checkout.lastOrderId();
    if (!orderId || this.processing()) return;
    const storePath = getStorePathFromUrl(this.router);
    this.startPixPayment(orderId, storePath);
  }

  get itemsCount(): number {
    return this.cart.totalItems();
  }
}
