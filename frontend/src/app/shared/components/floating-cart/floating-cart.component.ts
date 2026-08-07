import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonIcon } from '@ionic/angular/standalone';
import { BrlCurrencyPipe } from '../../pipes/brl-currency.pipe';

@Component({
  selector: 'app-floating-cart',
  standalone: true,
  imports: [CommonModule, IonIcon, BrlCurrencyPipe],
  template: `
    <button
      type="button"
      class="floating-cart"
      (click)="open.emit()"
      [attr.aria-label]="'Ver sacola: ' + itemCount + ' itens, ' + (total | brl)">
      <div class="bag-wrap">
        <span class="bag-icon-frame">
          <ion-icon name="bag-handle-outline" aria-hidden="true"></ion-icon>
          <span class="cart-count">{{ itemCount }}</span>
        </span>
      </div>
      <strong>Ver sacola</strong>
      <span class="vline"></span>
      <span class="cart-price">{{ total | brl }}</span>
    </button>
  `,
  styles: [`
    :host { display: contents; }

    .floating-cart {
      position: relative;
      z-index: 1;
      height: 48px;
      border-radius: 8px;
      border: none;
      background: var(--app-brand, #D54A51);
      color: #fff;
      display: grid;
      grid-template-columns: 54px 1fr 1px auto;
      align-items: center;
      gap: 8px;
      padding: 0 18px;
      margin: 0;
      width: 100%;
      box-sizing: border-box;
      box-shadow: 0 4px 16px rgba(213,74,81,.3);
      cursor: pointer;
      font-family: inherit;
      transition: opacity .15s;

      &:hover { opacity: .95; }
      &:active { opacity: .9; }
    }

    .bag-wrap {
      display: flex;
      align-items: center;
      justify-content: center;
      min-width: 40px;
    }

    .bag-icon-frame {
      position: relative;
      width: 34px;
      height: 34px;
      display: grid;
      place-items: center;

      ion-icon { font-size: 27px; }
    }

    .cart-count {
      position: absolute;
      top: -4px;
      right: -6px;
      min-width: 19px;
      height: 19px;
      padding: 0 5px;
      border-radius: 50%;
      background: #fff;
      color: var(--app-brand, #D54A51);
      font-size: 10px;
      line-height: 19px;
      font-weight: 800;
      display: grid;
      place-items: center;
      box-shadow: 0 2px 7px rgba(0,0,0,.2);
      border: 2px solid var(--app-brand, #D54A51);
      font-variant-numeric: tabular-nums;
    }

    .floating-cart strong {
      font-size: 13px;
      font-weight: 700;
      white-space: nowrap;
    }

    .vline {
      width: 1px;
      height: 22px;
      background: rgba(255,255,255,.35);
    }

    .cart-price {
      font-size: 14px;
      font-weight: 800;
      white-space: nowrap;
    }
  `],
})
export class FloatingCartComponent {
  @Input({ required: true }) itemCount!: number;
  @Input({ required: true }) total!: number;
  @Output() open = new EventEmitter<void>();
}
