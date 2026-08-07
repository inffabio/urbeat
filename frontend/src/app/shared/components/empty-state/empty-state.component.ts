import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonIcon } from '@ionic/angular/standalone';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  imports: [CommonModule, IonIcon],
  template: `
    <div class="empty-state">
      <ion-icon [name]="icon" aria-hidden="true" class="empty-icon"></ion-icon>
      <h3>{{ title }}</h3>
      @if (description) {
        <p>{{ description }}</p>
      }
      <div class="empty-actions">
        @if (showPrimaryAction) {
          <button type="button" class="btn-primary" (click)="primaryAction.emit()">
            {{ primaryLabel }}
          </button>
        }
        @if (showSecondaryAction) {
          <button type="button" class="btn-secondary" (click)="secondaryAction.emit()">
            {{ secondaryLabel }}
          </button>
        }
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .empty-state {
      text-align: center;
      padding: 48px 22px;
      color: var(--app-text-secondary, #5a5a63);
    }

    .empty-icon {
      font-size: 48px;
      color: var(--app-border-light, #eadfd6);
      margin-bottom: 16px;
    }

    h3 {
      font-size: 18px;
      font-weight: 700;
      color: var(--app-ink, #161616);
      margin: 0 0 8px;
    }

    p {
      margin: 0 0 20px;
      font-size: 14px;
      line-height: 1.4;
    }

    .empty-actions {
      display: flex;
      flex-direction: column;
      gap: 8px;
      align-items: center;
    }

    .btn-primary {
      height: 48px;
      padding: 0 24px;
      border: none;
      border-radius: 999px;
      background: var(--app-brand, #D54A51);
      color: #fff;
      font-family: inherit;
      font-size: 15px;
      font-weight: 700;
      cursor: pointer;
      transition: opacity .15s;

      &:hover { opacity: .9; }
    }

    .btn-secondary {
      height: 48px;
      padding: 0 24px;
      border: 1px solid var(--app-border-light, #eadfd6);
      border-radius: 999px;
      background: var(--app-surface, #fff);
      color: var(--app-brand, #D54A51);
      font-family: inherit;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: border-color .15s;

      &:hover { border-color: var(--app-brand, #D54A51); }
    }
  `],
})
export class EmptyStateComponent {
  @Input() icon = 'search-outline';
  @Input() title = 'Nenhum resultado';
  @Input() description = '';
  @Input() primaryLabel = '';
  @Input() secondaryLabel = '';
  @Input() showPrimaryAction = false;
  @Input() showSecondaryAction = false;
  @Output() primaryAction = new EventEmitter<void>();
  @Output() secondaryAction = new EventEmitter<void>();
}
