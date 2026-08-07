import { CommonModule } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { OrderStatus } from '../../../../shared/enums/order-status.enum';
import { OrderSummary } from '../../../../shared/models/order.model';
import { AdvanceOrderEvent, SellerOrderCardComponent } from '../seller-order-card/seller-order-card.component';

@Component({
  selector: 'app-seller-order-status-group',
  standalone: true,
  imports: [CommonModule, SellerOrderCardComponent],
  templateUrl: './seller-order-status-group.component.html',
  styleUrl: './seller-order-status-group.component.scss',
})
export class SellerOrderStatusGroupComponent {
  readonly title = input.required<string>();
  readonly status = input<OrderStatus | null>(null);
  readonly orders = input.required<OrderSummary[]>();
  readonly updatingOrderId = input<string | null>(null);
  readonly highlightedOrderId = input<string | null>(null);
  readonly advance = output<AdvanceOrderEvent>();
  readonly selectOrder = output<string>();
}
