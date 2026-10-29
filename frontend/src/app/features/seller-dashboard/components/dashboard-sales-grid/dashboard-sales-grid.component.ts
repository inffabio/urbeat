import { Component, computed, input, output } from '@angular/core';
import { PagedStoreSales, StoreSaleItem, StoreSaleRow } from '../../../../shared/models/order.model';

@Component({
  selector: 'app-dashboard-sales-grid',
  standalone: true,
  templateUrl: './dashboard-sales-grid.component.html',
  styleUrl: './dashboard-sales-grid.component.scss',
})
export class DashboardSalesGridComponent {
  readonly result = input<PagedStoreSales | null>(null);
  readonly loading = input<boolean>(false);
  readonly error = input<boolean>(false);
  readonly expandedOrders = input<ReadonlySet<string>>(new Set<string>());
  readonly rangeLabel = input<string>('');

  readonly pageChange = output<number>();
  readonly retry = output<void>();
  readonly toggleItems = output<string>();

  readonly skeletonRows = [1, 2, 3, 4, 5];

  readonly sales = computed(() => this.result()?.sales ?? []);

  readonly totals = computed(() => this.result()?.totals ?? { salesTotal: 0, deliveryFees: 0 });

  readonly page = computed(() => this.result()?.page ?? 1);

  readonly totalPages = computed(() => this.result()?.totalPages ?? 0);

  readonly totalItems = computed(() => this.result()?.totalItems ?? 0);

  readonly isEmpty = computed(() => !!this.result() && this.totalItems() === 0);

  readonly isOutOfRange = computed(() => {
    const result = this.result();
    return !!result && result.totalPages > 0 && result.page > result.totalPages;
  });

  readonly isFirstLoad = computed(() => this.loading() && !this.result());

  readonly isPageLoading = computed(() => this.loading() && !!this.result());

  readonly hasPrevious = computed(() => !!this.result() && this.page() > 1);

  readonly hasNext = computed(() => !!this.result() && this.page() < this.totalPages());

  isExpanded(orderId: string): boolean {
    return this.expandedOrders().has(orderId);
  }

  toggle(row: StoreSaleRow): void {
    this.toggleItems.emit(row.orderId);
  }

  itemLine(item: StoreSaleItem): string {
    return `${item.quantity} × ${this.formatCurrency(item.unitPrice)} = ${this.formatCurrency(item.totalPrice)}`;
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
      .format(value)
      .replace(/\u00a0/g, ' ');
  }

  previousPage(): void {
    if (!this.hasPrevious() || this.loading()) return;
    this.pageChange.emit(this.page() - 1);
  }

  nextPage(): void {
    if (!this.hasNext() || this.loading()) return;
    this.pageChange.emit(this.page() + 1);
  }

  goToLastPage(): void {
    if (this.totalPages() < 1) return;
    this.pageChange.emit(this.totalPages());
  }
}
