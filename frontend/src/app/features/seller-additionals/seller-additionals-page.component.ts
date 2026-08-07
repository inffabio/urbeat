import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonicModule } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { calendarOutline, chevronDownOutline, createOutline, notificationsOutline, refreshOutline, trashOutline } from 'ionicons/icons';
import { forkJoin } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { CardapioMenuTabsComponent } from '../../shared/components/cardapio-menu-tabs/cardapio-menu-tabs.component';
import { StoreAdditional, StoreAdditionalGroup, StoreAdditionalRequest } from '../../shared/models/product.model';

addIcons({ 'calendar-outline': calendarOutline, 'chevron-down-outline': chevronDownOutline, 'create-outline': createOutline, 'notifications-outline': notificationsOutline, 'refresh-outline': refreshOutline, 'trash-outline': trashOutline });

@Component({
  selector: 'app-seller-additionals-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonicModule, CardapioMenuTabsComponent],
  templateUrl: './seller-additionals-page.component.html',
  styleUrl: './seller-additionals-page.component.scss',
})
export class SellerAdditionalsPageComponent implements OnInit {
  private readonly stores = inject(StoreService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly saving = signal(false);
  readonly storeId = signal<string | null>(null);
  readonly additionals = signal<StoreAdditional[]>([]);
  readonly groups = signal<StoreAdditionalGroup[]>([]);
  readonly editingAdditionalId = signal<string | null>(null);
  readonly formName = signal('');
  readonly formDescription = signal('');
  readonly formGroupId = signal('');
  readonly formPrice = signal<number | null>(0);
  readonly formIsActive = signal(true);
  readonly headerSummary = computed(() => `Hoje, ${new Intl.DateTimeFormat('pt-BR', { day: 'numeric', month: 'long' }).format(new Date())}`);
  readonly formTitle = computed(() => this.editingAdditionalId() ? 'Editar adicional' : 'Novo adicional');

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.stores.getMyStore().subscribe({
      next: (store) => {
        this.storeId.set(store.id);
        forkJoin({ additionals: this.stores.getStoreAdditionals(store.id), groups: this.stores.getStoreAdditionalGroups(store.id) }).subscribe({
          next: ({ additionals, groups }) => {
            this.additionals.set(additionals);
            this.groups.set(groups);
            this.loading.set(false);
          },
          error: () => this.failLoad(),
        });
      },
      error: () => this.failLoad(),
    });
  }

  startEdit(additional: StoreAdditional): void {
    this.editingAdditionalId.set(additional.id);
    this.formName.set(additional.name);
    this.formDescription.set(additional.description ?? '');
    this.formGroupId.set(additional.groupId);
    this.formPrice.set(additional.price);
    this.formIsActive.set(additional.isActive);
  }

  saveAdditional(): void {
    const storeId = this.storeId();
    const name = this.formName().trim();
    const groupId = this.formGroupId();
    const price = this.formPrice();
    if (!storeId || !name || !groupId || price === null || price < 0 || this.saving()) return;

    const body: StoreAdditionalRequest = { name, description: this.formDescription().trim(), groupId, price, isActive: this.formIsActive(), displayOrder: this.editingAdditionalId() ? this.currentDisplayOrder() : this.additionals().length + 1 };
    const editingId = this.editingAdditionalId();
    this.saving.set(true);
    const request = editingId ? this.stores.updateStoreAdditional(storeId, editingId, body) : this.stores.createStoreAdditional(storeId, body);
    request.subscribe({
      next: (additional) => {
        this.additionals.update(items => editingId ? items.map(item => item.id === additional.id ? additional : item) : [...items, additional]);
        this.resetForm();
        this.saving.set(false);
        this.toast.showSuccess(editingId ? 'Adicional atualizado.' : 'Adicional criado.');
      },
      error: () => {
        this.saving.set(false);
        this.toast.showError(editingId ? 'Não foi possível atualizar o adicional.' : 'Não foi possível criar o adicional.');
      },
    });
  }

  toggleAdditional(additional: StoreAdditional): void {
    const storeId = this.storeId();
    if (!storeId) return;
    this.stores.toggleStoreAdditional(storeId, additional.id, !additional.isActive).subscribe({
      next: updated => this.additionals.update(items => items.map(item => item.id === updated.id ? updated : item)),
      error: () => this.toast.showError('Não foi possível atualizar o status do adicional.'),
    });
  }

  deleteAdditional(additional: StoreAdditional): void {
    const storeId = this.storeId();
    if (!storeId) return;
    if (additional.productCount > 0) {
      this.toast.showError(`Não é possível excluir "${additional.name}" porque ele está associado a produto(s).`);
      return;
    }
    if (!window.confirm(`Deseja realmente excluir o adicional "${additional.name}"?`)) return;
    this.stores.deleteStoreAdditional(storeId, additional.id).subscribe({
      next: () => {
        this.additionals.update(items => items.filter(item => item.id !== additional.id));
        if (this.editingAdditionalId() === additional.id) this.resetForm();
        this.toast.showSuccess('Adicional excluído.');
      },
      error: (error: HttpErrorResponse) => this.toast.showError(error.status === 409 ? 'Não é possível excluir um adicional associado a produtos.' : 'Não foi possível excluir o adicional.'),
    });
  }

  cancelEdit(): void { this.resetForm(); }

  formatCurrency(value: number): string { return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value); }

  private currentDisplayOrder(): number { return this.additionals().find(item => item.id === this.editingAdditionalId())?.displayOrder ?? 1; }

  private resetForm(): void {
    this.editingAdditionalId.set(null);
    this.formName.set('');
    this.formDescription.set('');
    this.formGroupId.set('');
    this.formPrice.set(0);
    this.formIsActive.set(true);
  }

  private failLoad(): void {
    this.error.set(true);
    this.loading.set(false);
  }
}
