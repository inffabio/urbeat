import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { StoreShellComponent } from './store-shell.component';
import { FooterNavComponent } from '../../shared/components/footer-nav/footer-nav.component';
import { CartSheetComponent } from '../../shared/components/cart-sheet/cart-sheet.component';
import { StickyActionBarComponent } from '../../shared/components/sticky-action-bar/sticky-action-bar.component';
import { CartCheckoutActionService } from '../../core/services/cart-checkout-action.service';
import { StoreService } from '../../core/services/store.service';
import { AuthService } from '../../core/services/auth.service';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { StoreContextService } from '../../core/services/store-context.service';

describe('StoreShellComponent', () => {
  let fixture: ComponentFixture<StoreShellComponent>;
  let routerMock: { url: string; navigate: jest.Mock };

  beforeEach(async () => {
    routerMock = { url: '/loja/carrinho', navigate: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: { paramMap: of({ get: () => 'loja' }), snapshot: { paramMap: { get: () => 'loja' } } } },
        { provide: StoreService, useValue: { getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', slug: 'loja', name: 'Loja' })) } },
        { provide: AuthService, useValue: { customerProfile: signal(null), logout: jest.fn() } },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        { provide: CustomerOrderTrackingService, useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() } },
      ],
    })
      .overrideComponent(StoreShellComponent, {
        set: {
          template: `
            @if (storeResolved() && showFooterNav()) {
              <footer class="footer-nav" aria-label="Navegação principal"></footer>
            }
          `,
        },
      })
      .compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug.set('loja');
  });

  it('should show the footer navigation on cart routes', () => {
    routerMock.url = '/loja/carrinho';

    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.footer-nav'))).not.toBeNull();
  });

  it('should hide the footer navigation on checkout routes', () => {
    routerMock.url = '/loja/checkout/cadastro';

    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.footer-nav'))).toBeNull();
  });

  it('should keep the footer navigation on the payment screen', () => {
    routerMock.url = '/loja/checkout/pagamento';

    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.footer-nav'))).not.toBeNull();
  });

  it('should keep the footer navigation on the store home', () => {
    routerMock.url = '/loja';

    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.footer-nav'))).not.toBeNull();
  });

  it('enables Conta when a customer profile is loaded', () => {
    const auth = TestBed.inject(AuthService) as unknown as { customerProfile: ReturnType<typeof signal<any>> };
    auth.customerProfile.set({ fullName: 'Maria Oliveira' });

    expect(fixture.componentInstance.footerItems().find((item) => item.id === 'conta')?.disabled).toBe(false);
  });

  it('reflects the account menu open state and control id on the Conta item', () => {
    const conta = () => fixture.componentInstance.footerItems().find((item) => item.id === 'conta');

    expect(conta()?.ariaControls).toBe('account-menu');
    expect(conta()?.ariaExpanded).toBe(false);

    fixture.componentInstance.isAccountMenuOpen.set(true);

    expect(conta()?.ariaExpanded).toBe(true);
  });

  it('navigates to the account edit route using separate path segments', () => {
    fixture.componentInstance.openAccountProfile();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'conta', 'cadastro']);
    expect(routerMock.navigate).not.toHaveBeenCalledWith(['/', 'loja', 'conta/cadastro']);
  });

  it('falls back to the route storePath when the slug has not resolved yet', () => {
    (fixture.componentInstance as any).storeSlug.set('');

    fixture.componentInstance.openAccountProfile();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'conta', 'cadastro']);
  });

  it('does not navigate to the cart and keeps the sheet open when the bag is empty', () => {
    const cart = TestBed.inject(CartService);
    cart.items.set([]);
    fixture.componentInstance.isCartSheetOpen.set(true);

    fixture.componentInstance.goToCart();

    expect(routerMock.navigate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.isCartSheetOpen()).toBe(true);
  });

  it('navigates to the cart and closes the sheet when the bag has items', () => {
    const cart = TestBed.inject(CartService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    fixture.componentInstance.isCartSheetOpen.set(true);

    fixture.componentInstance.goToCart();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'carrinho']);
    expect(fixture.componentInstance.isCartSheetOpen()).toBe(false);
  });

  it('enables and marks Pedidos red when there is an active order', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
      trackedOrders: ReturnType<typeof signal<any[]>>;
    };
    TestBed.inject(StoreContextService).storeId.set('s1');
    tracking.activeOrders.set([{ id: 'o1', storeId: 's1' }]);
    tracking.trackedOrders.set([{ id: 'o1', storeId: 's1' }]);

    const pedidos = fixture.componentInstance.footerItems().find((item) => item.id === 'pedidos');

    expect(pedidos?.disabled).toBe(false);
    expect(pedidos?.badge).toBe(1);
  });

  it('shows the number of active orders in the badge', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
      trackedOrders: ReturnType<typeof signal<any[]>>;
    };
    TestBed.inject(StoreContextService).storeId.set('s1');
    tracking.activeOrders.set([{ id: 'o1', storeId: 's1' }, { id: 'o2', storeId: 's1' }]);
    tracking.trackedOrders.set([{ id: 'o1', storeId: 's1' }, { id: 'o2', storeId: 's1' }]);

    const pedidos = fixture.componentInstance.footerItems().find((item) => item.id === 'pedidos');

    expect(pedidos?.badge).toBe(2);
  });

  it('keeps Pedidos disabled when there are no active orders', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
      trackedOrders: ReturnType<typeof signal<any[]>>;
    };
    TestBed.inject(StoreContextService).storeId.set('s1');
    tracking.activeOrders.set([]);
    tracking.trackedOrders.set([]);

    const pedidos = fixture.componentInstance.footerItems().find((item) => item.id === 'pedidos');

    expect(pedidos?.disabled).toBe(true);
    expect(pedidos?.badge).toBe(0);
  });

  it('does not count active orders from another store', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
      trackedOrders: ReturnType<typeof signal<any[]>>;
    };
    TestBed.inject(StoreContextService).storeId.set('s1');
    tracking.activeOrders.set([{ id: 'o1', storeId: 's2' }]);
    tracking.trackedOrders.set([{ id: 'o1', storeId: 's2' }]);

    const pedidos = fixture.componentInstance.footerItems().find((item) => item.id === 'pedidos');

    expect(pedidos?.disabled).toBe(true);
  });

  it('enables Pedidos when only history orders exist but shows no active badge', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
      trackedOrders: ReturnType<typeof signal<any[]>>;
    };
    TestBed.inject(StoreContextService).storeId.set('s1');
    tracking.activeOrders.set([]);
    tracking.trackedOrders.set([{ id: 'o1', storeId: 's1' }]);

    const pedidos = fixture.componentInstance.footerItems().find((item) => item.id === 'pedidos');

    expect(pedidos?.disabled).toBe(false);
    expect(pedidos?.badge).toBe(0);
  });

  it('navigates to the Pedidos list when the Pedidos footer item is selected', () => {
    fixture.componentInstance.onFooterSelect('pedidos');

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedidos']);
  });

  it('does not reset customer tracking on logout (the tracking service self-resets via token)', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as { reset: jest.Mock };
    const auth = TestBed.inject(AuthService) as unknown as { logout: jest.Mock };

    fixture.componentInstance.logoutCustomer();

    expect(auth.logout).toHaveBeenCalled();
    expect(tracking.reset).not.toHaveBeenCalled();
  });

  it('clears checkout personal data and redirects to the public menu on customer logout', () => {
    const checkout = TestBed.inject(CheckoutService) as unknown as { resetCheckout: jest.Mock };

    fixture.componentInstance.logoutCustomer();

    expect(checkout.resetCheckout).toHaveBeenCalled();
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
    expect(fixture.componentInstance.isAccountMenuOpen()).toBe(false);
  });

  it('keeps Pedidos disabled during reload when only unloaded persisted ids exist for another store', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
      trackedOrders: ReturnType<typeof signal<any[]>>;
      hasTrackedOrders: ReturnType<typeof signal<boolean>>;
    };
    TestBed.inject(StoreContextService).storeId.set('s1');
    tracking.activeOrders.set([]);
    tracking.trackedOrders.set([]);
    tracking.hasTrackedOrders.set(true);

    const pedidos = fixture.componentInstance.footerItems().find((item) => item.id === 'pedidos');

    expect(pedidos?.disabled).toBe(true);
    expect(pedidos?.badge).toBe(0);
  });
});

describe('StoreShellComponent store resolution lifecycle', () => {
  let fixture: ComponentFixture<StoreShellComponent>;
  let getStoreByPath: jest.Mock;
  let paramMap$: Subject<{ get: (key: string) => string | null }>;
  let routerMock: { url: string; navigate: jest.Mock };

  beforeEach(async () => {
    getStoreByPath = jest.fn();
    paramMap$ = new Subject();
    routerMock = { url: '/loja', navigate: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        { provide: Router, useValue: routerMock },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: paramMap$.asObservable(), snapshot: { paramMap: { get: () => 'loja' } } },
        },
        { provide: StoreService, useValue: { getStoreByPath } },
        { provide: AuthService, useValue: { customerProfile: signal(null), logout: jest.fn() } },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        { provide: CustomerOrderTrackingService, useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.detectChanges();
  });

  function resolveStore(): Subject<any> {
    const subject = new Subject<any>();
    getStoreByPath.mockReturnValueOnce(subject);
    return subject;
  }

  it('clears the previous store context before resolving a new store path', () => {
    const storeContext = TestBed.inject(StoreContextService);

    const first = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    first.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });

    expect(storeContext.storeId()).toBe('s1');
    expect(storeContext.storeName()).toBe('Loja');
    expect(storeContext.phoneNumber()).toBe('119999');
    expect(storeContext.isOpen()).toBe(true);
    expect(fixture.componentInstance.storeResolved()).toBe(true);

    const second = resolveStore();
    paramMap$.next({ get: () => 'outra-loja' });

    expect(storeContext.storeId()).toBeNull();
    expect(storeContext.storeName()).toBeNull();
    expect(storeContext.phoneNumber()).toBeNull();
    expect(fixture.componentInstance.storeResolved()).toBe(false);

    second.next({ id: 's2', slug: 'outra-loja', name: 'Outra Loja', phoneNumber: '118888', isOpenNow: false });

    expect(storeContext.storeId()).toBe('s2');
    expect(storeContext.storeName()).toBe('Outra Loja');
    expect(storeContext.isOpen()).toBe(false);
    expect(fixture.componentInstance.storeResolved()).toBe(true);
  });

  it('does not surface the previous store active orders after a failed resolution', () => {
    const storeContext = TestBed.inject(StoreContextService);
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
    };

    const first = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    first.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });
    tracking.activeOrders.set([{ id: 'o1', storeId: 's1' }]);

    expect(fixture.componentInstance.activeOrdersCount()).toBe(1);

    const failing = resolveStore();
    paramMap$.next({ get: () => 'outra-loja' });
    failing.error(new Error('store not found'));

    expect(storeContext.storeId()).toBeNull();
    expect(fixture.componentInstance.activeOrdersCount()).toBe(0);

    const pedidos = fixture.componentInstance.footerItems().find((item) => item.id === 'pedidos');
    expect(pedidos?.disabled).toBe(true);
    expect(pedidos?.badge).toBe(0);
  });

  it('filters active orders by the newly resolved store id', () => {
    const storeContext = TestBed.inject(StoreContextService);
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      activeOrders: ReturnType<typeof signal<any[]>>;
    };
    tracking.activeOrders.set([
      { id: 'o1', storeId: 's1' },
      { id: 'o2', storeId: 's2' },
    ]);

    const first = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    first.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });

    expect(fixture.componentInstance.activeOrdersCount()).toBe(1);

    const second = resolveStore();
    paramMap$.next({ get: () => 'outra-loja' });
    second.next({ id: 's2', slug: 'outra-loja', name: 'Outra Loja', phoneNumber: '118888', isOpenNow: true });

    expect(storeContext.storeId()).toBe('s2');
    expect(fixture.componentInstance.activeOrdersCount()).toBe(1);
  });

  it('keeps the store unresolved when a resolution fails', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    failing.error(new Error('loja nao encontrada'));

    expect(fixture.componentInstance.storeResolved()).toBe(false);
  });

  it('renders an explicit not-found state, without a retry, for a 404 on the public store path', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'mixdesabores' });
    failing.error(new HttpErrorResponse({ status: 404, statusText: 'Not Found' }));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';
    expect(fixture.componentInstance.storeResolutionError()).toBe('not-found');
    expect(fixture.componentInstance.storeResolved()).toBe(false);
    expect(text).toContain('Loja não encontrada');
    expect(text).toContain('Este endereço não corresponde a uma loja disponível');
    expect(element.querySelector('.btn-retry')).toBeNull();
    expect(element.querySelector('.btn-home')).not.toBeNull();
  });

  it('renders the personalized 404 artwork hidden from assistive technology instead of the transient icon', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'mixdesabores' });
    failing.error(new HttpErrorResponse({ status: 404, statusText: 'Not Found' }));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const art = element.querySelector('.store-not-found-art');
    const svg = art?.querySelector('svg');

    expect(fixture.componentInstance.storeResolutionError()).toBe('not-found');
    expect(art).not.toBeNull();
    expect(art?.getAttribute('aria-hidden')).toBe('true');
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('aria-hidden')).toBe('true');
    expect(svg?.querySelectorAll('path').length).toBeGreaterThan(0);
    expect(element.querySelector('ion-icon')).toBeNull();
    expect((element.querySelector('.store-resolution-error') as HTMLElement).classList.contains('is-not-found')).toBe(true);
  });

  it('keeps the cloud icon and the retry action for transient failures, without the 404 artwork', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    failing.error(new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.storeResolutionError()).toBe('unavailable');
    expect(element.querySelector('.store-not-found-art')).toBeNull();
    expect(element.querySelector('ion-icon')).not.toBeNull();
    expect(fixture.componentInstance.storeResolutionIcon()).toBe('cloud-offline-outline');
    expect(element.querySelector('.btn-retry')).not.toBeNull();
    expect((element.querySelector('.store-resolution-error') as HTMLElement).classList.contains('is-not-found')).toBe(false);
  });

  it('keeps inaccessible stores on the lock icon without the 404 artwork or a retry', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    failing.error(new HttpErrorResponse({ status: 403, statusText: 'Forbidden' }));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.storeResolutionError()).toBe('inaccessible');
    expect(element.querySelector('.store-not-found-art')).toBeNull();
    expect(element.querySelector('ion-icon')).not.toBeNull();
    expect(fixture.componentInstance.storeResolutionIcon()).toBe('lock-closed-outline');
    expect(element.querySelector('.btn-retry')).toBeNull();
  });

  it('labels the 404 return action "Voltar ao início" and navigates to the root', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'mixdesabores' });
    failing.error(new HttpErrorResponse({ status: 404, statusText: 'Not Found' }));
    fixture.detectChanges();

    const home = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.btn-home');

    expect(home?.textContent?.trim()).toBe('Voltar ao início');

    home?.click();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/']);
  });

  it('navigates to the home page from the resolution error state', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'mixdesabores' });
    failing.error(new HttpErrorResponse({ status: 404, statusText: 'Not Found' }));
    fixture.detectChanges();

    const home = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.btn-home');
    home?.click();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/']);
  });

  it('classifies transient resolution failures as unavailable with a retry action', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    failing.error(new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.storeResolutionError()).toBe('unavailable');
    expect(element.textContent).toContain('Não foi possível carregar a loja');
    expect(element.querySelector('.btn-retry')).not.toBeNull();
  });

  it.each([0, 408, 429, 500])('treats HTTP %s as a transient resolution failure', (status) => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    failing.error(new HttpErrorResponse({ status, statusText: 'Transient' }));

    expect(fixture.componentInstance.storeResolutionError()).toBe('unavailable');
  });

  it.each([400, 401, 403])('does not promise a retry for HTTP %s', (status) => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    failing.error(new HttpErrorResponse({ status, statusText: 'Not available' }));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.storeResolutionError()).toBe('inaccessible');
    expect(element.textContent).toContain('Loja indisponível');
    expect(element.querySelector('.btn-retry')).toBeNull();
    expect(element.querySelector('.btn-home')).not.toBeNull();
  });

  it('hides a residual cart checkout action when a new store path cannot be resolved', () => {
    const cartCheckoutAction = TestBed.inject(CartCheckoutActionService);
    routerMock.url = '/loja/carrinho';

    const first = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    first.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });
    cartCheckoutAction.setState({ visible: true, disabled: false, detail: 'Pagamento' });
    fixture.componentInstance.isCartSheetOpen.set(true);
    fixture.componentInstance.isAccountMenuOpen.set(true);

    expect(fixture.componentInstance.showCartCheckoutAction()).toBe(true);

    routerMock.url = '/loja-inexistente/carrinho';
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja-inexistente' });
    failing.error(new HttpErrorResponse({ status: 404, statusText: 'Not Found' }));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.storeResolved()).toBe(false);
    expect(cartCheckoutAction.visible()).toBe(false);
    expect(fixture.componentInstance.showCartCheckoutAction()).toBe(false);
    expect(fixture.componentInstance.isCartSheetOpen()).toBe(false);
    expect(fixture.componentInstance.isAccountMenuOpen()).toBe(false);
    expect(element.querySelector('app-sticky-action-bar')).toBeNull();
    expect(element.querySelector('.store-resolution-error')).not.toBeNull();
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('offers a retry for transient resolution failures and applies the recovered store', () => {
    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    failing.error(new HttpErrorResponse({ status: 503, statusText: 'Service Unavailable' }));
    fixture.detectChanges();

    expect(fixture.componentInstance.storeResolutionError()).toBe('unavailable');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Tentar novamente');

    const retry = resolveStore();
    fixture.componentInstance.retryStoreResolution();
    retry.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });
    fixture.detectChanges();

    expect(fixture.componentInstance.storeResolutionError()).toBeNull();
    expect(fixture.componentInstance.storeResolved()).toBe(true);
    expect(TestBed.inject(StoreContextService).storeId()).toBe('s1');
  });

  it('keeps the paramMap stream alive after a failed resolution so another store path can retry', () => {
    const storeContext = TestBed.inject(StoreContextService);

    const failing = resolveStore();
    paramMap$.next({ get: () => 'loja-inexistente' });
    failing.error(new Error('loja nao encontrada'));

    expect(storeContext.storeId()).toBeNull();
    expect(fixture.componentInstance.storeResolved()).toBe(false);

    const retry = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    retry.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });

    expect(storeContext.storeId()).toBe('s1');
    expect(storeContext.storeName()).toBe('Loja');
    expect(fixture.componentInstance.storeResolved()).toBe(true);
  });

  it('clears the previous slug when resetting the store context', () => {
    const first = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    first.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });

    expect((fixture.componentInstance as any).storeSlug()).toBe('loja');

    const second = resolveStore();
    paramMap$.next({ get: () => 'outra-loja' });

    expect((fixture.componentInstance as any).storeSlug()).toBe('');
  });

  it('does not apply a store that resolves after the component is destroyed', () => {
    const storeContext = TestBed.inject(StoreContextService);

    const pending = resolveStore();
    paramMap$.next({ get: () => 'loja' });

    fixture.destroy();

    pending.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });

    expect(storeContext.storeId()).toBeNull();
    expect(storeContext.storeName()).toBeNull();
  });

  it('stays unresolved and keeps the stream alive when storePath is missing', () => {
    paramMap$.next({ get: () => null });

    expect(fixture.componentInstance.storeResolved()).toBe(false);

    const retry = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    retry.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });

    expect(fixture.componentInstance.storeResolved()).toBe(true);
    expect(TestBed.inject(StoreContextService).storeId()).toBe('s1');
  });

  it('stops the shared tracking service when the shell is destroyed', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService) as unknown as {
      start: jest.Mock;
      stop: jest.Mock;
      activeOrders: ReturnType<typeof signal<any[]>>;
    };

    const first = resolveStore();
    paramMap$.next({ get: () => 'loja' });
    first.next({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '119999', isOpenNow: true });

    expect(tracking.start).toHaveBeenCalled();

    fixture.destroy();

    expect(tracking.stop).toHaveBeenCalled();
  });
});

describe('StoreShellComponent account menu accessibility', () => {
  let fixture: ComponentFixture<StoreShellComponent>;
  let authMock: { customerProfile: ReturnType<typeof signal<any>>; logout: jest.Mock };

  beforeEach(async () => {
    authMock = {
      customerProfile: signal(null),
      logout: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of({ get: () => 'loja' }),
            snapshot: { paramMap: { get: () => 'loja' } },
          },
        },
        {
          provide: StoreService,
          useValue: {
            getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '', isOpenNow: true })),
          },
        },
        { provide: AuthService, useValue: authMock },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        { provide: CustomerOrderTrackingService, useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug.set('loja');
    authMock.customerProfile.set({ fullName: 'Maria Oliveira' });
    fixture.detectChanges();
    document.body.appendChild(fixture.nativeElement);
  });

  afterEach(() => {
    fixture.nativeElement.remove();
  });

  it('links the Conta trigger to the account menu with aria attributes', () => {
    fixture.componentInstance.openAccountMenu();
    fixture.detectChanges();

    const menu = fixture.nativeElement.querySelector('#account-menu');
    const trigger = fixture.nativeElement.querySelector('[data-footer-id="conta"]');

    expect(menu).not.toBeNull();
    expect(trigger?.getAttribute('aria-expanded')).toBe('true');
    expect(trigger?.getAttribute('aria-controls')).toBe('account-menu');
    expect(trigger?.getAttribute('aria-haspopup')).toBe('menu');
  });

  it('closes the account menu on Escape', () => {
    fixture.componentInstance.openAccountMenu();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#account-menu')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#account-menu')).toBeNull();
  });

  it('moves focus into the menu when opened and back to the trigger on Escape', async () => {
    fixture.componentInstance.openAccountMenu();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#account-menu button'));

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('[data-footer-id="conta"]'));
  });

  it('marks the account menu and its items with menu roles', () => {
    fixture.componentInstance.openAccountMenu();
    fixture.detectChanges();

    const menu = fixture.nativeElement.querySelector('#account-menu');
    const items = menu.querySelectorAll('button');

    expect(menu.getAttribute('role')).toBe('menu');
    expect(items.length).toBe(2);
    items.forEach((item: HTMLButtonElement) => expect(item.getAttribute('role')).toBe('menuitem'));
  });

  it('navigates to Cadastro and closes the menu when the first item is activated', () => {
    const router = TestBed.inject(Router);
    const navigate = jest.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.componentInstance.openAccountMenu();
    fixture.detectChanges();

    fixture.nativeElement.querySelector('#account-menu button').click();
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith(['/', 'loja', 'conta', 'cadastro']);
    expect(fixture.nativeElement.querySelector('#account-menu')).toBeNull();
  });

  it('logs out and closes the menu when Sair is activated', () => {
    fixture.componentInstance.openAccountMenu();
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('#account-menu button');
    items[1].click();
    fixture.detectChanges();

    expect(authMock.logout).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('#account-menu')).toBeNull();
  });

  it('closes the menu when the backdrop is clicked', () => {
    fixture.componentInstance.openAccountMenu();
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.account-menu-backdrop').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#account-menu')).toBeNull();
  });
});

describe('StoreShellComponent mobile footer layout', () => {
  it('does not push the store home route down and leaves footer clearance to each route scrollport', () => {
    const source = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');

    expect(source).toContain('[class.store-home]="isStoreHome()"');
    expect(source).toContain('[class.has-footer]="storeResolved() && showFooterNav()"');
    expect(source).not.toContain('.store-route.has-footer:not(.store-home)');
    expect(source).not.toContain('padding-bottom: calc(64px + max(8px, env(safe-area-inset-bottom, 0px)))');
  });

  it('keeps the shell as a column flex container so the footer stays within the viewport', () => {
    const source = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');

    expect(source).toContain('display: flex');
    expect(source).toContain('flex-direction: column');
  });

  it('exposes a pixel-valued footer-clearance property without adding fixed footer padding to the route', () => {
    const source = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');

    expect(source).toContain('(heightChange)="onFooterHeightChange($event)"');
    expect(source).toContain('[style.--store-footer-clearance.px]="exposedFooterClearance()"');
    expect(source).toMatch(/exposedFooterClearance\s*\(\s*\):\s*number\s*\{[\s\S]*showFooterNav\(\)\s*\?\s*this\.footerClearance\(\)\s*:\s*0/);
    expect(source).toMatch(/footerClearance\s*=\s*signal<number>\(72\)/);
    expect(source).toMatch(/\.store-route\s*\{[\s\S]*?\n\s*\}/);
    expect(source).not.toMatch(/\.store-route[\s\S]*padding-bottom/);
  });

  it('keeps the footer in the shell flex flow and sizes the shell to the dynamic viewport', () => {
    const shellSource = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');
    const globalStyles = readFileSync(resolve(__dirname, '../../../theme/global.scss'), 'utf8');
    const rootStyles = readFileSync(resolve(__dirname, '../../../styles.scss'), 'utf8');

    expect(shellSource).toMatch(/\.app-shell\s*\{[^}]*min-height:\s*100dvh/);
    expect(globalStyles).toMatch(/\.app-shell\s*\{[^}]*min-height:\s*100dvh/);
    expect(shellSource).toMatch(/\.store-route\s*\{[^}]*flex:\s*1 1 0/);
    expect(shellSource).toMatch(/\.store-route\s*\{[^}]*min-height:\s*0/);

    expect(rootStyles).not.toMatch(/body\.store-page-active\s+\.ion-page\s*\{[^}]*height:\s*100dvh/);
  });

  it('makes every routed store page flex to fill the space above the in-flow footer', () => {
    const globalStyles = readFileSync(resolve(__dirname, '../../../theme/global.scss'), 'utf8');

    expect(globalStyles).toMatch(/\.store-route\s*>\s*router-outlet\s*\+\s*\*[^{]*\{[^}]*flex:\s*1 1 0/);
  });

  it('inherits the measured footer clearance on mobile and only resets it on desktop', () => {
    const source = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');

    const baseRoute = source.match(/\.store-route\s*\{([\s\S]*?)\n\s*\}/)?.[1] ?? '';
    expect(baseRoute).not.toContain('--store-footer-clearance: 0px');
    expect(source).toMatch(/@media\s*\(min-width:\s*900px\)\s*\{[\s\S]*\.store-route\s*\{[^}]*--store-footer-clearance:\s*0px/);
  });

  it('renders the footer after the stage that holds the routed content and the cart sheet', () => {
    const shellSource = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');

    expect(shellSource).toMatch(/<div class="store-stage">[\s\S]*<app-cart-sheet/);
    expect(shellSource).toMatch(
      /<\/div>\s*@if \(showCartCheckoutAction\(\)\)[\s\S]*?\}\s*@if \(storeResolved\(\) && showFooterNav\(\)\)\s*\{\s*<app-footer-nav/,
    );
  });

  it('marks the routed store surface inert while the cart sheet is open using a strict-compatible binding', () => {
    const source = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');

    expect(source).toContain('[attr.inert]="isCartSheetOpen() ? \'\' : null"');
  });

  it('keeps the footer in document flow instead of anchoring it to a viewport or containing block', () => {
    const shellSource = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');
    const footerSource = readFileSync(resolve(__dirname, '../../shared/components/footer-nav/footer-nav.component.ts'), 'utf8');

    expect(shellSource).not.toMatch(/:host\(\.ion-page\)\s*\{[^}]*contain:/);
    expect(footerSource).not.toMatch(/:host\s*\{[^}]*position:\s*fixed/);
    expect(footerSource).not.toMatch(/:host\s*\{[^}]*bottom:\s*0/);
  });
});

describe('StoreShellComponent footer clearance propagation', () => {
  let fixture: ComponentFixture<StoreShellComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of({ get: () => 'loja' }),
            snapshot: { paramMap: { get: () => 'loja' } },
          },
        },
        {
          provide: StoreService,
          useValue: {
            getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '', isOpenNow: true })),
          },
        },
        { provide: AuthService, useValue: { customerProfile: signal(null), logout: jest.fn() } },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        { provide: CustomerOrderTrackingService, useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug.set('loja');
    fixture.detectChanges();
  });

  it('starts with a safe non-zero footer clearance before the first measurement', () => {
    const shell = fixture.debugElement.query(By.css('.app-shell')).nativeElement as HTMLElement;

    expect(shell.style.getPropertyValue('--store-footer-clearance')).toBe('72px');
  });

  it('updates the shell clearance property from the footer measured height output', () => {
    const footer = fixture.debugElement.query(By.directive(FooterNavComponent));
    expect(footer).not.toBeNull();

    footer.componentInstance.heightChange.emit(104);
    fixture.detectChanges();

    const shell = fixture.debugElement.query(By.css('.app-shell')).nativeElement as HTMLElement;
    expect(shell.style.getPropertyValue('--store-footer-clearance')).toBe('104px');
  });

  it('marks the FooterNav host as sheet-open and inert-equivalent while the cart sheet is open', () => {
    const footer = fixture.debugElement.query(By.directive(FooterNavComponent));
    expect(footer).not.toBeNull();
    const host = footer.nativeElement as HTMLElement;
    const firstButton = host.querySelector('.footer-nav button') as HTMLButtonElement;

    expect(host.classList.contains('sheet-open')).toBe(false);
    expect(footer.componentInstance.inert).toBe(false);
    expect(host.getAttribute('inert')).toBeNull();

    fixture.componentInstance.isCartSheetOpen.set(true);
    fixture.detectChanges();

    expect(host.classList.contains('sheet-open')).toBe(true);
    expect(footer.componentInstance.inert).toBe(true);
    expect(host.getAttribute('inert')).toBe('');
    expect(firstButton.getAttribute('aria-hidden')).toBe('true');
    expect(firstButton.getAttribute('tabindex')).toBe('-1');

    fixture.componentInstance.isCartSheetOpen.set(false);
    fixture.detectChanges();

    expect(host.classList.contains('sheet-open')).toBe(false);
    expect(footer.componentInstance.inert).toBe(false);
    expect(host.getAttribute('inert')).toBeNull();
  });

  it('keeps the cart sheet inside the stage that ends at the footer instead of wiring it to the measured clearance', () => {
    const cartSheet = fixture.debugElement.query(By.directive(CartSheetComponent));
    const stage = fixture.debugElement.query(By.css('.store-stage'));
    const footer = fixture.debugElement.query(By.directive(FooterNavComponent));
    expect(cartSheet).not.toBeNull();
    expect(stage).not.toBeNull();
    expect(footer).not.toBeNull();

    expect(stage.nativeElement.contains(cartSheet.nativeElement)).toBe(true);
    expect(stage.nativeElement.contains(footer.nativeElement)).toBe(false);
    expect('footerHeight' in cartSheet.componentInstance).toBe(false);
  });

  it('keeps the measured footer clearance available to the shell for the fixed checkout action', () => {
    const footer = fixture.debugElement.query(By.directive(FooterNavComponent));
    const shell = fixture.debugElement.query(By.css('.app-shell')).nativeElement as HTMLElement;
    expect(footer).not.toBeNull();

    footer.componentInstance.heightChange.emit(104);
    fixture.detectChanges();

    expect(shell.style.getPropertyValue('--store-footer-clearance')).toBe('104px');
  });

  it('clears the bag, dismisses the sheet and returns to the store menu when clearing is confirmed in the sheet', () => {
    const router = TestBed.inject(Router);
    const navigate = jest.spyOn(router, 'navigate').mockResolvedValue(true);
    const cart = TestBed.inject(CartService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    fixture.componentInstance.isCartSheetOpen.set(true);
    fixture.detectChanges();

    const sheet = fixture.nativeElement.querySelector('app-cart-sheet') as HTMLElement;
    (sheet.querySelector('[data-action="clear-cart"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (sheet.querySelector('.modal-actions .modal-confirm') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(cart.items()).toEqual([]);
    expect(fixture.componentInstance.isCartSheetOpen()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/', 'loja']);
  });
});

describe('StoreShellComponent storefront cart sheet flow', () => {
  let fixture: ComponentFixture<StoreShellComponent>;
  let navigate: jest.Mock;

  beforeEach(async () => {
    navigate = jest.fn().mockResolvedValue(true);

    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        { provide: Router, useValue: { url: '/loja', navigate } },
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of({ get: () => 'loja' }),
            snapshot: { paramMap: { get: () => 'loja' } },
          },
        },
        {
          provide: StoreService,
          useValue: {
            getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '', isOpenNow: true })),
          },
        },
        { provide: AuthService, useValue: { customerProfile: signal(null), logout: jest.fn(), isLoggedIn: () => false } },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        {
          provide: CustomerOrderTrackingService,
          useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug.set('loja');
    fixture.detectChanges();
  });

  function openSheet(): HTMLElement {
    fixture.componentInstance.onFooterSelect('carrinho');
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('app-cart-sheet') as HTMLElement;
  }

  it('anchors the sheet inside a stage that sits above the footer instead of relying on a measured footer height', () => {
    const stage = fixture.nativeElement.querySelector('.store-stage') as HTMLElement | null;
    const footer = fixture.nativeElement.querySelector('app-footer-nav') as HTMLElement | null;

    expect(stage).not.toBeNull();
    expect(stage!.querySelector('app-cart-sheet')).not.toBeNull();
    expect(stage!.querySelector('section.store-route')).not.toBeNull();
    expect(footer).not.toBeNull();
    expect(stage!.contains(footer)).toBe(false);
  });

  it('renders a clickable trash and next action when the sheet is opened from the footer', () => {
    TestBed.inject(CartService).items.set([
      { id: 'i1', productId: 'p1', productName: 'Macaribe', quantity: 1, unitPrice: 37.9 },
      { id: 'i2', productId: 'p2', productName: 'Crepe', quantity: 1, unitPrice: 36 },
    ]);
    const sheet = openSheet();
    const trash = sheet.querySelector('[data-action="clear-cart"]') as HTMLButtonElement;
    const next = sheet.querySelector('[data-action="next-step"]') as HTMLButtonElement;

    expect(trash).not.toBeNull();
    expect(trash.disabled).toBe(false);
    expect(next).not.toBeNull();
    expect(next.disabled).toBe(false);
    expect(sheet.querySelectorAll('[data-action="remove-item"]').length).toBe(2);
  });

  it('removes a single item from the integrated sheet without closing it', () => {
    const cart = TestBed.inject(CartService);
    cart.items.set([
      { id: 'i1', productId: 'p1', productName: 'Macaribe', quantity: 1, unitPrice: 37.9 },
      { id: 'i2', productId: 'p2', productName: 'Crepe', quantity: 1, unitPrice: 36 },
    ]);
    const sheet = openSheet();

    (sheet.querySelectorAll('[data-action="remove-item"]')[0] as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(cart.items().map((item) => item.id)).toEqual(['i2']);
    expect(fixture.componentInstance.isCartSheetOpen()).toBe(true);
  });

  it('navigates to the cart and dismisses the sheet from the next action', () => {
    const cart = TestBed.inject(CartService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'Macaribe', quantity: 1, unitPrice: 37.9 }]);
    const sheet = openSheet();

    (sheet.querySelector('[data-action="next-step"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith(['/', 'loja', 'carrinho']);
    expect(fixture.componentInstance.isCartSheetOpen()).toBe(false);
  });
});

describe('StoreShellComponent footer clearance respects hidden footer routes', () => {
  let fixture: ComponentFixture<StoreShellComponent>;

  function routeTo(url: string): void {
    const router = TestBed.inject(Router);
    jest.spyOn(router, 'url', 'get').mockReturnValue(url);
    fixture.detectChanges();
  }

  function shellClearance(): string {
    const shell = fixture.debugElement.query(By.css('.app-shell')).nativeElement as HTMLElement;
    return shell.style.getPropertyValue('--store-footer-clearance');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of({ get: () => 'loja' }),
            snapshot: { paramMap: { get: () => 'loja' } },
          },
        },
        {
          provide: StoreService,
          useValue: {
            getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '', isOpenNow: true })),
          },
        },
        { provide: AuthService, useValue: { customerProfile: signal(null), logout: jest.fn() } },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        { provide: CustomerOrderTrackingService, useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug.set('loja');
  });

  it('exposes zero clearance when the footer is hidden on a checkout route', () => {
    routeTo('/loja/checkout/cadastro');

    expect(fixture.debugElement.query(By.directive(FooterNavComponent))).toBeNull();
    expect(shellClearance()).toBe('0px');
  });

  it('drops the measured clearance when navigating to a hidden-footer checkout route', () => {
    routeTo('/loja/carrinho');

    const footer = fixture.debugElement.query(By.directive(FooterNavComponent));
    expect(footer).not.toBeNull();
    footer.componentInstance.heightChange.emit(104);
    fixture.detectChanges();
    expect(shellClearance()).toBe('104px');

    routeTo('/loja/checkout/cadastro');

    expect(fixture.debugElement.query(By.directive(FooterNavComponent))).toBeNull();
    expect(shellClearance()).toBe('0px');
  });

  it('restores the measured clearance when returning to a footer-present route', () => {
    routeTo('/loja/carrinho');
    fixture.debugElement.query(By.directive(FooterNavComponent)).componentInstance.heightChange.emit(104);
    fixture.detectChanges();
    expect(shellClearance()).toBe('104px');

    routeTo('/loja/checkout/cadastro');
    expect(shellClearance()).toBe('0px');

    routeTo('/loja/carrinho');

    expect(fixture.debugElement.query(By.directive(FooterNavComponent))).not.toBeNull();
    expect(shellClearance()).toBe('104px');
  });
});

describe('StoreShellComponent storefront surface background', () => {
  let fixture: ComponentFixture<StoreShellComponent>;
  let urlSpy: jest.SpyInstance;

  function routeTo(url: string): void {
    urlSpy.mockReturnValue(url);
    fixture.detectChanges();
  }

  function shellElement(): HTMLElement {
    return fixture.debugElement.query(By.css('.app-shell')).nativeElement as HTMLElement;
  }

  function storeRouteElement(): HTMLElement {
    return fixture.debugElement.query(By.css('section.store-route')).nativeElement as HTMLElement;
  }

  function ruleBody(source: string, selector: RegExp): string {
    return source.match(selector)?.[1] ?? '';
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of({ get: () => 'loja' }),
            snapshot: { paramMap: { get: () => 'loja' } },
          },
        },
        {
          provide: StoreService,
          useValue: {
            getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '', isOpenNow: true })),
          },
        },
        { provide: AuthService, useValue: { customerProfile: signal(null), logout: jest.fn() } },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        { provide: CustomerOrderTrackingService, useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug.set('loja');
    urlSpy = jest.spyOn(TestBed.inject(Router), 'url', 'get');
  });

  it('marks the store home with has-footer and store-home while its routed content carries the white surface', () => {
    routeTo('/loja');

    const route = storeRouteElement();
    expect(route.classList.contains('has-footer')).toBe(true);
    expect(route.classList.contains('store-home')).toBe(true);

    const routedContentStyles = readFileSync(resolve(__dirname, 'store-page.component.scss'), 'utf8');
    const routedHost = ruleBody(routedContentStyles, /:host\s*\{([^}]*)\}/);
    expect(routedHost).toMatch(/background:\s*var\(--app-surface, #fff\)/);
    expect(routedHost).not.toContain('--app-bg');
  });

  it('does not mark the footer-less checkout with has-footer', () => {
    routeTo('/loja/checkout/cadastro');

    const route = storeRouteElement();
    expect(route.classList.contains('has-footer')).toBe(false);
    expect(route.classList.contains('store-home')).toBe(false);
  });

  it('uses the white surface on the shell and the footer-present route without altering the base route', () => {
    routeTo('/loja');
    expect(storeRouteElement().classList.contains('has-footer')).toBe(true);
    expect(shellElement().classList.contains('has-footer')).toBe(false);

    routeTo('/loja/checkout/cadastro');
    expect(storeRouteElement().classList.contains('has-footer')).toBe(false);

    const source = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');
    const shell = ruleBody(source, /\.app-shell\s*\{([^}]*)\}/);
    expect(ruleBody(source, /\.store-route\.has-footer\s*\{([^}]*)\}/)).toMatch(/background:\s*var\(--app-surface/);
    expect(shell).toMatch(/background:\s*var\(--app-surface, #fff\)/);
    expect(shell).not.toContain('var(--app-shell-bg)');
    expect(ruleBody(source, /\.store-route\s*\{([^}]*)\}/)).not.toContain('background:');
  });

  it('makes the routed store surface inert while the cart sheet is open and restores it when closed', () => {
    routeTo('/loja');
    expect(storeRouteElement().getAttribute('inert')).toBeNull();

    fixture.componentInstance.isCartSheetOpen.set(true);
    fixture.detectChanges();
    expect(storeRouteElement().getAttribute('inert')).toBe('');

    fixture.componentInstance.isCartSheetOpen.set(false);
    fixture.detectChanges();
    expect(storeRouteElement().getAttribute('inert')).toBeNull();
  });
});

describe('StoreShellComponent cart checkout action', () => {
  let fixture: ComponentFixture<StoreShellComponent>;
  let urlSpy: jest.SpyInstance;

  function routeTo(url: string): void {
    urlSpy.mockReturnValue(url);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of({ get: () => 'loja' }),
            snapshot: { paramMap: { get: () => 'loja' } },
          },
        },
        {
          provide: StoreService,
          useValue: {
            getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', slug: 'loja', name: 'Loja', phoneNumber: '', isOpenNow: true })),
          },
        },
        { provide: AuthService, useValue: { customerProfile: signal(null), logout: jest.fn() } },
        { provide: CheckoutService, useValue: { resetCheckout: jest.fn() } },
        { provide: CustomerOrderTrackingService, useValue: { activeOrders: signal([]), trackedOrders: signal([]), hasTrackedOrders: signal(false), start: jest.fn(), stop: jest.fn(), reset: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug.set('loja');
    urlSpy = jest.spyOn(TestBed.inject(Router), 'url', 'get');
    urlSpy.mockReturnValue('/loja/carrinho');
    fixture.detectChanges();
  });

  it('places the checkout action directly before the footer on the cart route', () => {
    TestBed.inject(CartCheckoutActionService).setState({ visible: true, disabled: false });
    fixture.detectChanges();

    const structural = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('app-sticky-action-bar, app-footer-nav'),
    ) as HTMLElement[];

    expect(structural.length).toBe(2);
    expect(structural[0].tagName.toLowerCase()).toBe('app-sticky-action-bar');
    expect(structural[1].tagName.toLowerCase()).toBe('app-footer-nav');
  });

  it('renders the cart checkout action as an in-flow row instead of a fixed overlay', () => {
    TestBed.inject(CartCheckoutActionService).setState({ visible: true, disabled: false });
    fixture.detectChanges();

    const action = fixture.nativeElement.querySelector('app-sticky-action-bar') as HTMLElement;

    expect(action).not.toBeNull();
    expect(action.classList.contains('in-flow')).toBe(true);
    expect(action.classList.contains('sticky-action-primary')).toBe(true);
  });

  it('keeps the in-flow checkout action structurally outside the footer nav in its own row', () => {
    TestBed.inject(CartCheckoutActionService).setState({ visible: true, disabled: false });
    fixture.detectChanges();

    const action = fixture.nativeElement.querySelector('app-sticky-action-bar') as HTMLElement;
    const footer = fixture.nativeElement.querySelector('app-footer-nav') as HTMLElement;

    expect(action.parentElement).toBe(footer.parentElement);
    expect(action.nextElementSibling).toBe(footer);
    expect(footer.contains(action)).toBe(false);
  });

  it('passes the inline placement to the cart checkout action so it owns layout space above the footer', () => {
    const source = readFileSync(resolve(__dirname, 'store-shell.component.ts'), 'utf8');

    expect(source).toMatch(/<app-sticky-action-bar[\s\S]*?placement="inline"/);
  });

  it('makes the sticky checkout action inert while the cart sheet is open and restores it when closed', () => {
    TestBed.inject(CartCheckoutActionService).setState({ visible: true, disabled: false });
    fixture.detectChanges();

    const action = fixture.nativeElement.querySelector('app-sticky-action-bar') as HTMLElement;
    expect(action).not.toBeNull();
    expect(action.getAttribute('inert')).toBeNull();

    fixture.componentInstance.isCartSheetOpen.set(true);
    fixture.detectChanges();
    expect(action.getAttribute('inert')).toBe('');

    fixture.componentInstance.isCartSheetOpen.set(false);
    fixture.detectChanges();
    expect(action.getAttribute('inert')).toBeNull();
  });

  it('hides the checkout action on non-cart routes even when the action is published', () => {
    TestBed.inject(CartCheckoutActionService).setState({ visible: true, disabled: false });
    routeTo('/loja');

    expect(fixture.debugElement.query(By.css('app-sticky-action-bar'))).toBeNull();
  });

  it('hides the checkout action when the cart page has not published a visible state', () => {
    routeTo('/loja/carrinho');

    expect(fixture.debugElement.query(By.css('app-sticky-action-bar'))).toBeNull();
  });

  it('binds the disabled condition from the shared checkout action service', () => {
    TestBed.inject(CartCheckoutActionService).setState({ visible: true, disabled: true });
    fixture.detectChanges();

    const action = fixture.debugElement.query(By.directive(StickyActionBarComponent));
    expect(action).not.toBeNull();
    expect(action.componentInstance.disabled).toBe(true);
    expect((action.nativeElement.querySelector('.sticky-action') as HTMLButtonElement).disabled).toBe(true);
  });

  it('forwards the action click to the shared continue request stream', () => {
    const service = TestBed.inject(CartCheckoutActionService);
    const requestContinue = jest.spyOn(service, 'requestContinue');
    service.setState({ visible: true, disabled: false });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.sticky-action') as HTMLButtonElement).click();

    expect(requestContinue).toHaveBeenCalledTimes(1);
  });
});
