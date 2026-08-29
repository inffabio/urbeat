import { Component, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';

import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { StoreContextService } from '../../core/services/store-context.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import { OrderDetails } from '../../shared/models/order.model';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-customer-orders-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, BrlCurrencyPipe, EmptyStateComponent],
  templateUrl: './customer-orders-page.component.html',
  styleUrl: './customer-orders-page.component.scss',
})
export class CustomerOrdersPageComponent {
  private readonly tracking = inject(CustomerOrderTrackingService);
  private readonly storeContext = inject(StoreContextService);
  private readonly router = inject(Router);

  readonly activeOrders = computed<OrderDetails[]>(() =>
    this.tracking
      .activeOrders()
      .filter((order) => order.storeId === this.storeContext.storeId())
      .sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()),
  );

  readonly finishedOrders = computed<OrderDetails[]>(() =>
    this.tracking
      .finishedOrders()
      .filter((order) => order.storeId === this.storeContext.storeId())
      .sort((a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime()),
  );

  statusLabel(status: OrderStatus): string {
    switch (status) {
      case OrderStatus.Created: return 'Criado';
      case OrderStatus.PendingPayment: return 'Aguardando pagamento';
      case OrderStatus.Received: return 'Recebido';
      case OrderStatus.Preparing: return 'Preparando';
      case OrderStatus.Ready: return 'Pronto';
      case OrderStatus.OnDelivery: return 'Saiu para entrega';
      case OrderStatus.Delivered: return 'Entregue';
      case OrderStatus.Cancelled: return 'Cancelado';
      default: return '';
    }
  }

  itemCount(order: OrderDetails): number {
    return order.items.reduce((sum, item) => sum + item.quantity, 0);
  }

  formatDate(value: string): string {
    return formatSaoPauloDate(value);
  }

  goToOrder(orderId: string): void {
    this.router.navigate(['/', this.storePath(), 'pedido', orderId]);
  }

  goToMenu(): void {
    this.router.navigate(['/', this.storePath()]);
  }

  private storePath(): string {
    const match = this.router.url.match(/^\/([^/]+)/);
    return match?.[1] ?? '';
  }
}
