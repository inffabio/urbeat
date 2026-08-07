import { Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'app-back-to-menu-link',
  standalone: true,
  template: `
    <button
      type="button"
      class="back-to-menu-action"
      [class.primary]="variant === 'primary'"
      [class.link]="variant === 'link'"
      [disabled]="disabled"
      (click)="navigateBack.emit()"
    >
      {{ label }}
    </button>
  `,
  styles: [`
    :host {
      display: block;
      text-align: center;
    }

    .back-to-menu-action {
      min-height: 44px;
      border: 0;
      border-radius: var(--radius-full, 999px);
      background: transparent;
      color: var(--app-brand, #D54A51);
      font-family: var(--app-font, inherit);
      font-size: 18px;
      font-weight: 800;
      cursor: pointer;
      text-decoration: none;
      transition: color .15s ease, background .15s ease, opacity .15s ease;
    }

    .back-to-menu-action.link {
      padding: 8px 12px;
    }

    .back-to-menu-action.primary {
      width: min(100%, 280px);
      padding: 0 24px;
      background: var(--app-brand, #D54A51);
      color: var(--app-surface, #fff);
      font-size: 15px;
      font-weight: 700;
    }

    .back-to-menu-action:hover:not(:disabled),
    .back-to-menu-action:focus-visible:not(:disabled) {
      color: var(--app-brand-dark, #B63A41);
      text-decoration: underline;
      outline: none;
    }

    .back-to-menu-action.primary:hover:not(:disabled),
    .back-to-menu-action.primary:focus-visible:not(:disabled) {
      background: var(--app-brand-dark, #B63A41);
      color: var(--app-surface, #fff);
      text-decoration: none;
    }

    .back-to-menu-action:focus-visible:not(:disabled) {
      box-shadow: 0 0 0 3px var(--app-brand-shadow, rgba(213, 74, 81, .18));
    }

    .back-to-menu-action:disabled {
      opacity: .5;
      cursor: not-allowed;
    }
  `],
})
export class BackToMenuLinkComponent {
  @Input() label = 'Voltar ao cardápio';
  @Input() variant: 'link' | 'primary' = 'link';
  @Input() disabled = false;
  @Output() navigateBack = new EventEmitter<void>();
}
