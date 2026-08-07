import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface StepperItem {
  label: string;
  isCompleted: boolean;
  isActive: boolean;
  route?: string;
}

@Component({
  selector: 'app-stepper',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="stepper-container">
      <div class="stepper-wrapper">
        @for (step of steps; track step.label; let i = $index; let last = $last) {
          <div class="step-item"
            [class.completed]="step.isCompleted"
            [class.active]="step.isActive"
            [class.clickable]="step.isCompleted && step.route"
            (click)="onStepClick(step)">

            <div class="step-indicator">
              <div class="icon-wrapper">
                @if (step.isCompleted) {
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                    <polyline points="20 6 9 17 4 12"></polyline>
                  </svg>
                } @else if (step.isActive) {
                  <div class="active-dot"></div>
                } @else {
                  <span class="step-number">{{ i + 1 }}</span>
                }
              </div>
            </div>

            <div class="step-content">
              <span class="step-title">{{ step.label }}</span>
            </div>

          </div>

          @if (!last) {
            <div class="step-separator" [class.completed]="steps[i+1].isCompleted || steps[i+1].isActive">
              <div class="separator-line"></div>
            </div>
          }
        }
      </div>

      <div class="progress-section">
        <div class="progress-info">
          <span class="progress-label">Progresso</span>
          <span class="progress-value">{{ computedProgress }}%</span>
        </div>
        <div class="progress-track">
          <div class="progress-indicator" [style.width.%]="computedProgress"></div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .stepper-container {
      display: flex;
      align-items: center;
      justify-content: space-between;
      width: 100%;
      background: var(--app-surface);
      padding: 16px 32px;
      border-radius: var(--radius-lg);
      box-shadow: var(--shadow-sm);
      border: 1px solid var(--app-border-light);
      margin-bottom: 24px;
    }

    .stepper-wrapper {
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .step-item {
      display: flex;
      align-items: center;
      gap: 12px;
      transition: transform 0.2s ease;
    }

    .step-item.clickable {
      cursor: pointer;
    }

    .step-item.clickable:hover .step-title {
      color: var(--app-success-green);
    }

    .step-item.clickable:hover .icon-wrapper {
      transform: scale(1.08);
    }

    .step-indicator {
      position: relative;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .icon-wrapper {
      width: 36px;
      height: 36px;
      border-radius: var(--radius-sm);
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--app-bg-warm);
      border: 1px solid var(--app-border-light);
      color: var(--app-muted-strong);
      transition: background 0.2s ease, border-color 0.2s ease, color 0.2s ease, transform 0.2s ease;
    }

    .step-number {
      font-size: 14px;
      font-weight: 600;
      font-family: inherit;
    }

    .step-content {
      display: flex;
      flex-direction: column;
    }

    .step-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--app-text-secondary);
      transition: color 0.2s ease;
    }

    .step-item.completed .icon-wrapper {
      background: var(--app-success-green);
      border-color: var(--app-success-green);
      color: var(--app-surface);
      box-shadow: 0 2px 8px var(--app-success-green-soft);
    }
    .step-item.completed .step-title {
      color: var(--app-success-green);
    }

    .step-item.active .icon-wrapper {
      background: var(--app-surface);
      border-color: var(--app-brand);
      box-shadow: 0 0 0 4px var(--app-brand-shadow);
    }
    .step-item.active .active-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--app-brand);
    }
    .step-item.active .step-title {
      color: var(--app-text-primary);
    }

    .step-separator {
      width: 48px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .separator-line {
      height: 2px;
      width: 100%;
      background: var(--app-border-light);
      border-radius: 999px;
      transition: background 0.3s ease;
    }

    .step-separator.completed .separator-line {
      background: var(--app-success-green);
    }

    .progress-section {
      width: 180px;
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding-left: 32px;
      border-left: 1px solid var(--app-hairline-warm);
    }

    .progress-info {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .progress-label {
      font-size: 13px;
      font-weight: 600;
      color: var(--app-text-secondary);
    }

    .progress-value {
      font-size: 13px;
      font-weight: 700;
      color: var(--app-brand);
    }

    .progress-track {
      width: 100%;
      height: 6px;
      background: var(--app-hairline-warm);
      border-radius: 999px;
      overflow: hidden;
    }

    .progress-indicator {
      height: 100%;
      background: var(--app-brand);
      border-radius: 999px;
      transition: width 0.4s ease;
    }

    @media (max-width: 992px) {
      .stepper-container {
        padding: 12px 16px;
        flex-direction: column;
        align-items: flex-start;
        gap: 16px;
      }
      .progress-section {
        width: 100%;
        padding-left: 0;
        border-left: none;
        border-top: 1px solid var(--app-hairline-warm);
        padding-top: 12px;
      }
      .step-title {
        font-size: 12px;
      }
      .step-separator {
        width: 20px;
      }
    }
  `]
})
export class StepperComponent {
  @Input() steps: StepperItem[] = [];
  @Output() stepClick = new EventEmitter<StepperItem>();

  get computedProgress(): number {
    if (!this.steps.length) return 0;
    const completed = this.steps.filter(s => s.isCompleted).length;
    const active = this.steps.findIndex(s => s.isActive);
    const total = this.steps.length;
    if (active >= 0) return Math.round((active / total) * 100);
    return Math.round((completed / total) * 100);
  }

  onStepClick(step: StepperItem) {
    if (step.isCompleted && step.route) {
      this.stepClick.emit(step);
    }
  }
}