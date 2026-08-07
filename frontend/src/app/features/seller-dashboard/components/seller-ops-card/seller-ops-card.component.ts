import { CommonModule } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterModule } from '@angular/router';
import { SellerNotification } from '../../../../shared/models/seller-notification.model';

@Component({
  selector: 'app-seller-ops-card',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './seller-ops-card.component.html',
  styleUrl: './seller-ops-card.component.scss',
})
export class SellerOpsCardComponent {
  readonly newOrder = input<SellerNotification | null>(null);
}
