import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { callOutline, cashOutline, checkmarkDoneOutline, locationOutline, navigateOutline, refreshOutline, searchOutline, trailSignOutline } from 'ionicons/icons';
import { OrderService } from '../../core/services/order.service';
import { formatSaoPauloDateTime } from '../../core/utils/sao-paulo-date.helper';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { SellerDeliverySummary } from '../../shared/models/order.model';

addIcons({
  'call-outline': callOutline,
  'cash-outline': cashOutline,
  'checkmark-done-outline': checkmarkDoneOutline,
  'location-outline': locationOutline,
  'navigate-outline': navigateOutline,
  'refresh-outline': refreshOutline,
  'search-outline': searchOutline,
  'trail-sign-outline': trailSignOutline,
});

@Component({
  selector: 'app-seller-deliveries-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonIcon],
  templateUrl: './seller-deliveries-page.component.html',
  styleUrls: ['./seller-deliveries-page.component.scss'],
})
export class SellerDeliveriesPageComponent implements OnInit {
  private readonly orders = inject(OrderService);

  readonly OrderStatus = OrderStatus;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly deliveries = signal<SellerDeliverySummary[]>([]);
  readonly searchQuery = signal('');
  readonly statusFilter = signal<'all' | OrderStatus.OnDelivery | OrderStatus.Delivered>('all');
  readonly filteredDeliveries = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const status = this.statusFilter();
    return this.deliveries().filter((order) => {
      const matchesStatus = status === 'all' || order.status === status;
      const haystack = [order.code, order.customerName, order.addressSummary, order.customerPhoneNumber].join(' ').toLowerCase();
      return matchesStatus && (!query || haystack.includes(query));
    });
  });
  readonly onDeliveryCount = computed(() => this.deliveries().filter((order) => order.status === OrderStatus.OnDelivery).length);
  readonly deliveredCount = computed(() => this.deliveries().filter((order) => order.status === OrderStatus.Delivered).length);
  readonly deliveryRevenue = computed(() => this.deliveries().filter((order) => order.status === OrderStatus.OnDelivery).reduce((sum, order) => sum + order.total, 0));
  readonly latestCreatedAtLabel = computed(() => {
    const latest = [...this.deliveries()].sort((a, b) => Date.parse(b.createdAtUtc) - Date.parse(a.createdAtUtc))[0];
    return latest ? `Atualizado em ${formatSaoPauloDateTime(latest.createdAtUtc)}` : 'Sem atualizacao recente';
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    this.orders.getStoreDeliveries().subscribe({
      next: (deliveries) => {
        this.deliveries.set(deliveries);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  formatDateTime(value: string): string {
    return formatSaoPauloDateTime(value);
  }

  statusLabel(status: OrderStatus): string {
    if (status === OrderStatus.OnDelivery) return 'Em rota';
    if (status === OrderStatus.Delivered) return 'Entregue';
    return 'Acompanhar';
  }

  paymentLabel(method?: PaymentMethod): string {
    switch (method) {
      case PaymentMethod.PixOnDelivery: return 'Pix na maquininha';
      case PaymentMethod.DebitOnDelivery: return 'Débito';
      case PaymentMethod.CreditOnDelivery: return 'Crédito';
      case PaymentMethod.CashOnDelivery: return 'Dinheiro';
      case PaymentMethod.PixOnline: return 'Pix online (legado)';
      case PaymentMethod.CardOnline: return 'Cartão online (legado)';
      case PaymentMethod.CardOnDelivery: return 'Cartão (legado)';
      default: return '-';
    }
  }

  cashDetail(order: SellerDeliverySummary): string {
    if (order.paymentMethod !== PaymentMethod.CashOnDelivery) return '';
    if (order.cashReceivedAmount != null) {
      return `Recebido ${this.formatCurrency(order.cashReceivedAmount)} · Troco ${this.formatCurrency(order.cashChangeAmount ?? 0)}`;
    }
    if (order.cashWithoutChange === true) return 'Sem troco';
    if (order.cashChangeForAmount != null) return `Troco para ${this.formatCurrency(order.cashChangeForAmount)}`;
    return '';
  }

  openMap(addressSummary?: string): void {
    if (!addressSummary) return;
    window.open(`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(addressSummary)}`, '_blank', 'noopener');
  }
}
