import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { callOutline, cashOutline, checkmarkDoneOutline, locationOutline, navigateOutline, refreshOutline, searchOutline, trailSignOutline } from 'ionicons/icons';
import { OrderService } from '../../core/services/order.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
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
    return latest ? `Atualizado em ${new Date(latest.createdAtUtc).toLocaleString('pt-BR')}` : 'Sem atualizacao recente';
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
    return new Date(value).toLocaleString('pt-BR');
  }

  statusLabel(status: OrderStatus): string {
    if (status === OrderStatus.OnDelivery) return 'Em rota';
    if (status === OrderStatus.Delivered) return 'Entregue';
    return 'Acompanhar';
  }

  openMap(addressSummary?: string): void {
    if (!addressSummary) return;
    window.open(`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(addressSummary)}`, '_blank', 'noopener');
  }
}
