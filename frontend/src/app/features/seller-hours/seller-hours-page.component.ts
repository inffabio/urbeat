import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IonButton,
  IonButtons,
  IonContent,
  IonHeader,
  IonIcon,
  IonModal,
  IonTitle,
  IonToolbar,
} from '@ionic/angular/standalone';
import { ConfigSubnavComponent } from '../seller-shell/config-subnav.component';
import { StoreHoursState } from '../../shared/state/store-hours.state';

@Component({
  selector: 'app-seller-hours-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    IonContent,
    IonIcon,
    IonModal,
    IonHeader,
    IonToolbar,
    IonTitle,
    IonButtons,
    IonButton,
    ConfigSubnavComponent,
  ],
  templateUrl: './seller-hours-page.component.html',
  styleUrls: [
    './seller-hours-page.component.scss',
  ],
})
export class SellerHoursPageComponent extends StoreHoursState {
  openDaysCount(): number {
    return this.weekDays.filter((day) => this.schedule()[day.id]?.isOpen).length;
  }

  daysWithIntervalCount(): number {
    return this.weekDays.filter((day) => (this.schedule()[day.id]?.shifts.length ?? 0) > 1).length;
  }

  override removeShift(dayId: string, index: number): void {
    const dayLabel = this.weekDays.find(day => day.id === dayId)?.label.toLowerCase() ?? 'este dia';
    if (!window.confirm(`Excluir o turno ${index + 1} de ${dayLabel}?`)) {
      return;
    }

    super.removeShift(dayId, index);
  }
}
