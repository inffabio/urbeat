import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { StoreShellComponent } from './store-shell.component';
import { FooterNavComponent } from '../../shared/components/footer-nav/footer-nav.component';
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

  beforeEach(async () => {
    getStoreByPath = jest.fn();
    paramMap$ = new Subject();

    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        { provide: Router, useValue: { url: '/loja', navigate: jest.fn() } },
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

  it('keeps the shell as a column flex container so the fixed footer stays within the viewport', () => {
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
