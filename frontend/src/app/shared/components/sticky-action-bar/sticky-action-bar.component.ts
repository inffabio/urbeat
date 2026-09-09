import { Component, EventEmitter, Input, Output } from '@angular/core';
import { IonIcon } from '@ionic/angular/standalone';

@Component({
  selector: 'app-sticky-action-bar',
  standalone: true,
  imports: [IonIcon],
  template: `
    <button
      type="button"
      class="sticky-action"
      [disabled]="disabled"
      [attr.aria-label]="label"
      (click)="action.emit()"
    >
      <span class="action-icon" aria-hidden="true">
        <ion-icon [name]="icon"></ion-icon>
      </span>
      <span class="action-copy">
        <strong>{{ label }}</strong>
      </span>
      <span class="action-divider" aria-hidden="true"></span>
      <span class="action-detail">{{ detail }}</span>
    </button>
  `,
  styles: [`
    :host {
      display: block;
      position: fixed;
      left: 50%;
      bottom: var(--store-footer-clearance, calc(64px + max(8px, env(safe-area-inset-bottom, 0px))));
      z-index: 40;
      width: min(430px, 100%);
      max-width: 100%;
      padding: 0 12px;
      transform: translateX(-50%);
      box-sizing: border-box;
    }

    .sticky-action {
      position: relative;
      z-index: 1;
      width: 100%;
      height: 48px;
      display: grid;
      grid-template-columns: 54px 1fr 1px auto;
      align-items: center;
      gap: 8px;
      padding: 0 18px;
      margin: 0;
       border: 1px solid var(--app-border-light, #eadfd6);
      border-radius: 8px;
       background: var(--app-surface, #fff);
       color: var(--app-ink, #161616);
       box-shadow: var(--shadow-sm, 0 8px 22px rgba(33, 20, 8, .07));
      cursor: pointer;
      font-family: inherit;
      text-align: left;
      transition: opacity .15s;
      box-sizing: border-box;
    }

     .sticky-action:hover { border-color: var(--app-brand, #D54A51); }
     .sticky-action:active { background: var(--app-brand-soft, #FDECEE); }
    .sticky-action:focus-visible {
      outline: 3px solid var(--app-brand-shadow, rgba(213, 74, 81, .25));
      outline-offset: 2px;
    }
    .sticky-action:disabled { opacity: .5; cursor: not-allowed; }

     .action-icon {
      min-width: 40px;
      display: flex;
      align-items: center;
      justify-content: center;
       color: var(--app-brand, #D54A51);
       font-size: 27px;
    }

    .action-copy {
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 1px;
    }

    .action-copy strong {
      overflow: hidden;
      font-size: 13px;
      font-weight: 700;
      line-height: 1.1;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .action-divider {
      width: 1px;
      height: 22px;
       background: var(--app-border-light, #eadfd6);
    }

    .action-detail {
      overflow: hidden;
       color: var(--app-text-secondary, #5a5a63);
       font-size: 14px;
      font-weight: 800;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `],
})
export class StickyActionBarComponent {
  @Input({ required: true }) icon!: string;
  @Input({ required: true }) label!: string;
  @Input() detail = '';
  @Input() disabled = false;
  @Output() readonly action = new EventEmitter<void>();
}
