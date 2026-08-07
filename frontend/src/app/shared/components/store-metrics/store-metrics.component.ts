import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonIcon } from '@ionic/angular/standalone';

@Component({
  selector: 'app-store-metrics',
  standalone: true,
  imports: [CommonModule, IonIcon],
  template: `
    <div class="metrics" aria-label="Informacoes do restaurante">
      <div class="metric" [class.status-open]="isOpen" [class.status-closed]="!isOpen">
        <ion-icon name="checkmark-circle" aria-hidden="true"></ion-icon>
        <div>
          <strong>{{ statusText }}</strong>
          <span>Status da loja</span>
        </div>
      </div>
      <div class="metric">
        <ion-icon name="time-outline" aria-hidden="true"></ion-icon>
        <div>
          <strong>{{ etaText || '30-45 min' }}</strong>
          <span>Tempo medio</span>
        </div>
      </div>
      <div class="metric">
        <ion-icon name="card-outline" aria-hidden="true"></ion-icon>
        <div>
          <strong>{{ minOrderText }}</strong>
          <span>Pedido minimo</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .metrics {
      display: grid;
      grid-template-columns: 1fr 1fr 1fr;
      gap: 0;
      background: var(--app-surface, #fff);
      border: 1px solid var(--app-border-light, #eadfd6);
      border-radius: var(--app-radius-lg, 18px);
      padding: 12px 6px;
      margin-bottom: 18px;
      text-align: center;
    }

    .metric {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      padding: 0 6px;
      border-right: 1px solid var(--app-border-light, #eadfd6);
      min-width: 0;

      &:last-child { border-right: 0; }

      ion-icon { font-size: 16px; color: var(--app-text-secondary, #6f6f76); }

      strong {
        font-size: 13px;
        line-height: 1.15;
        display: block;
        font-weight: 700;
      }

      span {
        font-size: 10px;
        color: var(--app-text-muted, #8c8c91);
        display: block;
      }

      &.status-open {
        ion-icon { color: var(--app-success-green, #119441); }
        strong { color: var(--app-success-green, #119441); }
      }

      &.status-closed {
        ion-icon { color: #b42318; }
        strong { color: #b42318; }
      }
    }
  `],
})
export class StoreMetricsComponent {
  @Input() isOpen = false;
  @Input() statusText = '';
  @Input() etaText = '';
  @Input() minOrderText = '';
}
