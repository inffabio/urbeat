import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormGroup, FormsModule, ReactiveFormsModule } from '@angular/forms';
import {
  IonButton,
  IonButtons,
  IonContent,
  IonHeader,
  IonIcon,
  IonItem,
  IonLabel,
  IonList,
  IonModal,
  IonNote,
  IonSearchbar,
  IonTitle,
  IonToolbar,
} from '@ionic/angular/standalone';
import { WizardFooterComponent } from '../../shared/components/wizard-footer/wizard-footer.component';
import { WizardHeaderComponent } from '../../shared/components/wizard-header/wizard-header.component';
import { ConfigSubnavComponent } from '../seller-shell/config-subnav.component';
import { StoreDeliveryPageComponent } from '../store-config/delivery/store-delivery-page.component';

@Component({
  selector: 'app-seller-neighborhoods-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    IonContent,
    IonHeader,
    IonToolbar,
    IonTitle,
    IonButtons,
    IonButton,
    IonIcon,
    IonModal,
    IonSearchbar,
    IonList,
    IonItem,
    IonLabel,
    ConfigSubnavComponent,
  ],
  templateUrl: './seller-neighborhoods-page.component.html',
  styleUrls: [
    './seller-neighborhoods-page.component.scss',
  ],
})
export class SellerNeighborhoodsPageComponent extends StoreDeliveryPageComponent {
  readonly neighborhoodPage = signal(1);
  readonly neighborhoodPageSize = 6;
  readonly neighborhoodPageCount = computed(() => Math.max(1, Math.ceil(this.filteredAreaIndices().length / this.neighborhoodPageSize)));
  readonly pagedAreaIndices = computed(() => {
    const pages = this.neighborhoodPageCount();
    const page = Math.min(this.neighborhoodPage(), pages);
    const start = (page - 1) * this.neighborhoodPageSize;
    return this.filteredAreaIndices().slice(start, start + this.neighborhoodPageSize);
  });

  setNeighborhoodPage(page: number): void {
    this.neighborhoodPage.set(Math.max(1, Math.min(page, this.neighborhoodPageCount())));
  }

  selectArea(index: number): void {
    this.activeRowIndex.set(index);
  }

  selectedAreaIndex(): number | null {
    const active = this.activeRowIndex();
    if (active !== null && active >= 0 && active < this.areas.length) {
      return active;
    }

    return this.areas.length > 0 ? 0 : null;
  }

  currentAreaIndex(): number {
    return this.selectedAreaIndex() ?? -1;
  }

  selectedAreaGroup(): FormGroup | null {
    const index = this.selectedAreaIndex();
    return index === null ? null : (this.areas.at(index) as FormGroup);
  }

  averageDeliveryFee(): string {
    if (this.areas.length === 0) {
      return 'R$ 0,00';
    }

    let total = 0;
    for (let i = 0; i < this.areas.length; i++) {
      const raw = String(this.areas.at(i).value.deliveryFee ?? '').replace(/\./g, '').replace(',', '.');
      total += Number.parseFloat(raw) || 0;
    }

    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(total / this.areas.length);
  }

  activeAreaCount(): number {
    return this.areas.controls.filter(area => area.value.isActive !== false).length;
  }

  averageMinimumOrder(): string {
    return this.averageAreaMoney('minimumOrderValue');
  }

  averageFreeShipping(): string {
    return this.averageAreaMoney('freeShippingThreshold');
  }

  private averageAreaMoney(field: 'minimumOrderValue' | 'freeShippingThreshold'): string {
    const values = this.areas.controls
      .map(area => Number.parseFloat(String(area.value[field] ?? '').replace(/\./g, '').replace(',', '.')))
      .filter(value => Number.isFinite(value) && value > 0);
    if (values.length === 0) return 'R$ 0,00';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(values.reduce((sum, value) => sum + value, 0) / values.length);
  }

  formatMoneyAreaField(index: number, field: 'minimumOrderValue' | 'freeShippingThreshold'): void {
    const control = this.areas.at(index)?.get(field);
    if (control?.value) control.setValue(this.formatCurrencyString(control.value));
  }

  override async removeArea(index: number): Promise<void> {
    const name = this.areas.at(index)?.value?.neighborhood?.trim() || `Bairro ${index + 1}`;
    if (!window.confirm(`Excluir o bairro "${name}"?`)) {
      return;
    }

    await super.removeArea(index);
  }
}
