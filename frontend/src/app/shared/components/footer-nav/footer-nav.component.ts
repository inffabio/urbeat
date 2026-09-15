import { Component, Input, Output, EventEmitter, AfterViewInit, OnDestroy, inject, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonIcon } from '@ionic/angular/standalone';

export interface FooterNavItem {
  id: string;
  icon: string;
  label: string;
  active?: boolean;
  disabled?: boolean;
  badge?: number;
  badgeLabel?: string;
  ariaExpanded?: boolean;
  ariaControls?: string;
}

@Component({
  selector: 'app-footer-nav',
  standalone: true,
  imports: [CommonModule, IonIcon],
  template: `
    <div class="footer-nav-safe-zone">
      <footer class="footer-nav" aria-label="Navegacao principal">
        @for (item of items; track item.id) {
          <button
            type="button"
            [class.active]="item.active"
            [class.disabled]="item.disabled"
            [attr.aria-disabled]="item.disabled ? true : undefined"
            [attr.aria-hidden]="inert ? 'true' : undefined"
            [attr.aria-current]="item.active ? 'page' : undefined"
            [attr.aria-expanded]="item.ariaExpanded ?? undefined"
            [attr.aria-controls]="item.ariaControls ?? undefined"
            [attr.aria-haspopup]="item.ariaControls ? 'menu' : undefined"
            [attr.data-footer-id]="item.id"
            (click)="!inert && !item.disabled && select.emit(item.id)"
            [attr.tabindex]="inert || item.disabled ? -1 : 0">
            <span class="footer-icon-wrap">
              <ion-icon
                [name]="item.icon"
                [class.cart-has-items]="item.badge && item.badge > 0"
                aria-hidden="true"></ion-icon>
              @if (item.badge && item.badge > 0) {
                <span
                  class="footer-nav-badge footer-nav-badge-over-icon"
                  [class.footer-nav-badge-wide]="item.badge >= 10"
                  aria-label="{{ item.badge }} {{ item.badgeLabel || 'itens' }}">{{ item.badge }}</span>
              }
            </span>
            <span>{{ item.label }}</span>
          </button>
        }
      </footer>
    </div>
  `,
  styles: [`
     :host { display: block; position: relative; z-index: 70; }
     :host(.sheet-open) { pointer-events: none; }

     .footer-nav-safe-zone {
       position: relative;
        z-index: 70;
       background: var(--app-surface, #fff);
       padding-bottom: max(8px, env(safe-area-inset-bottom, 0px));
       width: min(430px, 100%);
       max-width: 100%;
       margin: 0 auto;
       border-radius: 18px 18px 0 0;
     }

    .footer-nav {
       min-height: 64px;
      background: rgba(255, 255, 255, .96);
       border-top: 1px solid var(--app-border-light, #eadfd6);
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      align-items: center;
       padding: 4px 4px 6px;
       width: 100%;
       max-width: none;
       margin: 0;
      box-sizing: border-box;
    }

    .footer-nav button {
      min-height: 54px;
      display: grid;
      place-items: center;
      gap: 4px;
      padding: 0;
      border: 0;
      background: transparent;
      color: #505258;
      font-family: inherit;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      transition: color .18s ease;

      .footer-icon-wrap {
        position: relative;
        display: grid;
        place-items: center;
        min-width: 24px;
        min-height: 24px;
      }

      ion-icon { font-size: 22px; line-height: 1; }

      .footer-nav-badge {
        position: absolute;
        top: 1px;
        right: -5px;
        min-width: 18px;
        height: 18px;
        padding: 0 5px;
        border: 2px solid var(--app-surface, #fff);
        border-radius: 999px;
          background: var(--app-brand, #D54A51);
        color: #fff;
         font-size: 10px;
         line-height: 14px;
         font-weight: 800;
         text-align: center;
         font-variant-numeric: tabular-nums;
         white-space: nowrap;
         box-sizing: border-box;
       }

       .footer-nav-badge-wide {
         min-width: 28px;
         padding-inline: 6px;
       }

       &.active, &.active ion-icon, ion-icon.cart-has-items { color: var(--app-brand, #D54A51); }

      &.disabled {
        opacity: .4;
        cursor: not-allowed;
        pointer-events: none;
      }
    }

    @media (max-width: 400px) {
      .footer-nav button { font-size: 11px; }
      .footer-nav button ion-icon { font-size: 21px; }
    }
  `],
})
export class FooterNavComponent implements AfterViewInit, OnDestroy {
  @Input({ required: true }) items!: FooterNavItem[];
  @Input() inert = false;
  @Output() select = new EventEmitter<string>();
  @Output() heightChange = new EventEmitter<number>();

  private readonly host = inject(ElementRef<HTMLElement>);
  private resizeObserver?: ResizeObserver;

  ngAfterViewInit(): void {
    if (typeof ResizeObserver === 'undefined') return;
    const safeZone = this.host.nativeElement.querySelector('.footer-nav-safe-zone') as HTMLElement | null;
    if (!safeZone) return;
    this.resizeObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const height = this.measureSafeZoneHeight(entry);
        if (Number.isFinite(height) && height >= 0) {
          this.heightChange.emit(height);
        }
      }
    });
    this.resizeObserver.observe(safeZone);
  }

  private measureSafeZoneHeight(entry: ResizeObserverEntry): number {
    const box: unknown = (entry as ResizeObserverEntry & {
      borderBoxSize?: ReadonlyArray<ResizeObserverSize> | ResizeObserverSize;
    }).borderBoxSize;
    const blockSize = Array.isArray(box)
      ? (box[0] as ResizeObserverSize | undefined)?.blockSize
      : (box as ResizeObserverSize | undefined)?.blockSize;
    if (blockSize !== undefined) return blockSize;
    const safeZone = entry.target as HTMLElement | undefined;
    const boxHeight = safeZone?.getBoundingClientRect().height;
    if (boxHeight !== undefined) return boxHeight;
    return entry.contentRect.height;
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }
}
