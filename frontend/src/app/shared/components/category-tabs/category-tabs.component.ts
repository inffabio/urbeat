import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface CategoryTab {
  id: string;
  name: string;
}

@Component({
  selector: 'app-category-tabs',
  standalone: true,
  imports: [CommonModule],
  template: `
    <nav class="tabs" aria-label="Categorias" role="tablist" (keydown)="onKeydown($event)">
      @for (tab of tabs; track tab.id) {
        <button
          type="button"
          class="tab"
          [class.active]="tab.id === activeId"
          [attr.aria-selected]="tab.id === activeId"
          (click)="selectTab(tab.id, $event)"
          role="tab"
          [tabIndex]="tab.id === activeId ? 0 : -1">
          {{ tab.name }}
        </button>
      }
    </nav>
  `,
  styles: [`
    :host {
      display: block;
      position: sticky;
      top: env(safe-area-inset-top, 0px);
      z-index: 20;
      background: var(--app-surface, #fff);
    }

    .tabs {
      display: flex;
      flex-wrap: nowrap;
      overflow-x: auto;
      overflow-y: hidden;
      -webkit-overflow-scrolling: touch;
      scrollbar-width: none;
      gap: 8px;
      width: 100%;
      margin: 0;
      padding: 6px 0 4px;
      cursor: grab;
      user-select: none;
      touch-action: pan-x;
      overscroll-behavior-x: contain;

      &:active { cursor: grabbing; }

      &::-webkit-scrollbar { height: 0; }
    }

    @media (min-width: 900px) {
      .tabs {
        scrollbar-width: thin;
        scrollbar-color: rgba(213,74,81,.35) transparent;

        &::-webkit-scrollbar { height: 4px; }
        &::-webkit-scrollbar-track { background: transparent; }
        &::-webkit-scrollbar-thumb {
          background: rgba(213,74,81,.35);
          border-radius: 999px;
        }
       }
    }

    .tab {
      flex: 0 0 auto;
      min-width: max-content;
      white-space: nowrap;
      min-height: 44px;
      padding: 0 10px;
       border-radius: 999px;
       border: 0;
       background: transparent;
       color: var(--app-ink, #161616);
      font-size: 11px;
       font-weight: 700;
       cursor: pointer;
       font-family: inherit;
       position: relative;
       isolation: isolate;
       transition: background .2s, color .2s, border-color .2s;

       &::before {
         content: "";
         position: absolute;
         z-index: -1;
          inset: 4px 0;
         border: 1px solid var(--app-border-light, #eadfd6);
         border-radius: 999px;
         background: var(--app-surface, #fff);
         transition: background .2s, border-color .2s;
       }

       &:hover {
         color: var(--app-brand, #D54A51);

         &::before {
           background: var(--app-brand-soft, #FDECEE);
           border-color: var(--app-brand-shadow, rgba(213,74,81,.18));
         }
       }

       &.active {
         color: #fff;
         font-weight: 800;

         &::before {
           background: var(--app-brand, #D54A51);
           border-color: var(--app-brand, #D54A51);
         }
       }
    }
  `],
})
export class CategoryTabsComponent {
  @Input({ required: true }) tabs!: CategoryTab[];
  @Input() activeId: string | null = null;
  @Output() select = new EventEmitter<string>();

  selectTab(id: string, event?: Event): void {
    const tab = event?.currentTarget as HTMLElement | null;
    const tabs = tab?.parentElement;
    if (tab && tabs) {
      const left = Math.max(0, tab.offsetLeft - (tabs.clientWidth - tab.offsetWidth) / 2);
      tabs.scrollTo({ left, behavior: 'smooth' });
    }
    this.select.emit(id);
  }

  onKeydown(event: KeyboardEvent): void {
    const tabs = Array.from(
      (event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]'),
    );
    const currentIndex = tabs.findIndex((t) => t.getAttribute('tabindex') === '0');
    if (currentIndex === -1) return;

    let nextIndex = currentIndex;
    if (event.key === 'ArrowRight') {
      nextIndex = (currentIndex + 1) % tabs.length;
    } else if (event.key === 'ArrowLeft') {
      nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    } else {
      return;
    }

    event.preventDefault();
    tabs[nextIndex].focus();
    tabs[nextIndex].click();
  }
}
