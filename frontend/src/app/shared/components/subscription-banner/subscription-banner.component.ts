import { Component, input, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonicModule } from '@ionic/angular';

export type SubscriptionBannerStatus = 'ok' | 'due-soon' | 'overdue';

@Component({
  selector: 'app-subscription-banner',
  standalone: true,
  imports: [CommonModule, IonicModule],
  template: `
    <div class="sub-banner" [class]="status()" role="status">
      <ion-icon [name]="iconName()" aria-hidden="true" />
      <span>{{ message() }}</span>
    </div>
  `,
  styles: [`
    .sub-banner {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 18px;
      border-radius: 14px;
      font-size: 13px;
      font-weight: 500;
      line-height: 1.4;
      min-height: 44px;
    }

    .sub-banner.ok {
      background: #e6f7ee;
      color: #166534;
      border: 1px solid #b7e4cf;
    }

    .sub-banner.due-soon {
      background: #fef3c7;
      color: #92400e;
      border: 1px solid #fde68a;
    }

    .sub-banner.overdue {
      background: #fee2e2;
      color: #991b1b;
      border: 1px solid #fecaca;
    }

    .sub-banner ion-icon {
      font-size: 20px;
      flex-shrink: 0;
    }
  `],
})
export class SubscriptionBannerComponent {
  readonly status = input<SubscriptionBannerStatus>('ok');
  readonly nextDueDate = input<string>('');

  readonly iconName = computed(() => {
    switch (this.status()) {
      case 'due-soon': return 'alert-circle-outline';
      case 'overdue': return 'warning-outline';
      default: return 'checkmark-circle-outline';
    }
  });

  readonly message = computed(() => {
    const date = this.nextDueDate();
    const dateText = date ? ` Proximo vencimento ${date}` : '';
    switch (this.status()) {
      case 'due-soon': return `Sua mensalidade vence em breve.${dateText}`;
      case 'overdue': return 'Sua mensalidade esta pendente. Regularize para continuar recebendo pedidos.';
      default: return `Sua mensalidade esta em dia!${dateText}`;
    }
  });
}
