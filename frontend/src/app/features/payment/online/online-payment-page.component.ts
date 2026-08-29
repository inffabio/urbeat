import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { Subscription, interval } from 'rxjs';

import { CartService } from '../../../core/services/cart.service';
import { CheckoutService } from '../../../core/services/checkout.service';
import { PaymentService } from '../../../core/services/payment.service';
import { OrderService } from '../../../core/services/order.service';
import { CustomerOrderTrackingService } from '../../../core/services/customer-order-tracking.service';
import { ToastService } from '../../../core/services/toast.service';
import { OrderStatus } from '../../../shared/enums/order-status.enum';
import { PaymentStatus } from '../../../shared/enums/payment-status.enum';
import { BrlCurrencyPipe } from '../../../shared/pipes/brl-currency.pipe';
import { PaymentResponse } from '../../../shared/models/payment.model';
import { StickyActionBarComponent } from '../../../shared/components/sticky-action-bar/sticky-action-bar.component';

@Component({
  selector: 'app-online-payment-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, StickyActionBarComponent],
  templateUrl: './online-payment-page.component.html',
  styleUrl: './online-payment-page.component.scss',
})
export class OnlinePaymentPageComponent implements OnInit, OnDestroy {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly payments = inject(PaymentService);
  private readonly orders = inject(OrderService);
  private readonly tracking = inject(CustomerOrderTrackingService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly toast = inject(ToastService);

  readonly payment = signal<PaymentResponse | null>(null);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  private pollSub?: Subscription;
  private loadPaymentSub?: Subscription;
  private orderPollSub?: Subscription;
  private orderPollInFlight = false;
  private destroyed = false;
  private navigatedToTracking = false;

  ngOnInit(): void {
    const orderId = this.checkout.lastOrderId();
    if (!orderId) {
      this.router.navigate(['/', this.getStorePath(), 'checkout', 'pagamento']);
      return;
    }

    this.loadPayment(orderId);
    this.startPolling(orderId);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.pollSub?.unsubscribe();
    this.loadPaymentSub?.unsubscribe();
    this.orderPollSub?.unsubscribe();
  }

  onBack(): void {
    this.location.back();
  }

  goToMenu(): void {
    this.router.navigate(['/', this.getStorePath()]);
  }

  refreshPayment(): void {
    const orderId = this.checkout.lastOrderId();
    if (orderId) this.loadPayment(orderId);
  }

  private loadPayment(orderId: string): void {
    this.loadPaymentSub?.unsubscribe();
    this.loading.set(true);
    this.errorMessage.set(null);
    this.loadPaymentSub = this.payments.getPayment(orderId).subscribe({
      next: (payment) => {
        if (this.destroyed) return;
        this.payment.set(payment);
        this.loading.set(false);
        if (payment.status === PaymentStatus.Paid) {
          this.goToTracking(orderId);
        }
      },
      error: () => {
        if (this.destroyed) return;
        this.loading.set(false);
        this.errorMessage.set('Não foi possível carregar o Pix. Tente atualizar em instantes.');
        this.toast.showError('Não foi possível carregar o Pix.');
      },
    });
  }

  private startPolling(orderId: string): void {
    if (this.destroyed || this.navigatedToTracking) return;
    this.pollSub = interval(4000).subscribe(() => {
      if (this.destroyed || this.orderPollInFlight) return;
      this.orderPollInFlight = true;
      this.orderPollSub = this.orders.getOrder(orderId).subscribe({
        next: (order) => {
          this.orderPollInFlight = false;
          if (this.destroyed) return;
          if (order.status >= OrderStatus.Received) {
            this.goToTracking(orderId);
          }
        },
        error: () => {
          this.orderPollInFlight = false;
        },
      });
    });
  }

  private cancelPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
    this.orderPollSub?.unsubscribe();
    this.orderPollSub = undefined;
    this.orderPollInFlight = false;
  }

  private goToTracking(orderId: string): void {
    if (this.navigatedToTracking) return;
    this.navigatedToTracking = true;
    this.cancelPolling();
    this.tracking.trackOrder(orderId);
    this.cart.clear();
    this.router.navigate(['/', this.getStorePath(), 'pedido', orderId]);
  }

  private getStorePath(): string {
    const m = this.router.url.match(/^\/([^/]+)\//);
    return m?.[1] ?? '';
  }
}
