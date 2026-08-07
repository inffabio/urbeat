import { Component, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { StepperComponent, StepperItem } from '../stepper/stepper.component';

@Component({
  selector: 'app-wizard-header',
  standalone: true,
  imports: [CommonModule, StepperComponent],
  template: `
    <header class="wizard-header">
      <div class="wizard-header-inner">
        <div class="wizard-logo">
          <img src="assets/images/logo_v2.svg" alt="Urbeat" class="wizard-logo-img" />
        </div>
        <app-stepper [steps]="steps" (stepClick)="onStepClick($event)"></app-stepper>
        @if (isPublished) {
          <span class="published-badge">Publicado</span>
        } @else {
          <span class="unpublished-badge">Não publicado</span>
        }
      </div>
    </header>
  `,
  styles: [`
    .wizard-header {
      background: var(--app-surface);
      border-bottom: 1px solid var(--app-border-light);
      position: sticky;
      top: 0;
      z-index: 50;
    }
    .wizard-header-inner {
      max-width: 1440px;
      margin: 0 auto;
      padding: 12px 32px;
      display: flex;
      align-items: center;
      gap: 24px;
    }
    .wizard-logo {
      display: flex;
      align-items: center;
      min-width: 110px;
      flex-shrink: 0;
    }
    .wizard-logo-img {
      height: 32px;
      width: auto;
      object-fit: contain;
    }
    .unpublished-badge {
      display: inline-block;
      font-size: 11px;
      font-weight: 600;
      padding: 5px 14px;
      border-radius: 999px;
      background: var(--app-brand-soft);
      color: var(--app-brand-dark);
      letter-spacing: 0.3px;
      white-space: nowrap;
      margin-left: auto;
      flex-shrink: 0;
    }
    .published-badge {
      display: inline-block;
      font-size: 11px;
      font-weight: 600;
      padding: 5px 14px;
      border-radius: 999px;
      background: var(--app-success-green-soft);
      color: var(--app-success-green);
      letter-spacing: 0.3px;
      white-space: nowrap;
      margin-left: auto;
      flex-shrink: 0;
    }
    @media (max-width: 768px) {
      .wizard-header-inner {
        flex-direction: column;
        padding: 12px 16px;
        gap: 12px;
      }
      .unpublished-badge, .published-badge {
        margin-left: 0;
      }
    }
  `]
})
export class WizardHeaderComponent {
  @Input() steps: StepperItem[] = [];
  @Input() isPublished: boolean = false;
  @Input() hasUnsavedChanges: boolean = false;

  private readonly router = inject(Router);

  onStepClick(step: StepperItem): void {
    if (!step.route) return;
    if (this.hasUnsavedChanges) {
      const confirmed = confirm('Você tem alterações não salvas. Deseja sair sem salvar?');
      if (!confirmed) return;
    }
    this.router.navigate([step.route]);
  }
}