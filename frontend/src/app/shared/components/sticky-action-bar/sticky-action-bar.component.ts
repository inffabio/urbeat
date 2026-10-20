import { Component, EventEmitter, HostBinding, Input, Output } from '@angular/core';
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
      /* The measured footer clearance already includes the safe area when the
         footer is rendered. On footer-less routes the shell sets it to 0px, so
         max() keeps the bar above the iOS home indicator / Android gesture
         area instead of letting the var() fallback mask the inset. */
      bottom: calc(max(var(--store-footer-clearance, 0px), env(safe-area-inset-bottom, 0px)) + 8px);
      z-index: 75;
      width: min(430px, 100%);
      max-width: 100%;
      padding: 0 12px;
      transform: translateX(-50%);
        box-sizing: border-box;
      }

      :host(.in-flow) {
        position: static;
        flex: 0 0 auto;
        width: 100%;
        padding: 0 18px 16px;
        transform: none;
      }

      /* In-flow bars are the last item of a full-height route column, so they
         must add the device inset themselves instead of relying on a fixed
         bottom offset. */
      :host(.in-flow.in-flow-safe-area) {
        padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px));
      }

    .sticky-action {
      position: relative;
      z-index: 1;
      width: 100%;
      height: 48px;
      display: grid;
      /* The label is the action itself: it keeps its intrinsic width and the
         detail ellipsizes into whatever is left, so "Continuar" is never
         reduced to "Co..." on narrow phones. */
      grid-template-columns: 54px auto 1px minmax(0, 1fr);
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
      min-width: 0;
      overflow: hidden;
       color: var(--app-text-secondary, #5a5a63);
       font-size: 14px;
      font-weight: 800;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    :host(.sticky-action-primary) .sticky-action {
      border-color: var(--app-brand, #D54A51);
      background: var(--app-brand, #D54A51);
      color: var(--app-surface, #fff);
    }

    :host(.sticky-action-primary) .action-icon,
    :host(.sticky-action-primary) .action-detail,
    :host(.sticky-action-primary) .action-divider {
      color: var(--app-surface, #fff);
    }

    :host(.sticky-action-primary) .action-divider {
      background: rgba(255, 255, 255, .35);
    }

    :host(.sticky-action-primary) .sticky-action:hover {
      border-color: var(--app-brand-dark, #B63A41);
      background: var(--app-brand-dark, #B63A41);
    }

    /* DESIGN.md: primary disabled keeps a border-medium fill and no shadow.
       Unlike the default bar's opacity, this keeps the label legible instead
       of washing white text over a translucent bordeaux fill. */
    :host(.sticky-action-primary) .sticky-action:disabled {
      border-color: var(--app-border-medium, #d6d0cc);
      background: var(--app-border-medium, #d6d0cc);
      color: var(--app-ink, #161616);
      box-shadow: none;
      opacity: 1;
    }

    :host(.sticky-action-primary) .sticky-action:disabled .action-icon,
    :host(.sticky-action-primary) .sticky-action:disabled .action-detail {
      color: var(--app-ink, #161616);
    }

    :host(.sticky-action-primary) .sticky-action:disabled .action-divider {
      background: rgba(22, 22, 22, .25);
    }
  `],
})
export class StickyActionBarComponent {
  @Input({ required: true }) icon!: string;
  @Input({ required: true }) label!: string;
      @Input() detail = '';
      @Input() disabled = false;
      @Input() placement: 'fixed' | 'inline' = 'fixed';
      @Input() appearance: 'default' | 'primary' = 'default';
      /** Only meaningful with `placement="inline"`: adds the device bottom inset. */
      @Input() safeArea = false;
      @Output() readonly action = new EventEmitter<void>();

      @HostBinding('class.in-flow')
      get isInFlow(): boolean {
        return this.placement === 'inline';
      }

      @HostBinding('class.in-flow-safe-area')
      get hasSafeAreaPadding(): boolean {
        return this.placement === 'inline' && this.safeArea;
      }

      @HostBinding('class.sticky-action-primary')
      get isPrimary(): boolean {
        return this.appearance === 'primary';
      }
    }
