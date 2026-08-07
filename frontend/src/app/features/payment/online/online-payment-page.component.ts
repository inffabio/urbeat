import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { Subscription, interval } from 'rxjs';

import { CartService } from '../../../core/services/cart.service';
import { CheckoutService } from '../../../core/services/checkout.service';
import { PaymentService } from '../../../core/services/payment.service';
import { OrderService } from '../../../core/services/order.service';
import { ToastService } from '../../../core/services/toast.service';
import { OrderStatus } from '../../../shared/enums/order-status.enum';
import { PaymentStatus } from '../../../shared/enums/payment-status.enum';
import { BrlCurrencyPipe } from '../../../shared/pipes/brl-currency.pipe';
import { BackToMenuLinkComponent } from '../../../shared/components/back-to-menu-link/back-to-menu-link.component';
import { PaymentResponse } from '../../../shared/models/payment.model';

@Component({
  selector: 'app-online-payment-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, BackToMenuLinkComponent],
  templateUrl: './online-payment-page.component.html',
  styleUrl: './online-payment-page.component.scss',
})
export class OnlinePaymentPageComponent implements OnInit, OnDestroy {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly payments = inject(PaymentService);
  private readonly orders = inject(OrderService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly toast = inject(ToastService);

  readonly payment = signal<PaymentResponse | null>(null);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  private pollSub?: Subscription;

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
    this.pollSub?.unsubscribe();
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
    this.loading.set(true);
    this.errorMessage.set(null);
    this.payments.getPayment(orderId).subscribe({
      next: (payment) => {
        this.payment.set(payment);
        this.loading.set(false);
        if (payment.status === PaymentStatus.Paid) {
          this.goToTracking(orderId);
        }
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('Não foi possível carregar o Pix. Tente atualizar em instantes.');
        this.toast.showError('Não foi possível carregar o Pix.');
      },
    });
  }

  private startPolling(orderId: string): void {
    this.pollSub = interval(4000).subscribe(() => {
      this.orders.getOrder(orderId).subscribe({
        next: (order) => {
          if (order.status >= OrderStatus.Received) {
            this.pollSub?.unsubscribe();
            this.goToTracking(orderId);
          }
        },
      });
    });
  }

  private goToTracking(orderId: string): void {
    this.cart.clear();
    this.router.navigate(['/', this.getStorePath(), 'pedido', orderId]);
  }

  private getStorePath(): string {
    const m = this.router.url.match(/^\/([^/]+)\//);
    return m?.[1] ?? '';
  }
}
