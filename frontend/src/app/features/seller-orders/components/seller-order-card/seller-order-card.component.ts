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

  statusLabel(status: OrderStatus, fulfillmentType?: FulfillmentType): string {
    switch (status) {
      case OrderStatus.Received:
        return 'Recebido';
      case OrderStatus.Preparing:
        return 'Preparando';
      case OrderStatus.Ready:
        return fulfillmentType === FulfillmentType.PickUp ? 'Pronto para retirada' : 'Pronto';
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
        if (this.order().fulfillmentType === FulfillmentType.PickUp) return [];
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
      case PaymentMethod.PixOnDelivery:
        return 'Pix na maquininha';
      case PaymentMethod.DebitOnDelivery:
        return 'Débito na maquininha';
      case PaymentMethod.CreditOnDelivery:
        return 'Crédito na maquininha';
      case PaymentMethod.CashOnDelivery:
        return 'Dinheiro';
      case PaymentMethod.PixOnline:
        return 'Pix online (legado)';
      case PaymentMethod.CardOnline:
        return 'Cartão online (legado)';
      case PaymentMethod.CardOnDelivery:
        return 'Cartão na entrega (legado)';
      default:
        return '';
    }
  }

  /** Cash change summary, without any card machine identifier or bank verification claim. */
  cashDetail(): string {
    const order = this.order();
    if (order.paymentMethod !== PaymentMethod.CashOnDelivery) return '';

    if (order.cashReceivedAmount !== undefined && order.cashReceivedAmount !== null) {
      const change = order.cashChangeAmount ?? 0;
      return `Recebido ${this.formatCurrency(order.cashReceivedAmount)} · Troco ${this.formatCurrency(change)}`;
    }

    if (order.cashWithoutChange === true) return 'Sem troco';
    if (order.cashChangeForAmount !== undefined && order.cashChangeForAmount !== null) {
      return `Troco para ${this.formatCurrency(order.cashChangeForAmount)}`;
    }
    return '';
  }

  paymentConfirmationLabel(): string {
    const confirmedAt = this.order().paymentConfirmedAtUtc;
    return confirmedAt ? `Pagamento confirmado às ${this.formatTime(confirmedAt)}` : '';
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
