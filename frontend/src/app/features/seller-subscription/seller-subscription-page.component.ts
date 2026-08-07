import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { SubscriptionService } from '../../core/services/subscription.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import {
  SellerSubscriptionBillingStatus,
  SellerSubscriptionChargeHistoryItem,
  SellerSubscriptionMyResponse,
} from '../../shared/models/subscription.model';

@Component({
  selector: 'app-seller-subscription-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-subscription-page.component.html',
  styleUrl: './seller-subscription-page.component.scss',
})
export class SellerSubscriptionPageComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);

  readonly BillingStatus = SellerSubscriptionBillingStatus;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly subscription = signal<SellerSubscriptionMyResponse | null>(null);
  readonly charges = signal<SellerSubscriptionChargeHistoryItem[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    forkJoin({
      subscription: this.subscriptions.getMySubscription(),
      charges: this.subscriptions.listMyCharges(),
    }).subscribe({
      next: ({ subscription, charges }) => {
        this.subscription.set(subscription);
        this.charges.set(charges);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  statusLabel(status: SellerSubscriptionBillingStatus | null | undefined): string {
    switch (status) {
      case SellerSubscriptionBillingStatus.Active:
        return 'Ativa';
      case SellerSubscriptionBillingStatus.DueSoon:
        return 'Vencendo';
      case SellerSubscriptionBillingStatus.Overdue:
        return 'Em atraso';
      case SellerSubscriptionBillingStatus.Blocked:
        return 'Bloqueada';
      case SellerSubscriptionBillingStatus.Cancelled:
        return 'Cancelada';
      default:
        return 'Nao contratada';
    }
  }

  formatCurrency(value: number | null | undefined): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value ?? 0);
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return 'Sem data';
    return formatSaoPauloDate(value);
  }
}
