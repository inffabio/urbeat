import { Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { IonIcon } from '@ionic/angular/standalone';

import { CartService } from '../../../core/services/cart.service';
import { BrlCurrencyPipe } from '../../pipes/brl-currency.pipe';

@Component({
  selector: 'app-cart-sheet',
  standalone: true,
  imports: [IonIcon, BrlCurrencyPipe],
  template: `
    @if (isOpen) {
      <div
        class="cart-sheet-backdrop cart-sheet-backdrop-above-footer"
        role="presentation"
        [style.--footer-height]="footerHeight"
        (click)="close.emit()"></div>
      <section
        class="cart-sheet cart-sheet-above-footer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="cart-sheet-title"
        tabindex="-1"
        [style.--footer-height]="footerHeight"
        [style.--cart-sheet-height]="sheetHeight"
        (click)="$event.stopPropagation()"
        (keydown.escape)="close.emit()">
        <button type="button" class="cart-sheet-collapse" aria-label="Recolher sacola" (click)="close.emit()">
          <ion-icon name="chevron-down" aria-hidden="true"></ion-icon>
        </button>

        <header class="cart-sheet-header">
          <div>
            <h2 id="cart-sheet-title">Sua sacola</h2>
            <p>{{ cart.totalItems() }} {{ cart.totalItems() === 1 ? 'item' : 'itens' }}</p>
          </div>
          <strong>{{ cart.subtotal() | brl }}</strong>
        </header>

        <div class="cart-sheet-list">
          @for (item of cart.items(); track item.id || item.productId || $index) {
            <article class="cart-sheet-item">
              @if (item.productImage) {
                <img [src]="item.productImage" [alt]="item.productName" />
              } @else {
                <span class="cart-sheet-item-placeholder" aria-hidden="true">
                  <ion-icon name="restaurant-outline"></ion-icon>
                </span>
              }
              <div class="cart-sheet-item-copy">
                <strong>{{ item.productName }}</strong>
                <span>{{ item.quantity }} x {{ item.unitPrice | brl }}</span>
              </div>
              <strong class="cart-sheet-item-total">{{ item.quantity * item.unitPrice | brl }}</strong>
            </article>
          }
        </div>

        <footer class="cart-sheet-footer">
          <div class="cart-sheet-total">
            <span>Total dos itens</span>
            <strong>{{ cart.subtotal() | brl }}</strong>
          </div>
          @if (cart.isEmpty()) {
            <p class="cart-sheet-empty-hint">Adicione itens do cardápio para continuar.</p>
          }
          <button
            type="button"
            class="cart-sheet-next"
            data-action="next-step"
            [disabled]="cart.isEmpty()"
            [attr.aria-disabled]="cart.isEmpty() ? 'true' : null"
            (click)="next.emit()">
            <span>Próxima etapa</span>
            <ion-icon name="arrow-forward" aria-hidden="true"></ion-icon>
          </button>
        </footer>
      </section>
    }
  `,
  styles: [`
    :host { display: contents; }
    .cart-sheet-backdrop {
      position: fixed;
      inset: 0;
      bottom: var(--footer-height);
      z-index: 60;
      background: rgba(22, 22, 22, .34);
      animation: cart-sheet-fade-in .24s ease-out both;
    }
    .cart-sheet {
      --cart-sheet-height: min(82dvh, calc(100dvh - var(--footer-height)));
      position: fixed;
      left: 50%;
      bottom: var(--footer-height);
      z-index: 80;
      display: flex;
      flex-direction: column;
      width: min(430px, 100%);
      height: var(--cart-sheet-height);
      max-height: var(--cart-sheet-height);
      margin: 0 auto;
      overflow: hidden;
      border-radius: 26px 26px 0 0;
      background: var(--app-surface, #fff);
      box-shadow: var(--shadow-lg);
      transform: translateX(-50%);
      animation: cart-sheet-rise .38s cubic-bezier(.22, 1, .36, 1) both;
    }
    .cart-sheet-collapse {
      flex: 0 0 44px;
      align-self: center;
      width: 44px;
      height: 44px;
      border: 0;
      background: transparent;
      color: var(--app-text-secondary, #5a5a63);
      cursor: pointer;
      font-size: 22px;
    }
    .cart-sheet-collapse:hover,
    .cart-sheet-collapse:focus-visible { color: var(--app-brand, #D54A51); outline: none; }
    .cart-sheet-header,
    .cart-sheet-footer { padding: 0 20px; }
    .cart-sheet-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
      padding-bottom: 14px;
    }
    .cart-sheet-header h2 { margin: 0; color: var(--app-ink, #161616); font-size: 21px; font-weight: 800; letter-spacing: -.035em; }
    .cart-sheet-header p { margin: 3px 0 0; color: var(--app-text-secondary, #5a5a63); font-size: 12px; }
    .cart-sheet-header > strong { color: var(--app-ink, #161616); font-size: 15px; font-weight: 800; }
    .cart-sheet-list {
      display: grid;
      flex: 1 1 auto;
      gap: 10px;
      min-height: 0;
      overflow-y: auto;
      padding: 0 20px 18px;
    }
    .cart-sheet-item {
      display: grid;
      grid-template-columns: 48px minmax(0, 1fr) auto;
      align-items: center;
      gap: 10px;
      min-height: 64px;
      padding: 8px 0;
      border-bottom: 1px solid var(--app-border-light, #eadfd6);
    }
    .cart-sheet-item img,
    .cart-sheet-item-placeholder { width: 48px; height: 48px; border-radius: 12px; object-fit: cover; }
    .cart-sheet-item-placeholder { display: grid; place-items: center; background: var(--app-hairline-warm, #f3efe9); color: var(--app-muted-strong, #6f6f6f); font-size: 20px; }
    .cart-sheet-item-copy { display: grid; gap: 3px; min-width: 0; }
    .cart-sheet-item-copy strong { overflow: hidden; color: var(--app-ink, #161616); font-size: 13px; font-weight: 800; text-overflow: ellipsis; white-space: nowrap; }
    .cart-sheet-item-copy span { color: var(--app-text-secondary, #5a5a63); font-size: 11px; }
    .cart-sheet-item-total { color: var(--app-ink, #161616); font-size: 12px; font-weight: 800; white-space: nowrap; }
    .cart-sheet-footer { flex-shrink: 0; padding-top: 14px; padding-bottom: max(16px, env(safe-area-inset-bottom, 16px)); box-shadow: 0 -8px 20px rgba(33, 20, 8, .05); }
    .cart-sheet-total { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 12px; color: var(--app-text-secondary, #5a5a63); font-size: 12px; }
    .cart-sheet-total strong { color: var(--app-ink, #161616); font-size: 18px; font-weight: 800; }
    .cart-sheet-next { display: inline-flex; align-items: center; justify-content: center; gap: 8px; width: 100%; min-height: 46px; border: 0; border-radius: 999px; background: var(--app-brand, #D54A51); color: #fff; font: inherit; font-size: 14px; font-weight: 800; cursor: pointer; }
    .cart-sheet-next:not(:disabled):hover,
    .cart-sheet-next:not(:disabled):focus-visible { background: var(--app-brand-dark, #B63A41); outline: 3px solid var(--app-brand-shadow, rgba(213, 74, 81, .25)); outline-offset: 2px; }
    .cart-sheet-next:disabled { background: var(--app-muted-strong, #6f6f6f); opacity: .45; cursor: not-allowed; }
    .cart-sheet-next ion-icon { font-size: 18px; }
    .cart-sheet-empty-hint { margin: 0 0 12px; color: var(--app-text-secondary, #5a5a63); font-size: 12px; text-align: center; }
    @keyframes cart-sheet-fade-in { from { opacity: 0; } to { opacity: 1; } }
    @keyframes cart-sheet-rise { from { transform: translate(-50%, 100%); } to { transform: translate(-50%, 0); } }
    @media (prefers-reduced-motion: reduce) { .cart-sheet-backdrop, .cart-sheet { animation: none; } }
  `],
})
export class CartSheetComponent {
  readonly cart = inject(CartService);
  @Input() isOpen = false;
  @Input() footerHeight = 'var(--store-footer-clearance, calc(64px + max(8px, env(safe-area-inset-bottom, 0px))))';
  @Input() sheetHeight = 'min(82dvh, calc(100dvh - var(--footer-height)))';
  @Output() readonly close = new EventEmitter<void>();
  @Output() readonly next = new EventEmitter<void>();
}
