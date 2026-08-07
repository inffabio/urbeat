import { CommonModule } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterModule } from '@angular/router';
import { OrderStatus } from '../../../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../../../shared/enums/payment-method.enum';
import { OrderSummary } from '../../../../shared/models/order.model';

@Component({
  selector: 'app-recent-orders-list',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './recent-orders-list.component.html',
  styleUrl: './recent-orders-list.component.scss',
})
export class RecentOrdersListComponent {
  readonly orders = input.required<OrderSummary[]>();

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  statusLabel(status: OrderStatus): string {
    switch (status) {
      case OrderStatus.Received:
        return 'Recebido';
      case OrderStatus.Preparing:
        return 'Preparando';
      case OrderStatus.Ready:
        return 'Pronto';
      case OrderStatus.OnDelivery:
        return 'Saiu para entrega';
      case OrderStatus.Delivered:
        return 'Entregue';
      case OrderStatus.Cancelled:
        return 'Cancelado';
      default:
        return 'Pedido';
    }
  }

  statusTone(status: OrderStatus): string {
    if (status === OrderStatus.Preparing) return 'badge-yellow';
    if (status === OrderStatus.Ready || status === OrderStatus.OnDelivery) return 'badge-blue';
    if (status === OrderStatus.Delivered) return 'badge-green';
    if (status === OrderStatus.Cancelled) return 'badge-red';
    return 'badge-orange';
  }

  paymentLabel(method: PaymentMethod | undefined): string {
    switch (method) {
      case PaymentMethod.PixOnline:
        return 'Pix';
      case PaymentMethod.CardOnline:
        return 'Cartao online';
      case PaymentMethod.CashOnDelivery:
        return 'Dinheiro';
      case PaymentMethod.CardOnDelivery:
        return 'Cartao ao receber';
      default:
        return '-';
    }
  }
}
