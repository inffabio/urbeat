import { CommonModule } from '@angular/common';
import { Component, computed, signal, ElementRef, ViewChild } from '@angular/core';
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

  @ViewChild('neighborhoodNameInput') private neighborhoodNameInput?: ElementRef<HTMLInputElement>;

  readonly editorEntry = computed(() => {
    const group = this.selectedAreaGroup();
    if (!group) return [];
    return [{ index: this.selectedAreaIndex() ?? -1, group }];
  });

  setNeighborhoodPage(page: number): void {
    this.neighborhoodPage.set(Math.max(1, Math.min(page, this.neighborhoodPageCount())));
  }

  setAreaSearchFilter(value: string): void {
    this.areaSearchFilter.set(value);
    this.neighborhoodPage.set(1);
  }

  setAreaStatusFilter(value: 'all' | 'active' | 'paused'): void {
    this.areaStatusFilter.set(value);
    this.neighborhoodPage.set(1);
  }

  selectArea(index: number): void {
    this.activeRowIndex.set(index);
    this.focusNeighborhoodName();
  }

  onRowKeydown(event: KeyboardEvent, index: number): void {
    const target = event.target as HTMLElement | null;
    if (target?.closest('button')) {
      return;
    }

    if (event.key === 'Enter' || event.key === ' ' || event.key === 'Spacebar') {
      event.preventDefault();
      this.selectArea(index);
    }
  }

  private focusNeighborhoodName(): void {
    setTimeout(() => this.neighborhoodNameInput?.nativeElement?.focus(), 0);
  }

  selectedAreaIndex(): number | null {
    const active = this.activeRowIndex();
    if (active !== null && active >= 0 && active < this.areas.length) {
      return active;
    }

    return null;
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

  override async removeArea(index: number): Promise<void> {
    const name = this.areas.at(index)?.value?.neighborhood?.trim() || `Bairro ${index + 1}`;
    if (!window.confirm(`Excluir o bairro "${name}"?`)) {
      return;
    }

    await super.removeArea(index);
  }
}
