import { Component, computed, signal } from '@angular/core';
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
import { SellerHoursTimeFieldComponent } from './seller-hours-time-field.component';

@Component({
  selector: 'app-seller-hours-page',
  standalone: true,
  imports: [
    IonContent,
    IonIcon,
    IonModal,
    IonHeader,
    IonToolbar,
    IonTitle,
    IonButtons,
    IonButton,
    ConfigSubnavComponent,
    SellerHoursTimeFieldComponent,
  ],
  templateUrl: './seller-hours-page.component.html',
  styleUrls: [
    './seller-hours-page.component.scss',
  ],
})
export class SellerHoursPageComponent extends StoreHoursState {
  readonly pendingDrafts = signal<ReadonlySet<string>>(new Set<string>());
  readonly hasPendingDrafts = computed(() => this.pendingDrafts().size > 0);

  setDraftState(fieldId: string, hasDraft: boolean): void {
    const current = this.pendingDrafts();
    if (current.has(fieldId) === hasDraft) return;

    const next = new Set(current);
    if (hasDraft) {
      next.add(fieldId);
    } else {
      next.delete(fieldId);
    }
    this.pendingDrafts.set(next);
  }

  override hasUnsavedChanges(): boolean {
    return super.hasUnsavedChanges() || this.hasPendingDrafts();
  }

  override cancelChanges(): void {
    this.pendingDrafts.set(new Set<string>());
    super.cancelChanges();
  }

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
