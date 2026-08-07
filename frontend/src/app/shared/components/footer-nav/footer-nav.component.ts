import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonIcon } from '@ionic/angular/standalone';

export interface FooterNavItem {
  id: string;
  icon: string;
  label: string;
  active?: boolean;
  disabled?: boolean;
}

@Component({
  selector: 'app-footer-nav',
  standalone: true,
  imports: [CommonModule, IonIcon],
  template: `
    <div class="footer-nav-safe-zone">
      <footer class="footer-nav" aria-label="Navegacao principal">
        @for (item of items; track item.id) {
          <a
            [class.active]="item.active"
            [class.disabled]="item.disabled"
            [attr.aria-disabled]="item.disabled ? true : undefined"
            [attr.aria-current]="item.active ? 'page' : undefined"
            (click)="!item.disabled && select.emit(item.id)"
            (keydown.enter)="!item.disabled && select.emit(item.id)"
            tabindex="0"
            role="button">
            <ion-icon [name]="item.icon" aria-hidden="true"></ion-icon>
            <span>{{ item.label }}</span>
          </a>
        }
      </footer>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .footer-nav-safe-zone {
      background: var(--app-surface, #fff);
      padding-bottom: max(28px, env(safe-area-inset-bottom, 0px));
    }

    .footer-nav {
      min-height: 78px;
      background: rgba(255, 255, 255, .96);
      border-top: 1px solid rgba(234, 223, 214, .9);
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      align-items: center;
      padding: 7px 4px 9px;
      position: relative;
      z-index: 1;
      box-sizing: border-box;
    }

    .footer-nav a {
      min-height: 54px;
      display: grid;
      place-items: center;
      gap: 4px;
      color: #505258;
      font-size: 12px;
      font-weight: 500;
      text-decoration: none;
      cursor: pointer;
      transition: color .18s ease;

      ion-icon { font-size: 22px; line-height: 1; }

      &.active, &.active ion-icon { color: var(--app-brand, #D54A51); }

      &.disabled {
        opacity: .4;
        cursor: not-allowed;
        pointer-events: none;
      }
    }

    @media (max-width: 400px) {
      .footer-nav a { font-size: 11px; }
      .footer-nav a ion-icon { font-size: 21px; }
    }
  `],
})
export class FooterNavComponent {
  @Input({ required: true }) items!: FooterNavItem[];
  @Output() select = new EventEmitter<string>();
}
