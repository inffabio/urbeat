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
          (click)="selectTab(tab.id)"
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
      scrollbar-width: thin;
      scrollbar-color: rgba(213,74,81,.35) transparent;
      gap: 10px;
      width: calc(100% + 44px);
      margin-left: -22px;
      margin-right: -22px;
      padding: 12px 22px 10px;
      cursor: grab;
      user-select: none;
      touch-action: pan-x;
      overscroll-behavior-x: contain;

      &:active { cursor: grabbing; }

      &::-webkit-scrollbar { height: 4px; display: block; }
      &::-webkit-scrollbar-track { background: transparent; }
      &::-webkit-scrollbar-thumb {
        background: rgba(213,74,81,.35);
        border-radius: 999px;
      }
    }

    .tab {
      flex: 0 0 auto;
      min-width: max-content;
      white-space: nowrap;
      height: 42px;
      padding: 0 15px;
      border-radius: 999px;
      border: 1px solid var(--app-border-light, #eadfd6);
      background: var(--app-surface, #fff);
      color: var(--app-ink, #161616);
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      font-family: inherit;
      transition: background .2s, color .2s, border-color .2s;

      &:hover {
        background: var(--app-brand-soft, #FDECEE);
        color: var(--app-brand, #D54A51);
      }

      &.active {
        background: var(--app-brand, #D54A51);
        color: #fff;
        border-color: var(--app-brand, #D54A51);
        font-weight: 800;
      }
    }
  `],
})
export class CategoryTabsComponent {
  @Input({ required: true }) tabs!: CategoryTab[];
  @Input() activeId: string | null = null;
  @Output() select = new EventEmitter<string>();

  selectTab(id: string): void {
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
