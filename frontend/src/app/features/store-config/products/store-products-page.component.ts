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
import { Product } from '../../../shared/models/product.model';
import { ToastService } from '../../../core/services/toast.service';

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
  private readonly wizardToast = inject(ToastService);

  override newProduct(): void {
    if (!this.allowDraftSwitch()) return;
    super.newProduct();
    this.formDirty.set(false);
    this.saveStatus.set('idle');
  }

  override selectProduct(product: Product): void {
    if (!this.allowDraftSwitch()) return;
    super.selectProduct(product);
    this.formDirty.set(false);
    this.saveStatus.set('idle');
  }

  override copyProductOptions(product: Product): void {
    if (!this.allowDraftSwitch()) return;
    super.copyProductOptions(product);
    this.saveStatus.set('idle');
  }

  /**
   * Switching the editor target discards the in-memory draft, so the wizard
   * asks first. The dashboard keeps its existing silent switch behavior.
   */
  private allowDraftSwitch(): boolean {
    if (this.isSaving()) {
      this.wizardToast.showInfo('Aguarde o salvamento do produto em andamento.');
      return false;
    }
    if (!this.hasUnsavedChanges()) return true;
    return window.confirm('Você tem alterações não salvas neste produto. Deseja descartá-las?');
  }

  goBack(): void {
    this.wizardRouter.navigate(['/configurar-loja/entrega']);
  }

  async goNext(): Promise<void> {
    if (this.hasUnsavedChanges()) {
      if (!this.hasProductDraft()) {
        this.wizardToast.showError(
          'Preencha os dados do produto em edição (nome, categoria, preço e imagem) para salvar antes de avançar.',
        );
        return;
      }

      // Persist the pending draft through the same flow as the save button so
      // advancing never silently discards an edit.
      await this.saveDraft();
      if (this.hasUnsavedChanges()) return;
    }

    if (!this.validateProductsForAdvance()) return;
    this.wizardRouter.navigate(['/configurar-loja/publicar']);
  }
}
