import { CommonModule } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { formatSaoPauloTime } from '../../../../core/utils/sao-paulo-date.helper';
import { FulfillmentType } from '../../../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../../../shared/enums/payment-method.enum';
import { OrderSummary } from '../../../../shared/models/order.model';

export interface AdvanceOrderEvent {
  orderId: string;
  nextStatus: OrderStatus;
}

interface OrderStatusAction {
  label: string;
  nextStatus: OrderStatus;
  variant?: 'primary' | 'secondary' | 'danger';
}

@Component({
  selector: 'app-seller-order-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-order-card.component.html',
  styleUrl: './seller-order-card.component.scss',
})
export class SellerOrderCardComponent {
  readonly order = input.required<OrderSummary>();
  readonly updatingOrderId = input<string | null>(null);
  readonly highlighted = input(false);
  readonly advance = output<AdvanceOrderEvent>();
  readonly select = output<string>();

  readonly OrderStatus = OrderStatus;

  statusLabel(status: OrderStatus): string {
    switch (status) {
      case OrderStatus.Received:
        return 'Recebido';
      case OrderStatus.Preparing:
        return 'Preparando';
      case OrderStatus.Ready:
        return 'Pronto';
      case OrderStatus.OnDelivery:
        return 'Em entrega';
      case OrderStatus.Delivered:
        return 'Entregue';
      case OrderStatus.Cancelled:
        return 'Cancelado';
      default:
        return 'Pedido';
    }
  }

  statusActions(status: OrderStatus): OrderStatusAction[] {
    switch (status) {
      case OrderStatus.Received:
        return [
          { label: 'Aceitar pedido', nextStatus: OrderStatus.Preparing },
          { label: 'Cancelar', nextStatus: OrderStatus.Cancelled, variant: 'danger' },
        ];
      case OrderStatus.Preparing:
        return [
          { label: 'Marcar pronto', nextStatus: OrderStatus.Ready },
          { label: 'Cancelar', nextStatus: OrderStatus.Cancelled, variant: 'danger' },
        ];
      case OrderStatus.Ready:
        return [
          { label: 'Saiu para entrega', nextStatus: OrderStatus.OnDelivery },
          { label: 'Entregue no balcão', nextStatus: OrderStatus.Delivered, variant: 'secondary' },
          { label: 'Cancelar', nextStatus: OrderStatus.Cancelled, variant: 'danger' },
        ];
      case OrderStatus.OnDelivery:
        return [
          { label: 'Concluir pedido', nextStatus: OrderStatus.Delivered },
          { label: 'Cancelar', nextStatus: OrderStatus.Cancelled, variant: 'danger' },
        ];
      default:
        return [];
    }
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  formatTime(value: string): string {
    return formatSaoPauloTime(value);
  }

  fulfillmentLabel(type: FulfillmentType | undefined): string {
    if (type === undefined || type === null) return '';
    return type === FulfillmentType.PickUp ? 'Retirada' : 'Entrega';
  }

  paymentLabel(method: PaymentMethod | undefined): string {
    switch (method) {
      case PaymentMethod.PixOnline:
        return 'Pix online';
      case PaymentMethod.CardOnline:
        return 'Cartao online';
      case PaymentMethod.CashOnDelivery:
        return 'Dinheiro ao receber';
      case PaymentMethod.CardOnDelivery:
        return 'Cartao ao receber';
      default:
        return '';
    }
  }

  emitSelect(): void {
    this.select.emit(this.order().id);
  }

  emitAdvance(event: Event, nextStatus: OrderStatus): void {
    event.stopPropagation();
    if (this.updatingOrderId() === this.order().id) return;
    this.advance.emit({ orderId: this.order().id, nextStatus });
  }

}
