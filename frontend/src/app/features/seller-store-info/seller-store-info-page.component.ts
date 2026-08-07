import { CommonModule } from '@angular/common';
import { Component, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IonIcon,
  IonSpinner,
} from '@ionic/angular/standalone';
import { ConfigSubnavComponent } from '../seller-shell/config-subnav.component';
import { StoreConfigPageComponent } from '../store-config/store-config-page.component';

@Component({
  selector: 'app-seller-store-info-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    IonIcon,
    IonSpinner,
    ConfigSubnavComponent,
  ],
  templateUrl: './seller-store-info-page.component.html',
  styleUrls: [
    '../store-config/store-config-page.component.scss',
    './seller-store-info-page.component.scss',
  ],
})
export class SellerStoreInfoPageComponent extends StoreConfigPageComponent {
  readonly headerStatus = computed(() => {
    switch (this.saveStatus()) {
      case 'saving':
        return 'Salvando alteracoes';
      case 'saved':
        return 'Alteracoes salvas';
      case 'error':
        return 'Revisar alteracoes';
      default:
        return 'Dados publicos da loja';
    }
  });
}
