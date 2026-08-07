import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonicModule } from '@ionic/angular';
import { addIcons } from 'ionicons';
import {
  calendarOutline, checkmarkCircle, chevronDownOutline, chevronUpOutline, createOutline, ellipseOutline,
  layersOutline, notificationsOutline, refreshOutline, trashOutline,
} from 'ionicons/icons';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import { StoreService } from '../../core/services/store.service';
import { SubscriptionService } from '../../core/services/subscription.service';
import { ToastService } from '../../core/services/toast.service';
import { CardapioMenuTabsComponent } from '../../shared/components/cardapio-menu-tabs/cardapio-menu-tabs.component';
import { ProductCategory } from '../../shared/models/product.model';

addIcons({
  'calendar-outline': calendarOutline,
  'checkmark-circle': checkmarkCircle,
  'chevron-down-outline': chevronDownOutline,
  'chevron-up-outline': chevronUpOutline,
  'create-outline': createOutline,
  'ellipse-outline': ellipseOutline,
  'layers-outline': layersOutline,
  'notifications-outline': notificationsOutline,
  'refresh-outline': refreshOutline,
  'trash-outline': trashOutline,
});

@Component({
  selector: 'app-seller-categories-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonicModule, CardapioMenuTabsComponent],
  templateUrl: './seller-categories-page.component.html',
  styleUrl: './seller-categories-page.component.scss',
})
export class SellerCategoriesPageComponent implements OnInit {
  private readonly stores = inject(StoreService);
  private readonly subscriptionService = inject(SubscriptionService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly saving = signal(false);
  readonly storeId = signal<string | null>(null);
  readonly categories = signal<ProductCategory[]>([]);
  readonly productCounts = signal<Record<string, number>>({});
  readonly newName = signal('');
  readonly newDescription = signal('');
  readonly newIsActive = signal(true);
  readonly newDisplayOrder = signal(1);
  readonly editingCategoryId = signal<string | null>(null);
  readonly draggedCategoryId = signal<string | null>(null);
  readonly subscriptionStatus = signal<'ok' | 'due-soon' | 'overdue'>('ok');
  readonly subscriptionDueDate = signal('');

  readonly sortedCategories = computed(() => [...this.categories()].sort((a, b) => a.displayOrder - b.displayOrder));
  readonly headerSummary = computed(() => `Hoje, ${new Intl.DateTimeFormat('pt-BR', { day: 'numeric', month: 'long' }).format(new Date())}`);
  readonly subscriptionMessage = computed(() => {
    const date = this.subscriptionDueDate();
    if (this.subscriptionStatus() === 'overdue') return 'Sua mensalidade está pendente. Regularize para continuar recebendo pedidos.';
    if (this.subscriptionStatus() === 'due-soon') return `Sua mensalidade vence em breve.${date ? ` Próximo vencimento ${date}` : ''}`;
    return `Sua mensalidade está em dia!${date ? ` Próximo vencimento ${date}` : ''}`;
  });
  readonly formTitle = computed(() => this.editingCategoryId() ? 'Editar categoria' : 'Nova categoria');
  readonly nextDisplayOrderLabel = computed(() => `${this.sortedCategories().length + 1}ª posição da lista`);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    this.subscriptionService.getMySubscription().subscribe({
      next: (sub) => {
        if (sub.nextDueDateUtc) {
          this.subscriptionDueDate.set(formatSaoPauloDate(sub.nextDueDateUtc));
        }
        if (sub.storeBlocked) {
          this.subscriptionStatus.set('overdue');
        } else if (sub.billingStatus === 2) {
          this.subscriptionStatus.set('due-soon');
        } else {
          this.subscriptionStatus.set('ok');
        }
      },
      error: () => {},
    });

    this.stores.getMyStore().subscribe({
      next: (store) => {
        this.storeId.set(store.id);
        forkJoin({
          categories: this.stores.getStoreCategories(store.id),
          products: this.stores.getStoreProducts(store.id).pipe(catchError(() => of([]))),
        }).subscribe({
          next: ({ categories, products }) => {
            this.categories.set(categories);
            this.productCounts.set(products.reduce<Record<string, number>>((counts, product) => {
              counts[product.categoryId] = (counts[product.categoryId] ?? 0) + 1;
              return counts;
            }, {}));
            this.newDisplayOrder.set(categories.length + 1);
            this.loading.set(false);
          },
          error: () => this.failLoad(),
        });
      },
      error: () => this.failLoad(),
    });
  }

  addCategory(): void {
    this.saveCategory();
  }

  saveCategory(): void {
    const storeId = this.storeId();
    const name = this.newName().trim();
    if (!storeId || !name || this.saving()) return;

    this.saving.set(true);
    const editingId = this.editingCategoryId();
    const body = {
      name,
      description: this.newDescription().trim() || undefined,
      displayOrder: this.newDisplayOrder(),
      isActive: this.newIsActive(),
      isFeatured: false,
    };
    const request = editingId
      ? this.stores.updateStoreCategory(storeId, editingId, body)
      : this.stores.createStoreCategory(storeId, body);

    request.subscribe({
      next: (category) => {
        this.categories.update((items) => editingId
          ? items.map((item) => item.id === category.id ? category : item)
          : [...items, category]);
        this.persistSavedCategoryOrder(category, this.newDisplayOrder(), editingId ? 'Categoria atualizada.' : 'Categoria criada.');
      },
      error: () => {
        this.saving.set(false);
        this.toast.showError(editingId ? 'Não foi possível atualizar a categoria.' : 'Não foi possível criar a categoria.');
      },
    });
  }

  startEdit(category: ProductCategory): void {
    this.editingCategoryId.set(category.id);
    this.newName.set(category.name);
    this.newDescription.set(category.description ?? '');
    this.newIsActive.set(category.isActive);
    this.newDisplayOrder.set(category.displayOrder);
  }

  cancelEdit(): void {
    this.resetForm();
  }

  toggleCategory(category: ProductCategory): void {
    const storeId = this.storeId();
    if (!storeId) return;

    this.stores.updateStoreCategory(storeId, category.id, {
      name: category.name,
      description: category.description,
      displayOrder: category.displayOrder,
      isActive: !category.isActive,
      isFeatured: category.isFeatured,
    }).subscribe({
      next: (updated) => this.categories.update((items) => items.map((item) => item.id === updated.id ? updated : item)),
       error: () => this.toast.showError('Não foi possível atualizar a categoria.'),
    });
  }

  deleteCategory(category: ProductCategory): void {
    const storeId = this.storeId();
    const associatedProducts = this.itemCount(category);
    if (associatedProducts > 0) {
      this.toast.showError(`Não é possível excluir "${category.name}" porque ela possui ${associatedProducts} produto(s) associado(s).`);
      return;
    }
    if (!storeId || !window.confirm(`Deseja realmente excluir a categoria "${category.name}"?`)) return;

    this.stores.deleteStoreCategory(storeId, category.id).subscribe({
      next: () => {
        this.categories.update((items) => items.filter((item) => item.id !== category.id));
        this.toast.showSuccess('Categoria excluída.');
      },
      error: (error: HttpErrorResponse) => {
        this.toast.showError(error.status === 409
          ? 'Não é possível excluir uma categoria que possui produtos associados.'
          : 'Não foi possível excluir a categoria.');
      },
    });
  }

  moveCategory(category: ProductCategory, direction: -1 | 1): void {
    const storeId = this.storeId();
    const current = this.sortedCategories();
    const index = current.findIndex((item) => item.id === category.id);
    const targetIndex = index + direction;
    if (!storeId || index < 0 || targetIndex < 0 || targetIndex >= current.length) return;

    const reordered = [...current];
    [reordered[index], reordered[targetIndex]] = [reordered[targetIndex], reordered[index]];
    const payload = reordered.map((item, itemIndex) => ({ id: item.id, displayOrder: itemIndex + 1 }));
    this.categories.set(reordered.map((item, itemIndex) => ({ ...item, displayOrder: itemIndex + 1 })));
    this.stores.reorderStoreCategories(storeId, payload).subscribe({
      error: () => {
        this.toast.showError('Não foi possível reordenar as categorias.');
        this.load();
      },
    });
  }

  onDragStart(category: ProductCategory): void {
    this.draggedCategoryId.set(category.id);
  }

  onDrop(target: ProductCategory): void {
    const draggedId = this.draggedCategoryId();
    const current = this.sortedCategories();
    const fromIndex = current.findIndex((item) => item.id === draggedId);
    const toIndex = current.findIndex((item) => item.id === target.id);
    this.draggedCategoryId.set(null);
    if (!draggedId || fromIndex < 0 || toIndex < 0 || fromIndex === toIndex) return;

    const reordered = [...current];
    const [moved] = reordered.splice(fromIndex, 1);
    reordered.splice(toIndex, 0, moved);
    const payload = reordered.map((item, index) => ({ id: item.id, displayOrder: index + 1 }));
    this.categories.set(reordered.map((item, index) => ({ ...item, displayOrder: index + 1 })));
    const storeId = this.storeId();
    if (!storeId) return;
    this.stores.reorderStoreCategories(storeId, payload).subscribe({
      error: () => {
        this.toast.showError('Não foi possível reordenar as categorias.');
        this.load();
      },
    });
  }

  itemCount(category: ProductCategory): number {
    return this.productCounts()[category.id] ?? 0;
  }

  categorySupportCopy(category: ProductCategory): string {
    return category.description?.trim() || (category.isActive ? 'Categoria visivel no cardapio.' : 'Categoria fora de exibicao no momento.');
  }

  private failLoad(): void {
    this.error.set(true);
    this.loading.set(false);
  }

  private resetForm(): void {
    this.editingCategoryId.set(null);
    this.newName.set('');
    this.newDescription.set('');
    this.newIsActive.set(true);
    this.newDisplayOrder.set(this.sortedCategories().length + 1);
  }

  private persistSavedCategoryOrder(category: ProductCategory, requestedPosition: number, successMessage: string): void {
    const storeId = this.storeId();
    if (!storeId) return;
    const withoutSaved = this.sortedCategories().filter(item => item.id !== category.id);
    const position = Math.max(1, Math.min(requestedPosition, withoutSaved.length + 1));
    const reordered = [...withoutSaved];
    reordered.splice(position - 1, 0, category);
    const payload = reordered.map((item, index) => ({ id: item.id, displayOrder: index + 1 }));
    this.categories.set(reordered.map((item, index) => ({ ...item, displayOrder: index + 1 })));
    this.stores.reorderStoreCategories(storeId, payload).subscribe({
      next: () => {
        this.resetForm();
        this.saving.set(false);
        this.toast.showSuccess(successMessage);
      },
      error: () => {
        this.saving.set(false);
        this.toast.showError('Categoria salva, mas não foi possível atualizar a ordem.');
        this.load();
      },
    });
  }
}
