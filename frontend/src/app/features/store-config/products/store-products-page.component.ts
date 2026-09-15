import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  IonContent, IonIcon, IonSpinner,
  IonReorderGroup, IonReorder
} from '@ionic/angular/standalone';
import { WizardFooterComponent } from '../../../shared/components/wizard-footer/wizard-footer.component';
import { WizardHeaderComponent } from '../../../shared/components/wizard-header/wizard-header.component';
import { CardapioMenuTabsComponent } from '../../../shared/components/cardapio-menu-tabs/cardapio-menu-tabs.component';
import { SubscriptionBannerComponent } from '../../../shared/components/subscription-banner/subscription-banner.component';
import { createStepperSteps } from '../../../shared/config/wizard-steps.config';
import { StoreProductsState } from '../../../shared/state/store-products.state';

@Component({
  selector: 'app-store-products-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, IonSpinner, IonReorderGroup, IonReorder, WizardHeaderComponent, WizardFooterComponent, CardapioMenuTabsComponent, SubscriptionBannerComponent],
  templateUrl: './store-products-page.component.html',
  styleUrl: '../../../shared/styles/store-products.shared.scss',
  host: { '[class.urbeat-onboarding]': '!isDashboardView()' },
})
export class StoreProductsPageComponent extends StoreProductsState {
  readonly stepperSteps = createStepperSteps(3);

  private readonly wizardRouter = inject(Router);

  goBack(): void {
    this.wizardRouter.navigate(['/configurar-loja/entrega']);
  }

  goNext(): void {
    if (!this.validateProductsForAdvance()) return;
    this.wizardRouter.navigate(['/configurar-loja/publicar']);
  }
}
