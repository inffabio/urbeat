import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';

import { CartService } from '../../../core/services/cart.service';
import { CheckoutService } from '../../../core/services/checkout.service';
import { ToastService } from '../../../core/services/toast.service';
import { PaymentMethod } from '../../../shared/enums/payment-method.enum';
import { FulfillmentType } from '../../../shared/enums/fulfillment-type.enum';
import { BrlCurrencyPipe } from '../../../shared/pipes/brl-currency.pipe';
import { BackToMenuLinkComponent } from '../../../shared/components/back-to-menu-link/back-to-menu-link.component';

type DeliveryPay = 'cash' | 'card';

@Component({
  selector: 'app-delivery-payment-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, BrlCurrencyPipe, BackToMenuLinkComponent],
  templateUrl: './delivery-payment-page.component.html',
  styleUrl: './delivery-payment-page.component.scss',
})
export class DeliveryPaymentPageComponent implements OnInit {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly toast = inject(ToastService);

  // Inicia com "Cartão" pré-selecionado (forma mais segura/usual)
  readonly selected = signal<DeliveryPay | null>('card');
  readonly needsChange = signal<boolean | null>(null);
  readonly changeFor = signal<string>('');
  readonly cardPreference = signal<'credit' | 'debit' | 'any'>('any');
  readonly processing = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly subtotal = this.cart.subtotal;
  readonly deliveryFee = signal(6.99);
  readonly discount = signal(0);
  readonly effectiveDeliveryFee = computed(() =>
    this.checkout.fulfillmentType() === FulfillmentType.Delivery ? this.deliveryFee() : 0,
  );
  readonly total = computed(
    () => this.subtotal() + this.effectiveDeliveryFee() - this.discount(),
  );
  readonly itemsCount = computed(() => this.cart.totalItems());

  readonly canFinalize = computed(() => {
    if (!this.selected()) return false;
    if (this.selected() === 'cash' && this.needsChange() === true) {
      const value = parseFloat(this.changeFor().replace(',', '.'));
      if (Number.isNaN(value) || value < this.total()) return false;
    }
    return true;
  });

  ngOnInit(): void {
    // Pre-set fulfillmentType
  }

  select(s: DeliveryPay): void {
    this.selected.set(s);
    if (s !== 'cash') {
      this.needsChange.set(null);
      this.changeFor.set('');
    }
  }

  setNeedsChange(v: boolean): void {
    this.needsChange.set(v);
    if (!v) this.changeFor.set('');
  }

  onChangeForInput(v: string): void {
    const digits = v.replace(/\D/g, '');
    if (!digits) {
      this.changeFor.set('');
      return;
    }
    const num = parseFloat(digits) / 100;
    this.changeFor.set(num.toFixed(2).replace('.', ','));
  }

  onBack(): void {
    this.location.back();
  }

  goToMenu(): void {
    this.router.navigate(['/', this.getStorePath()]);
  }

  finalize(): void {
    if (!this.canFinalize() || this.processing()) return;

    const method =
      this.selected() === 'cash'
        ? PaymentMethod.CashOnDelivery
        : PaymentMethod.CardOnDelivery;

    let notes = `Pagamento: ${this.selected() === 'cash' ? 'dinheiro' : 'cartão'} na entrega.`;
    if (this.selected() === 'cash') {
      if (this.needsChange() && this.changeFor()) {
        notes += ` Precisa de troco para R$ ${this.changeFor()}.`;
      } else {
        notes += ' Não precisa de troco.';
      }
    } else if (this.selected() === 'card' && this.cardPreference() !== 'any') {
      notes += ` Preferência: ${this.cardPreference() === 'credit' ? 'crédito' : 'débito'}.`;
    }

    this.processing.set(true);
    this.errorMessage.set(null);

    const storeId = this.cart.storeId();
    if (!storeId) {
      this.processing.set(false);
      return;
    }

    this.checkout
      .confirm({
        storeId,
        fulfillmentType: this.checkout.fulfillmentType(),
        customerAddressId: this.checkout.customerAddressId() ?? undefined,
        paymentMethod: method,
        notes,
        items: this.cart.toCheckoutItems(),
      })
      .subscribe({
        next: (order) => {
          this.checkout.lastOrderId.set(order.orderId);
          this.checkout.lastOrderCode.set(order.code);
          this.cart.clear();
          this.processing.set(false);
          this.toast.showSuccess('Pedido criado com sucesso!');
          this.router.navigate(['/', this.getStorePath(), 'pedido', order.orderId]);
        },
        error: () => {
          this.errorMessage.set('Não foi possível criar o pedido. Tente novamente.');
          this.toast.showError('Não foi possível criar o pedido. Tente novamente.');
          this.processing.set(false);
        },
      });
  }

  private getStorePath(): string {
    const m = this.router.url.match(/^\/([^/]+)\//);
    return m?.[1] ?? '';
  }
}
