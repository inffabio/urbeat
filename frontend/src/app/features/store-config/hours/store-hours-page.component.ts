import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  IonContent, IonIcon, IonModal, IonHeader, IonToolbar,
  IonTitle, IonButtons, IonButton
} from '@ionic/angular/standalone';
import { WizardFooterComponent } from '../../../shared/components/wizard-footer/wizard-footer.component';
import { WizardHeaderComponent } from '../../../shared/components/wizard-header/wizard-header.component';
import { createStepperSteps } from '../../../shared/config/wizard-steps.config';
import { ToastService } from '../../../core/services/toast.service';
import { StoreHoursState } from '../../../shared/state/store-hours.state';

@Component({
  selector: 'app-store-hours-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    IonContent, IonIcon, IonModal, IonHeader, IonToolbar,
    IonTitle, IonButtons, IonButton,
    WizardHeaderComponent, WizardFooterComponent,
  ],
  templateUrl: './store-hours-page.component.html',
  styleUrl: './store-hours-page.component.scss',
  host: { '[class.urbeat-onboarding]': '!isDashboardView()' },
})
export class StoreHoursPageComponent extends StoreHoursState {
  readonly stepperSteps = createStepperSteps(1);

  private readonly wizardRouter = inject(Router);
  private readonly wizardToast = inject(ToastService);

  async goNext(): Promise<void> {
    if (!this.anyOpen()) {
      this.wizardToast.showError('Abra pelo menos um dia para continuar.');
      return;
    }
    const errors = this.validateScheduleForSubmit();
    if (errors.length > 0) {
      this.wizardToast.showGrouped(errors.slice(0, 3).map(e => ({ type: 'error' as const, text: e })));
      return;
    }
    const success = await this.saveHours();
    if (success) this.wizardRouter.navigate(['/configurar-loja/entrega']);
  }

  goBack(): void {
    this.wizardRouter.navigate(['/configurar-loja']);
  }
}
