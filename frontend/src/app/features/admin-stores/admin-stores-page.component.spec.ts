import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AdminStoresPageComponent } from './admin-stores-page.component';
import { AdminStoresService } from '../../core/services/admin-stores.service';
import { AdminStoreSessionService } from '../../core/services/admin-store-session.service';
import { ToastService } from '../../core/services/toast.service';
import {
  AdminStoreContext,
  AdminStoreSummary,
  PagedAdminStoreSummary,
} from '../../shared/models/admin-store.model';

function store(overrides: Partial<AdminStoreSummary> = {}): AdminStoreSummary {
  return {
    id: 'store-1',
    name: 'Pizza Hunter',
    slug: 'pizza-hunter',
    logoUrl: null,
    owner: { userId: 'owner-1', email: 'maria@exemplo.com', name: 'Maria Souza' },
    status: 'Active',
    isPublished: true,
    isOpen: true,
    isSubscriptionBlocked: false,
    subscription: { status: 'Active', planName: 'Pro', nextDueDateUtc: '2026-12-01T00:00:00Z' },
    createdAtUtc: '2026-09-01T11:00:00Z',
    updatedAtUtc: '2026-11-13T09:15:00Z',
    ...overrides,
  };
}

function page(items: AdminStoreSummary[], overrides: Partial<PagedAdminStoreSummary> = {}): PagedAdminStoreSummary {
  return { page: 1, pageSize: 20, totalItems: items.length, totalPages: 1, items, ...overrides };
}

const context: AdminStoreContext = {
  contextId: 'context-1',
  adminUserId: 'admin-1',
  storeId: 'store-1',
  storeName: 'Pizza Hunter',
  storeSlug: 'pizza-hunter',
  expiresAtUtc: '2026-11-16T12:30:00Z',
  token: 'opaque-token',
};

describe('AdminStoresPageComponent', () => {
  let fixture: ComponentFixture<AdminStoresPageComponent>;
  let component: AdminStoresPageComponent;
  let stores: { list: jest.Mock; openContext: jest.Mock };
  let session: { start: jest.Mock };
  let router: { url: string; navigate: jest.Mock; navigateByUrl: jest.Mock };
  let toast: { showError: jest.Mock; showWarning: jest.Mock };
  let queryParams: Record<string, string>;

  beforeEach(async () => {
    queryParams = {};
    stores = {
      list: jest.fn(() => of(page([store()]))),
      openContext: jest.fn(() => of(context)),
    };
    session = { start: jest.fn() };
    router = {
      url: '/admin/lojas?page=1',
      navigate: jest.fn(() => Promise.resolve(true)),
      navigateByUrl: jest.fn(() => Promise.resolve(true)),
    };
    toast = {
      showError: jest.fn(() => Promise.resolve()),
      showWarning: jest.fn(() => Promise.resolve()),
    };

    await TestBed.configureTestingModule({
      imports: [AdminStoresPageComponent],
      providers: [
        { provide: AdminStoresService, useValue: stores },
        { provide: AdminStoreSessionService, useValue: session },
        { provide: ToastService, useValue: toast },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              get queryParamMap() {
                return convertToParamMap(queryParams);
              },
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminStoresPageComponent);
    component = fixture.componentInstance;
  });

  function render(): void {
    fixture.detectChanges();
  }

  it('shows the loading skeleton before the first response', () => {
    const pending = new Subject<PagedAdminStoreSummary>();
    stores.list.mockReturnValue(pending.asObservable());

    render();

    expect(fixture.nativeElement.querySelector('.stores-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.stores-table')).toBeNull();
  });

  it('renders the listing with store name, owner, badges and initial placeholder', () => {
    render();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Pizza Hunter');
    expect(text).toContain('Maria Souza');
    expect(text).toContain('Ativa');
    expect(text).toContain('Publicada');
    expect(text).toContain('Aberta');
    expect(fixture.nativeElement.querySelector('.store-logo-initial')?.textContent?.trim()).toBe('P');
    expect(fixture.nativeElement.querySelectorAll('.store-row')).toHaveLength(1);
  });

  it('renders the empty state with a filter reset', () => {
    stores.list.mockReturnValue(of(page([])));

    render();

    expect(fixture.nativeElement.textContent).toContain(
      'Nenhuma loja encontrada com esses filtros',
    );
    expect(fixture.nativeElement.querySelector('.stores-table')).toBeNull();
  });

  it('renders the error state and retries the request', () => {
    stores.list.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));

    render();
    expect(fixture.nativeElement.querySelector('.stores-state.is-error')).not.toBeNull();

    stores.list.mockReturnValue(of(page([store()])));
    const retry = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((button) => (button as HTMLButtonElement).textContent?.includes('Tentar novamente')) as
      | HTMLButtonElement
      | undefined;
    retry?.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.store-row')).toHaveLength(1);
  });

  it('denies the listing on 403 and offers the administrative return', () => {
    stores.list.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));

    render();

    expect(fixture.nativeElement.textContent).toContain('Acesso administrativo necessário');
    expect(component.permissionDenied()).toBe(true);
  });

  it('debounces the search for 300 ms and resets the page', () => {
    jest.useFakeTimers();
    try {
      render();
      stores.list.mockClear();

      const input = fixture.nativeElement.querySelector('#admin-stores-search') as HTMLInputElement;
      input.value = 'pizza';
      input.dispatchEvent(new Event('input'));

      jest.advanceTimersByTime(299);
      expect(stores.list).not.toHaveBeenCalled();

      jest.advanceTimersByTime(1);
      expect(stores.list).toHaveBeenCalledTimes(1);
      expect(stores.list.mock.calls[0][0].search).toBe('pizza');
      expect(stores.list.mock.calls[0][0].page).toBe(1);
    } finally {
      jest.useRealTimers();
    }
  });

  it('resets the page when a filter changes', () => {
    queryParams = { page: '3' };
    render();
    stores.list.mockClear();

    const select = fixture.nativeElement.querySelector('#admin-stores-status') as HTMLSelectElement;
    select.value = 'onboarding';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(stores.list).toHaveBeenCalledTimes(1);
    expect(stores.list.mock.calls[0][0]).toMatchObject({ status: 'onboarding', page: 1 });
    expect(router.navigate).toHaveBeenCalled();
  });

  it('opens the store, starts the session with the current url and navigates to the dashboard', () => {
    router.url = '/admin/lojas?page=2&search=pizza';
    render();

    (fixture.nativeElement.querySelector('.store-row') as HTMLElement).click();
    fixture.detectChanges();

    expect(stores.openContext).toHaveBeenCalledWith('store-1');
    expect(session.start).toHaveBeenCalledWith(context, '/admin/lojas?page=2&search=pizza');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/app/dashboard');
  });

  it('opens the store with the keyboard (Enter)', () => {
    render();

    const row = fixture.nativeElement.querySelector('.store-row') as HTMLElement;
    row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    expect(stores.openContext).toHaveBeenCalledTimes(1);
  });

  it('maps open errors to actionable messages', () => {
    render();

    stores.openContext.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 409 })),
    );
    component.openStore(component.items()[0]);
    expect(toast.showError).toHaveBeenCalledWith('Loja sem proprietário resolvível.');

    stores.openContext.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 429 })),
    );
    component.openStore(component.items()[0]);
    expect(toast.showWarning).toHaveBeenCalled();
    expect(session.start).not.toHaveBeenCalled();
  });

  it('disables the public store action for draft stores', () => {
    stores.list.mockReturnValue(of(page([store({ isPublished: false })])));
    render();

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('.btn-ghost')) as HTMLButtonElement[];
    expect(buttons[0].disabled).toBe(true);
  });

  it('restores filters and page from the url', () => {
    queryParams = {
      page: '3',
      search: 'pizza',
      status: 'onboarding',
      publication: 'draft',
      openState: 'closed',
      subscription: 'none',
      sort: 'name_desc',
    };
    stores.list.mockClear();

    const restoredFixture = TestBed.createComponent(AdminStoresPageComponent);
    restoredFixture.detectChanges();

    expect(stores.list).toHaveBeenCalledWith({
      search: 'pizza',
      status: 'onboarding',
      publication: 'draft',
      openState: 'closed',
      subscription: 'none',
      sort: 'name_desc',
      page: 3,
      pageSize: 20,
    });

    const select = restoredFixture.nativeElement.querySelector(
      '#admin-stores-status',
    ) as HTMLSelectElement;
    expect(select.value).toBe('onboarding');
  });

  it('paginates with the expected query parameters', () => {
    stores.list.mockReturnValue(of(page([store()], { totalPages: 3, totalItems: 60 })));
    render();
    stores.list.mockClear();

    component.goToPage(2);

    expect(stores.list).toHaveBeenCalledTimes(1);
    expect(stores.list.mock.calls[0][0].page).toBe(2);
  });
});
