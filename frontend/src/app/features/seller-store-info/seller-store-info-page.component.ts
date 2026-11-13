import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IonIcon,
  IonSpinner,
} from '@ionic/angular/standalone';
import { ConfigSubnavComponent } from '../seller-shell/config-subnav.component';
import { StoreConfigPageComponent } from '../store-config/store-config-page.component';

/** Word the seller must type before the irreversible deletion is sent. */
const DELETE_CONFIRMATION_WORD = 'excluir';

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

  // ─── Zona destrutiva ──────────────────────────────────────
  readonly deletePanelOpen = signal(false);
  readonly deleting = signal(false);
  readonly deleteConfirmation = signal('');
  readonly deleteError = signal<string | null>(null);
  /** The seller may delete only after typing the confirmation word. */
  readonly deleteConfirmed = computed(
    () => this.deleteConfirmation().trim().toLowerCase() === DELETE_CONFIRMATION_WORD,
  );
  readonly canSubmitDeletion = computed(() => this.deleteConfirmed() && !this.deleting());

  openDeletePanel(): void {
    if (!this.existingStoreId()) {
      this.toast.showWarning('Nenhuma loja encontrada para excluir.');
      return;
    }
    this.deleteError.set(null);
    this.deleteConfirmation.set('');
    this.deletePanelOpen.set(true);
  }

  cancelDeletion(): void {
    if (this.deleting()) return;
    this.closeDeletePanel();
  }

  confirmDeletion(): void {
    const storeId = this.existingStoreId();
    if (!storeId || this.deleting()) return;

    if (!this.deleteConfirmed()) {
      this.deleteError.set('Digite "excluir" para confirmar a exclusão da loja.');
      return;
    }

    this.deleting.set(true);
    this.deleteError.set(null);

    this.storeService.deleteStore(storeId).subscribe({
      next: () => this.handleDeletedStore(),
      error: (error: HttpErrorResponse) => this.handleDeleteError(error),
    });
  }

  private handleDeletedStore(): void {
    this.deleting.set(false);
    this.closeDeletePanel();
    // The seller account keeps its token; only the store is gone, so the wizard
    // that creates a new store is the correct destination instead of the login.
    this.toast.showSuccess('Loja excluída. Crie uma nova loja quando quiser voltar a vender.');
    void this.router.navigate(['/configurar-loja']);
  }

  private handleDeleteError(error: HttpErrorResponse): void {
    this.deleting.set(false);
    const status = error?.status;

    if (status === 401) {
      this.closeDeletePanel();
      this.authService.logout();
      this.toast.showError('Sua sessão expirou. Entre novamente para continuar.');
      void this.router.navigate(['/login-vendedor']);
      return;
    }

    if (status === 403) {
      this.closeDeletePanel();
      this.toast.showError('Você não tem permissão para excluir esta loja.');
      return;
    }

    if (status === 404) {
      this.closeDeletePanel();
      this.toast.showInfo('Esta loja já não existe. Crie uma nova loja para voltar a vender.');
      void this.router.navigate(['/configurar-loja']);
      return;
    }

    const message = 'Não foi possível excluir a loja. Tente novamente.';
    this.deleteError.set(message);
    this.toast.showError(message);
  }

  private closeDeletePanel(): void {
    this.deletePanelOpen.set(false);
    this.deleteConfirmation.set('');
    this.deleteError.set(null);
  }

  protected override resolveAtendimentoDefaults(
    delivery: boolean | undefined,
    pickup: boolean | undefined,
  ): { delivery: boolean; pickup: boolean } {
    const resolved = super.resolveAtendimentoDefaults(delivery, pickup);
    if (!resolved.delivery && !resolved.pickup) {
      return { delivery: true, pickup: false };
    }
    return resolved;
  }
}
