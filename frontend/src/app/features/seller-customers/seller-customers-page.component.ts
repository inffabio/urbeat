import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonIcon, IonModal } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  bagHandleOutline,
  cashOutline,
  chevronBackOutline,
  chevronForwardOutline,
  closeOutline,
  createOutline,
  eyeOutline,
  eyeOffOutline,
  peopleOutline,
  refreshOutline,
  searchOutline,
  starOutline,
} from 'ionicons/icons';
import { OrderService } from '../../core/services/order.service';
import { AddressService } from '../../core/services/address.service';
import { ToastService } from '../../core/services/toast.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import {
  PagedSellerCustomerSummary,
  SellerCustomerMetrics,
  SellerCustomerSummary,
  StoreCustomersQuery,
} from '../../shared/models/order.model';

addIcons({
  'bag-handle-outline': bagHandleOutline,
  'cash-outline': cashOutline,
  'chevron-back-outline': chevronBackOutline,
  'chevron-forward-outline': chevronForwardOutline,
  'close-outline': closeOutline,
  'create-outline': createOutline,
  'eye-outline': eyeOutline,
  'eye-off-outline': eyeOffOutline,
  'people-outline': peopleOutline,
  'refresh-outline': refreshOutline,
  'search-outline': searchOutline,
  'star-outline': starOutline,
});

@Component({
  selector: 'app-seller-customers-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonIcon, IonModal],
  templateUrl: './seller-customers-page.component.html',
  styleUrl: './seller-customers-page.component.scss',
})
export class SellerCustomersPageComponent implements OnInit, OnDestroy {
  private readonly orders = inject(OrderService);
  private readonly address = inject(AddressService);
  private readonly toast = inject(ToastService);
  private searchDebounce: ReturnType<typeof setTimeout> | undefined;

  private readonly emptyMetrics: SellerCustomerMetrics = {
    totalCustomers: 0,
    activeCustomers: 0,
    recurringCustomers: 0,
    newCustomersThisMonth: 0,
    averageTicket: 0,
  };

  private readonly emptyResponse: PagedSellerCustomerSummary = {
    page: 1,
    pageSize: 7,
    totalItems: 0,
    totalPages: 0,
    metrics: this.emptyMetrics,
    items: [],
  };

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly response = signal<PagedSellerCustomerSummary>(this.emptyResponse);
  readonly query = signal<Required<StoreCustomersQuery>>({
    page: 1,
    pageSize: 7,
    search: '',
    status: 'all',
    sort: 'lastOrderDesc',
  });
  readonly customers = computed(() => this.response().items);
  readonly metrics = computed(() => this.response().metrics);
  readonly selectedCustomer = signal<SellerCustomerSummary | null>(null);
  readonly editingCustomer = signal<SellerCustomerSummary | null>(null);
  readonly editName = signal('');
  readonly editEmail = signal('');
  readonly editPhone = signal('');
  readonly editCep = signal('');
  readonly editStreet = signal('');
  readonly editNumber = signal('');
  readonly editComplement = signal('');
  readonly editNeighborhood = signal('');
  readonly editCity = signal('');
  readonly editState = signal('');
  readonly editCepLoading = signal(false);

  readonly totalCustomers = computed(() => this.metrics().totalCustomers);
  readonly activeCustomers = computed(() => this.metrics().activeCustomers);
  readonly recurringCustomers = computed(() => this.metrics().recurringCustomers);
  readonly newCustomersThisMonth = computed(() => this.metrics().newCustomersThisMonth);
  readonly averageTicket = computed(() => this.metrics().averageTicket);
  readonly totalPages = computed(() => this.response().totalPages);
  readonly currentPage = computed(() => this.response().page);
  readonly pagination = computed(() => {
    const totalPages = this.totalPages();
    const page = this.currentPage();
    if (totalPages <= 1) {
      return totalPages === 1 ? [1] : [];
    }

    const start = Math.max(1, page - 1);
    const end = Math.min(totalPages, start + 2);
    const adjustedStart = Math.max(1, end - 2);
    return Array.from({ length: end - adjustedStart + 1 }, (_, index) => adjustedStart + index);
  });
  readonly showingFrom = computed(() => {
    if (this.response().totalItems === 0) return 0;
    return (this.currentPage() - 1) * this.response().pageSize + 1;
  });
  readonly showingTo = computed(() => {
    if (this.response().totalItems === 0) return 0;
    return Math.min(this.currentPage() * this.response().pageSize, this.response().totalItems);
  });

  getInitials(name: string): string {
    if (!name) return '??';
    const parts = name.trim().split(/\s+/);
    return parts.length >= 2
      ? (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
      : name.substring(0, 2).toUpperCase();
  }

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    if (this.searchDebounce) {
      clearTimeout(this.searchDebounce);
    }
  }

  load(page = this.query().page): void {
    const nextQuery = { ...this.query(), page };
    this.query.set(nextQuery);
    this.loading.set(true);
    this.error.set(false);
    this.orders.getStoreCustomers(nextQuery).subscribe({
      next: (response) => {
        this.response.set(response);
        this.loading.set(false);
      },
      error: () => {
        this.response.set(this.emptyResponse);
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  refresh(): void {
    this.load(1);
  }

  onSearchChange(search: string): void {
    if (this.searchDebounce) {
      clearTimeout(this.searchDebounce);
    }

    this.searchDebounce = setTimeout(() => {
      this.query.update((current) => ({ ...current, search }));
      this.load(1);
    }, 250);
  }

  updateStatus(status: Required<StoreCustomersQuery>['status']): void {
    this.query.update((current) => ({ ...current, status }));
    this.load(1);
  }

  updateSort(sort: Required<StoreCustomersQuery>['sort']): void {
    this.query.update((current) => ({ ...current, sort }));
    this.load(1);
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) {
      return;
    }

    this.load(page);
  }

  canGoToPreviousPage(): boolean {
    return this.currentPage() > 1;
  }

  canGoToNextPage(): boolean {
    return this.currentPage() < this.totalPages();
  }

  viewCustomer(customer: SellerCustomerSummary): void {
    this.selectedCustomer.set(customer);
  }

  editCustomer(customer: SellerCustomerSummary): void {
    this.editingCustomer.set(customer);
    this.editName.set(customer.name);
    this.editEmail.set(customer.email);
    this.editPhone.set(customer.phone);
    this.editCep.set(customer.cep ?? '');
    this.editStreet.set(customer.street ?? '');
    this.editNumber.set(customer.number ?? '');
    this.editComplement.set(customer.complement ?? '');
    this.editNeighborhood.set(customer.neighborhood ?? '');
    this.editCity.set(customer.city ?? '');
    this.editState.set(customer.state ?? '');
  }

  onEditCepChange(value: string): void {
    const digits = value.replace(/\D/g, '').slice(0, 8);
    this.editCep.set(digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits);
    if (digits.length !== 8) return;

    this.editCepLoading.set(true);
    this.address.lookupCep(digits).subscribe({
      next: (result) => {
        this.editStreet.set(result.street);
        this.editNeighborhood.set(result.neighborhood);
        this.editCity.set(result.city);
        this.editState.set(result.state);
        this.editCepLoading.set(false);
      },
      error: () => {
        this.editCepLoading.set(false);
        this.toast.showError('Não foi possível localizar este CEP.');
      },
    });
  }

  saveCustomer(): void {
    const customer = this.editingCustomer();
    if (!customer || !this.editName().trim() || !this.editEmail().trim() || !this.editPhone().trim()) return;
    this.orders.updateStoreCustomer(customer.id, {
      name: this.editName().trim(),
      email: this.editEmail().trim(),
      phone: this.editPhone().replace(/\D/g, ''),
      cep: this.editCep().replace(/\D/g, ''),
      street: this.editStreet().trim(),
      number: this.editNumber().trim(),
      complement: this.editComplement().trim(),
      neighborhood: this.editNeighborhood().trim(),
      city: this.editCity().trim(),
      state: this.editState().trim().toUpperCase(),
    }).subscribe({
      next: updated => { this.replaceCustomer(updated); this.editingCustomer.set(null); this.toast.showSuccess('Cliente atualizado.'); },
      error: (error: { status?: number }) => this.toast.showError(error.status === 409 ? 'Este e-mail já está sendo usado por outro cliente.' : 'Não foi possível atualizar o cliente.'),
    });
  }

  toggleCustomer(customer: SellerCustomerSummary): void {
    this.orders.toggleStoreCustomer(customer.id, !customer.isActive).subscribe({
      next: updated => { this.replaceCustomer(updated); this.toast.showSuccess(updated.isActive ? 'Cliente ativado.' : 'Cliente inativado.'); },
      error: () => this.toast.showError('Não foi possível alterar o status do cliente.'),
    });
  }

  private replaceCustomer(updated: SellerCustomerSummary): void {
    this.response.update(current => ({ ...current, items: current.items.map(item => item.id === updated.id ? updated : item) }));
  }

  dismissView(): void {
    this.selectedCustomer.set(null);
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  formatPhone(value: string | null | undefined): string {
    const digits = (value ?? '').replace(/\D/g, '');
    if (digits.length === 11) return `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
    if (digits.length === 10) return `(${digits.slice(0, 2)}) ${digits.slice(2, 6)}-${digits.slice(6)}`;
    return value?.trim() || '-';
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '-';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '-' : formatSaoPauloDate(value);
  }
}
