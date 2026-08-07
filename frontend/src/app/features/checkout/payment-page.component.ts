import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { finalize } from 'rxjs';

import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { PaymentService } from '../../core/services/payment.service';
import { ToastService } from '../../core/services/toast.service';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { getStorePathFromUrl } from '../../shared/utils/router.utils';
import { BackToMenuLinkComponent } from '../../shared/components/back-to-menu-link/back-to-menu-link.component';

type PaymentChoice = 'pix' | 'receive';

@Component({
  selector: 'app-payment-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, BackToMenuLinkComponent],
  templateUrl: './payment-page.component.html',
  styleUrl: './payment-page.component.scss',
})
export class PaymentPageComponent implements OnInit {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly payments = inject(PaymentService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  readonly selected = signal<PaymentChoice | null>('receive');
  readonly showDetailsModal = signal(false);
  readonly processing = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly deliveryFee = signal(0);
  readonly freeShippingApplied = signal(false);
  readonly checkoutError = signal(false);
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

  ngOnInit(): void {
    const storeId = this.cart.storeId();
    if (!storeId) {
      this.location.back();
      return;
    }
    this.checkout
      .preview({
        storeId,
        fulfillmentType: this.checkout.fulfillmentType(),
        customerAddressId: this.checkout.customerAddressId() ?? undefined,
        items: this.cart.toCheckoutItems(),
      })
      .subscribe({
        next: (res) => {
          this.deliveryFee.set(res.deliveryFee);
          this.freeShippingApplied.set(res.freeShippingApplied);
        },
        error: () => {
          this.checkoutError.set(true);
        },
      });
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

  goToMenu(): void {
    this.router.navigate(['/', getStorePathFromUrl(this.router)]);
  }

  continue(): void {
    const sel = this.selected();
    const storeId = this.cart.storeId();
    if (!sel || !storeId || this.processing()) return;

    const storePath = getStorePathFromUrl(this.router);
    const paymentMethod = sel === 'pix' ? PaymentMethod.PixOnline : PaymentMethod.CashOnDelivery;

    this.processing.set(true);
    this.errorMessage.set(null);
    this.checkout
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
          this.checkout.lastOrderId.set(order.orderId);
          this.checkout.lastOrderCode.set(order.code);

          if (paymentMethod === PaymentMethod.PixOnline) {
            this.startPixPayment(order.orderId, storePath);
            return;
          }

          this.cart.clear();
          this.processing.set(false);
          this.toast.showSuccess('Pedido enviado para a loja.');
          this.router.navigate(['/', storePath, 'pedido', order.orderId]);
        },
        error: () => {
          this.processing.set(false);
          this.errorMessage.set('Não foi possível criar o pedido. Tente novamente.');
          this.toast.showError('Não foi possível criar o pedido. Tente novamente.');
        },
      });
  }

  private startPixPayment(orderId: string, storePath: string): void {
    this.processing.set(true);
    this.payments.createPayment(orderId)
      .pipe(finalize(() => this.processing.set(false)))
      .subscribe({
        next: () => {
          this.router.navigate(['/', storePath, 'checkout', 'pagar']);
        },
        error: () => {
          this.errorMessage.set('Pedido criado, mas o Pix ainda não foi iniciado. Tente novamente em instantes.');
          this.toast.showError('Não foi possível iniciar o Pix.');
        },
      });
  }

  get itemsCount(): number {
    return this.cart.totalItems();
  }
}
