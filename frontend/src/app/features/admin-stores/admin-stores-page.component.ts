import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { AdminStoresService } from '../../core/services/admin-stores.service';
import { AdminStoreSessionService } from '../../core/services/admin-store-session.service';
import { ToastService } from '../../core/services/toast.service';
import { StoreLogoAvatarComponent } from '../../shared/components/store-logo-avatar/store-logo-avatar.component';
import {
  AdminStoreSummary,
  AdminStoresOpenStateFilter,
  AdminStoresPublicationFilter,
  AdminStoresQuery,
  AdminStoresSort,
  AdminStoresStatusFilter,
  AdminStoresSubscriptionFilter,
} from '../../shared/models/admin-store.model';

type SortField = 'name' | 'created' | 'updated';

const STATUS_FILTERS: ReadonlyArray<{ value: AdminStoresStatusFilter; label: string }> = [
  { value: 'all', label: 'Todos os status' },
  { value: 'onboarding', label: 'Onboarding' },
  { value: 'active', label: 'Ativas' },
  { value: 'suspended', label: 'Suspensas' },
];

const PUBLICATION_FILTERS: ReadonlyArray<{ value: AdminStoresPublicationFilter; label: string }> = [
  { value: 'all', label: 'Publicação: todas' },
  { value: 'published', label: 'Publicadas' },
  { value: 'draft', label: 'Rascunho' },
];

const OPEN_STATE_FILTERS: ReadonlyArray<{ value: AdminStoresOpenStateFilter; label: string }> = [
  { value: 'all', label: 'Abertura: todas' },
  { value: 'open', label: 'Abertas' },
  { value: 'closed', label: 'Fechadas' },
];

const SUBSCRIPTION_FILTERS: ReadonlyArray<{ value: AdminStoresSubscriptionFilter; label: string }> = [
  { value: 'all', label: 'Assinatura: todas' },
  { value: 'active', label: 'Ativa' },
  { value: 'overdue', label: 'Vencida' },
  { value: 'blocked', label: 'Bloqueada' },
  { value: 'none', label: 'Sem assinatura' },
];

/**
 * Administrative store directory. Every action is backed by an AdminOnly server endpoint; opening a
 * store creates a short-lived server-side context and reuses the existing seller dashboard.
 */
@Component({
  selector: 'app-admin-stores-page',
  standalone: true,
  imports: [CommonModule, FormsModule, StoreLogoAvatarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-stores-page.component.html',
  styleUrl: './admin-stores-page.component.scss',
})
export class AdminStoresPageComponent implements OnInit {
  private readonly service = inject(AdminStoresService);
  private readonly session = inject(AdminStoreSessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly statusFilters = STATUS_FILTERS;
  readonly publicationFilters = PUBLICATION_FILTERS;
  readonly openStateFilters = OPEN_STATE_FILTERS;
  readonly subscriptionFilters = SUBSCRIPTION_FILTERS;

  readonly items = signal<AdminStoreSummary[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly totalItems = signal(0);
  readonly totalPages = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly permissionDenied = signal(false);
  readonly openingStoreId = signal<string | null>(null);

  readonly search = signal('');
  readonly status = signal<AdminStoresStatusFilter>('all');
  readonly publication = signal<AdminStoresPublicationFilter>('all');
  readonly openState = signal<AdminStoresOpenStateFilter>('all');
  readonly subscription = signal<AdminStoresSubscriptionFilter>('all');
  readonly sort = signal<AdminStoresSort>('name');

  readonly hasPrevious = computed(() => this.page() > 1);
  readonly hasNext = computed(() => this.page() < this.totalPages());
  readonly pages = computed(() => {
    const total = this.totalPages();
    if (total <= 0) return [];
    if (total <= 7) return Array.from({ length: total }, (_, index) => index + 1);

    const start = Math.max(1, Math.min(this.page() - 3, total - 6));
    return Array.from({ length: 7 }, (_, index) => start + index);
  });
  readonly announcement = computed(() => {
    if (this.loading()) return 'Carregando lojas';
    if (this.totalItems() === 0) return 'Nenhuma loja encontrada';
    return `${this.totalItems()} ${this.totalItems() === 1 ? 'loja encontrada' : 'lojas encontradas'}`;
  });

  private readonly searchInput$ = new Subject<string>();
  private readonly requests$ = new Subject<void>();

  constructor() {
    this.searchInput$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        this.search.set(value);
        this.page.set(1);
        this.syncUrl();
        this.load();
      });

    this.requests$
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
          this.permissionDenied.set(false);
        }),
        switchMap(() =>
          this.service.list(this.buildQuery()).pipe(
            map((response) => ({ response, failure: null as HttpErrorResponse | null })),
            catchError((failure: HttpErrorResponse) => of({ response: null, failure })),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ response, failure }) => {
        if (failure || !response) {
          this.loading.set(false);
          this.handleLoadError(failure);
          return;
        }

        this.items.set(response.items);
        this.totalItems.set(response.totalItems);
        this.totalPages.set(response.totalPages);
        this.loading.set(false);
      });
  }

  ngOnInit(): void {
    this.readUrlState();
    this.load();
  }

  retry(): void {
    this.load();
  }

  goToAdminHome(): void {
    void this.router.navigateByUrl('/admin');
  }

  onSearchInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.searchInput$.next(target?.value ?? '');
  }

  onStatusChange(event: Event): void {
    this.status.set(readFilter(event, STATUS_FILTERS.map((option) => option.value), 'all'));
    this.applyFilterChange();
  }

  onPublicationChange(event: Event): void {
    this.publication.set(
      readFilter(event, PUBLICATION_FILTERS.map((option) => option.value), 'all'),
    );
    this.applyFilterChange();
  }

  onOpenStateChange(event: Event): void {
    this.openState.set(readFilter(event, OPEN_STATE_FILTERS.map((option) => option.value), 'all'));
    this.applyFilterChange();
  }

  onSubscriptionChange(event: Event): void {
    this.subscription.set(
      readFilter(event, SUBSCRIPTION_FILTERS.map((option) => option.value), 'all'),
    );
    this.applyFilterChange();
  }

  clearFilters(): void {
    this.search.set('');
    this.status.set('all');
    this.publication.set('all');
    this.openState.set('all');
    this.subscription.set('all');
    this.page.set(1);
    this.syncUrl();
    this.load();
  }

  goToPage(page: number): void {
    if (page < 1 || page > Math.max(this.totalPages(), 1) || page === this.page()) return;

    this.page.set(page);
    this.syncUrl();
    this.load();
  }

  previousPage(): void {
    if (this.hasPrevious()) this.goToPage(this.page() - 1);
  }

  nextPage(): void {
    if (this.hasNext()) this.goToPage(this.page() + 1);
  }

  toggleSort(field: SortField): void {
    const current = this.sort();
    const isSameField = current === field || current === `${field}_desc`;
    const descending = isSameField && current === field;

    this.sort.set(`${field}${descending ? '_desc' : ''}` as AdminStoresSort);
    this.page.set(1);
    this.syncUrl();
    this.load();
  }

  sortAria(field: SortField): 'ascending' | 'descending' | 'none' {
    const current = this.sort();
    if (current === field) return 'ascending';
    if (current === `${field}_desc`) return 'descending';
    return 'none';
  }

  openStore(item: AdminStoreSummary): void {
    if (this.openingStoreId()) return;

    this.openingStoreId.set(item.id);
    const returnUrl = this.router.url;

    this.service
      .openContext(item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (context) => {
          this.session.start(context, returnUrl);
          this.openingStoreId.set(null);
          void this.router.navigateByUrl('/app/dashboard');
        },
        error: (failure: HttpErrorResponse) => {
          this.openingStoreId.set(null);
          this.handleOpenError(item, failure);
        },
      });
  }

  viewPublicStore(item: AdminStoreSummary, event: Event): void {
    event.stopPropagation();
    if (!item.isPublished) return;

    window.open(`/${item.slug}`, '_blank', 'noopener,noreferrer');
  }

  statusLabel(status: AdminStoreSummary['status']): string {
    switch (status) {
      case 'Active':
        return 'Ativa';
      case 'Suspended':
        return 'Suspensa';
      default:
        return 'Onboarding';
    }
  }

  statusClass(status: AdminStoreSummary['status']): string {
    switch (status) {
      case 'Active':
        return 'badge is-success';
      case 'Suspended':
        return 'badge is-danger';
      default:
        return 'badge is-neutral';
    }
  }

  publicationLabel(item: AdminStoreSummary): string {
    return item.isPublished ? 'Publicada' : 'Rascunho';
  }

  openLabel(item: AdminStoreSummary): string {
    return item.isOpen ? 'Aberta' : 'Fechada';
  }

  subscriptionLabel(item: AdminStoreSummary): string {
    switch (item.subscription.status) {
      case 'Active':
        return 'Ativa';
      case 'Overdue':
        return 'Vencida';
      case 'Blocked':
        return 'Bloqueada';
      default:
        return 'Sem assinatura';
    }
  }

  subscriptionClass(item: AdminStoreSummary): string {
    switch (item.subscription.status) {
      case 'Active':
        return 'badge is-success';
      case 'Overdue':
        return 'badge is-warning';
      case 'Blocked':
        return 'badge is-danger';
      default:
        return 'badge is-neutral';
    }
  }

  formatDate(value: string | null): string {
    if (!value) return '—';

    return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short' }).format(new Date(value));
  }

  private applyFilterChange(): void {
    this.page.set(1);
    this.syncUrl();
    this.load();
  }

  private load(): void {
    this.requests$.next();
  }

  private buildQuery(): AdminStoresQuery {
    return {
      search: this.search(),
      status: this.status(),
      publication: this.publication(),
      openState: this.openState(),
      subscription: this.subscription(),
      sort: this.sort(),
      page: this.page(),
      pageSize: this.pageSize(),
    };
  }

  private syncUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        page: this.page() > 1 ? this.page() : null,
        search: this.search().trim() || null,
        status: this.status() !== 'all' ? this.status() : null,
        publication: this.publication() !== 'all' ? this.publication() : null,
        openState: this.openState() !== 'all' ? this.openState() : null,
        subscription: this.subscription() !== 'all' ? this.subscription() : null,
        sort: this.sort() !== 'name' ? this.sort() : null,
      },
    });
  }

  private readUrlState(): void {
    const params = this.route.snapshot.queryParamMap;

    const page = Number(params.get('page'));
    this.page.set(Number.isFinite(page) && page >= 1 ? Math.floor(page) : 1);
    this.search.set((params.get('search') ?? '').slice(0, 120));
    this.status.set(
      readQuery(params.get('status'), STATUS_FILTERS.map((option) => option.value), 'all'),
    );
    this.publication.set(
      readQuery(params.get('publication'), PUBLICATION_FILTERS.map((option) => option.value), 'all'),
    );
    this.openState.set(
      readQuery(params.get('openState'), OPEN_STATE_FILTERS.map((option) => option.value), 'all'),
    );
    this.subscription.set(
      readQuery(params.get('subscription'), SUBSCRIPTION_FILTERS.map((option) => option.value), 'all'),
    );
    this.sort.set(readQuery(params.get('sort'), ['name', 'name_desc', 'created', 'created_desc', 'updated', 'updated_desc'] as const, 'name'));
  }

  private handleLoadError(failure: HttpErrorResponse | null): void {
    if (failure && (failure.status === 401 || failure.status === 403)) {
      this.permissionDenied.set(true);
      this.error.set(null);
      return;
    }

    this.error.set('Não foi possível carregar as lojas.');
  }

  private handleOpenError(item: AdminStoreSummary, failure: HttpErrorResponse): void {
    switch (failure.status) {
      case 404:
        void this.toast.showError('Loja não encontrada. Atualize a lista.');
        this.load();
        return;
      case 409:
        void this.toast.showError('Loja sem proprietário resolvível.');
        return;
      case 403:
        void this.toast.showError('Seu acesso administrativo expirou.');
        return;
      case 429:
        void this.toast.showWarning('Muitas tentativas. Aguarde alguns segundos e tente novamente.');
        return;
      default:
        void this.toast.showError(`Não foi possível abrir a loja ${item.name}. Tente novamente.`);
    }
  }
}

function readFilter<T extends string>(event: Event, allowed: readonly T[], fallback: T): T {
  const target = event.target as HTMLSelectElement | null;
  return readQuery(target?.value, allowed, fallback);
}

function readQuery<T extends string>(value: string | null | undefined, allowed: readonly T[], fallback: T): T {
  return value && (allowed as readonly string[]).includes(value) ? (value as T) : fallback;
}
