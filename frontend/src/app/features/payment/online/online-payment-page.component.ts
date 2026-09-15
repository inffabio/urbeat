import { Component, OnDestroy, OnInit, inject, signal, computed } from '@angular/core';
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
import { PaymentGateway } from '../../../shared/enums/payment-gateway.enum';
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
  readonly remainingSeconds = signal(0);
  readonly expired = signal(false);

  readonly isMock = computed(() => this.payment()?.gateway === PaymentGateway.Mock);
  readonly countdownLabel = computed(() => {
    const total = this.remainingSeconds();
    const minutes = Math.floor(total / 60);
    const seconds = total % 60;
    return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
  });
  readonly stickyLabel = computed(() => {
    if (this.loading()) return 'Carregando...';
    if (this.expired()) return 'Tentar novamente';
    return 'Continuar';
  });

  private pollSub?: Subscription;
  private loadPaymentSub?: Subscription;
  private orderPollSub?: Subscription;
  private countdownSub?: Subscription;
  private confirmSub?: Subscription;
  private orderPollInFlight = false;
  private confirmExpiryInFlight = false;
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
    this.countdownSub?.unsubscribe();
    this.confirmSub?.unsubscribe();
  }

  onBack(): void {
    this.location.back();
  }

  goToMenu(): void {
    this.router.navigate(['/', this.getStorePath()]);
  }

  refreshPayment(): void {
    const orderId = this.checkout.lastOrderId();
    if (!orderId) return;
    this.cancelConfirmation();
    this.loadPayment(orderId);
  }

  retryPayment(): void {
    const orderId = this.checkout.lastOrderId();
    if (!orderId || this.loading()) return;
    this.loadPaymentSub?.unsubscribe();
    this.cancelConfirmation();
    this.loading.set(true);
    this.expired.set(false);
    this.errorMessage.set(null);
    this.loadPaymentSub = this.payments.createPayment(orderId).subscribe({
      next: () => {
        if (this.destroyed) return;
        this.loadPayment(orderId);
        this.startPolling(orderId);
      },
      error: () => {
        if (this.destroyed) return;
        this.loading.set(false);
        this.errorMessage.set('Não foi possível reiniciar o Pix. Tente novamente.');
        this.toast.showError('Não foi possível reiniciar o Pix.');
      },
    });
  }

  onStickyAction(): void {
    if (this.expired()) {
      this.retryPayment();
      return;
    }
    this.refreshPayment();
  }

  private loadPayment(orderId: string): void {
    this.loadPaymentSub?.unsubscribe();
    this.loading.set(true);
    this.errorMessage.set(null);
    this.loadPaymentSub = this.payments.getPayment(orderId).subscribe({
      next: (payment) => {
        if (this.destroyed) return;

        // Clear any visual state from a previous response (expired flag, countdown timer and
        // remaining seconds) before deciding the new gateway/status, so a Mercado Pago Pending
        // that arrives after a mock expiry never shows the expired card nor keeps a stale timer.
        this.stopCountdown();
        this.expired.set(false);
        this.remainingSeconds.set(0);

        this.payment.set(payment);
        this.loading.set(false);

        if (payment.status === PaymentStatus.Paid) {
          this.goToTracking(orderId);
          return;
        }

        if (this.isExpiredStatus(payment)) {
          this.cancelPolling();
          this.expired.set(true);
          return;
        }

        // The server status is authoritative. Keep observing the order until the local countdown
        // reaches zero; from there the final server confirmation decides Paid vs. expired.
        this.startPolling(orderId);

        if (this.isMock() && payment.expiresAtUtc) {
          this.startCountdown(payment.expiresAtUtc, orderId);
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

  private isExpiredStatus(payment: PaymentResponse): boolean {
    return payment.gateway === PaymentGateway.Mock
      && (payment.status === PaymentStatus.Failed || payment.status === PaymentStatus.Cancelled);
  }

  private startCountdown(expiresAtUtc: string, orderId: string): void {
    this.stopCountdown();
    this.cancelConfirmation();
    this.expired.set(false);
    const deadline = Date.parse(expiresAtUtc);
    if (Number.isNaN(deadline)) return;
    const remaining = this.updateRemaining(deadline);
    if (remaining <= 0) {
      this.confirmExpiry(orderId);
      return;
    }
    this.countdownSub = interval(1000).subscribe(() => {
      if (this.destroyed) return;
      const nextRemaining = this.updateRemaining(deadline);
      if (nextRemaining <= 0) {
        this.stopCountdown();
        this.confirmExpiry(orderId);
      }
    });
  }

  private updateRemaining(deadline: number): number {
    const remaining = Math.max(0, Math.floor((deadline - Date.now()) / 1000));
    this.remainingSeconds.set(remaining);
    return remaining;
  }

  private stopCountdown(): void {
    this.countdownSub?.unsubscribe();
    this.countdownSub = undefined;
  }

  private confirmExpiry(orderId: string): void {
    if (this.destroyed || this.navigatedToTracking || this.confirmExpiryInFlight) return;
    this.confirmExpiryInFlight = true;
    this.cancelPolling();
    this.confirmSub = this.payments.getPayment(orderId).subscribe({
      next: (payment) => {
        this.confirmExpiryInFlight = false;
        if (this.destroyed) return;
        if (payment.status === PaymentStatus.Paid) {
          this.goToTracking(orderId);
          return;
        }
        this.expired.set(true);
        this.cancelPolling();
      },
      error: () => {
        this.confirmExpiryInFlight = false;
        if (this.destroyed) return;
        this.expired.set(true);
        this.cancelPolling();
      },
    });
  }

  private cancelConfirmation(): void {
    this.confirmSub?.unsubscribe();
    this.confirmSub = undefined;
    this.confirmExpiryInFlight = false;
  }

  private startPolling(orderId: string): void {
    if (this.destroyed || this.navigatedToTracking || this.pollSub || this.expired()) return;
    // A server-confirmed terminal payment can never be approved, so there is nothing left to
    // observe. An expired mock payment has already been settled by the final server confirmation,
    // so polling must not be re-armed.
    const status = this.payment()?.status;
    if (status !== undefined && status !== PaymentStatus.Pending) return;
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
    this.stopCountdown();
    this.tracking.trackOrder(orderId);
    this.cart.clear();
    this.router.navigate(['/', this.getStorePath(), 'pedido', orderId]);
  }

  private getStorePath(): string {
    const m = this.router.url.match(/^\/([^/]+)\//);
    return m?.[1] ?? '';
  }
}
