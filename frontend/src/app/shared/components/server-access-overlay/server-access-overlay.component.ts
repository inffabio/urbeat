import { Component, Input } from '@angular/core';
import { FoodDeliverySpinnerComponent } from '../food-delivery-spinner/food-delivery-spinner.component';

@Component({
  selector: 'app-server-access-overlay',
  standalone: true,
  imports: [FoodDeliverySpinnerComponent],
  template: `
    <div
      class="server-access-overlay"
      role="status"
      aria-live="polite"
      aria-busy="true"
      [attr.aria-label]="message"
    >
      <div class="server-access-panel" aria-hidden="true">
        <app-food-delivery-spinner [message]="message" />
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }

    .server-access-overlay {
      position: fixed;
      inset: 0;
      z-index: 10000;
      display: grid;
      place-items: center;
      background: var(--app-overlay, rgba(0, 0, 0, .45));
      overscroll-behavior: contain;
      touch-action: none;
      animation: server-access-fade .16s ease-out;
    }

    .server-access-panel {
      display: grid;
      place-items: center;
      box-sizing: border-box;
      width: min(
        300px,
        calc(100vw - env(safe-area-inset-left, 0px) - env(safe-area-inset-right, 0px) - 32px),
        calc(100vh - env(safe-area-inset-top, 0px) - env(safe-area-inset-bottom, 0px) - 32px)
      );
      aspect-ratio: 1;
      overflow: hidden;
      padding: 8px;
      border-radius: 50%;
      background: rgba(255, 255, 255, .12);
    }

    @media (prefers-reduced-motion: reduce) {
      .server-access-overlay {
        animation: none;
      }
    }

    @keyframes server-access-fade {
      from { opacity: 0; }
      to { opacity: 1; }
    }
  `],
})
export class ServerAccessOverlayComponent {
  @Input() message = 'Carregando...';
}
