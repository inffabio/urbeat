import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { addIcons } from 'ionicons';
import { arrowBackOutline, arrowForwardOutline, lockClosed, checkmarkCircle, alertCircle } from 'ionicons/icons';
import { IonIcon, IonSpinner } from '@ionic/angular/standalone';

addIcons({
  'arrow-back-outline': arrowBackOutline,
  'arrow-forward-outline': arrowForwardOutline,
  'lock-closed': lockClosed,
  'checkmark-circle': checkmarkCircle,
  'alert-circle': alertCircle,
});

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

@Component({
  selector: 'app-wizard-footer',
  standalone: true,
  imports: [CommonModule, IonIcon, IonSpinner],
  template: `
    <div class="wizard-footer">
      <button class="btn-back" (click)="back.emit()" [disabled]="backDisabled || isSaving">
        <ion-icon name="arrow-back-outline"></ion-icon> {{ backLabel }}
      </button>

      <div class="save-indicator">
        @switch (saveStatus) {
          @case ('saving') {
            <ion-spinner name="crescent" class="save-spinner"></ion-spinner>
            <span class="save-label saving">Salvando...</span>
          }
          @case ('saved') {
            <ion-icon name="checkmark-circle" class="save-icon saved"></ion-icon>
            <span class="save-label saved-text">Salvo</span>
          }
          @case ('error') {
            <ion-icon name="alert-circle" class="save-icon error"></ion-icon>
            <span class="save-label error-text">Erro ao salvar</span>
          }
          @default {
            @if (hasUnsavedChanges) {
              <span class="unsaved-dot"></span>
              <span class="save-label unsaved">Alterações não salvas</span>
            } @else {
              <ion-icon name="lock-closed"></ion-icon>
              <span class="save-label">Seus dados estão seguros</span>
            }
          }
        }
      </div>

      <div class="footer-actions">
        @if (hasUnsavedChanges && !isSaving) {
          <button class="btn-save" (click)="save.emit()" [disabled]="isSaving">
            Salvar
          </button>
        }
        <button class="btn-next" (click)="next.emit()" [disabled]="nextDisabled || isSaving">
          @if (isSaving) {
            <ion-spinner name="crescent" class="next-spinner"></ion-spinner>
            Salvando...
          } @else {
            {{ nextLabel }} <ion-icon name="arrow-forward-outline"></ion-icon>
          }
        </button>
      </div>
    </div>
  `,
  styles: [`
    .wizard-footer {
      max-width: 1440px;
      margin: 32px auto 0;
      padding: 24px 32px;
      border-top: 1px solid var(--app-border-light);
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .btn-back {
      background: var(--app-surface);
      border: 1px solid var(--app-border-light);
      color: var(--app-ink-soft);
      padding: 10px 24px;
      border-radius: 999px;
      font-weight: 600;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
      font-family: var(--app-font);
    }
    .btn-back:disabled { opacity: 0.5; cursor: not-allowed; }

    .save-indicator {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: var(--app-text-secondary);
    }

    .save-spinner { width: 16px; height: 16px; color: var(--app-brand); }

    .save-icon { font-size: 18px; }
    .save-icon.saved { color: var(--app-success-green); }
    .save-icon.error { color: var(--app-brand); }

    .save-label.saving { color: var(--app-brand); font-weight: 600; }
    .save-label.saved-text { color: var(--app-success-green); font-weight: 600; }
    .save-label.error-text { color: var(--app-brand); font-weight: 600; }
    .save-label.unsaved { color: var(--app-ink-soft); font-weight: 600; }

    .unsaved-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--app-brand);
      flex-shrink: 0;
    }

    .footer-actions { display: flex; align-items: center; gap: 12px; }

    .btn-save {
      background: var(--app-surface);
      border: 1px solid var(--app-brand);
      color: var(--app-brand);
      padding: 10px 20px;
      border-radius: 999px;
      font-weight: 600;
      cursor: pointer;
      font-size: 14px;
      font-family: var(--app-font);
      white-space: nowrap;
      transition: background .15s ease;
    }
    .btn-save:hover { background: var(--app-brand-soft); }

    .btn-next {
      background: var(--app-brand);
      color: var(--app-surface);
      border: none;
      padding: 10px 24px;
      border-radius: 999px;
      font-weight: 600;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
      font-family: var(--app-font);
      white-space: nowrap;
    }
    .btn-next:disabled { opacity: 0.5; cursor: not-allowed; }
    .next-spinner { width: 16px; height: 16px; }

    @media (max-width: 768px) {
      .wizard-footer { flex-wrap: wrap; gap: 12px; padding: 16px 20px; }
      .save-indicator { order: 3; width: 100%; justify-content: center; }
      .footer-actions { width: 100%; justify-content: flex-end; }
    }
  `]
})
export class WizardFooterComponent {
  @Input() backLabel: string = 'Voltar';
  @Input() nextLabel: string = 'Avançar';
  @Input() isSaving: boolean = false;
  @Input() backDisabled: boolean = false;
  @Input() nextDisabled: boolean = false;
  @Input() hasUnsavedChanges: boolean = false;
  @Input() saveStatus: SaveStatus = 'idle';
  @Output() back = new EventEmitter<void>();
  @Output() next = new EventEmitter<void>();
  @Output() save = new EventEmitter<void>();
}