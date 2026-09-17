import { Component, OnInit, OnDestroy, computed, inject, signal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { EMPTY, catchError, filter, Subscription, switchMap } from 'rxjs';

import { StoreContextService } from '../../core/services/store-context.service';
import { StoreService } from '../../core/services/store.service';
import { CartService } from '../../core/services/cart.service';
import { AuthService } from '../../core/services/auth.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { FooterNavComponent, FooterNavItem } from '../../shared/components/footer-nav/footer-nav.component';
import { CartSheetComponent } from '../../shared/components/cart-sheet/cart-sheet.component';

@Component({
  selector: 'app-store-shell',
  standalone: true,
    imports: [CommonModule, RouterOutlet, IonIcon, FooterNavComponent, CartSheetComponent],
  template: `
     <main
       class="app-shell"
       [style.--store-footer-clearance.px]="exposedFooterClearance()">
       <section class="store-route" [class.has-footer]="storeResolved() && showFooterNav()" [class.store-home]="isStoreHome()">
        @if (storeResolved()) {
          <router-outlet />
        }
      </section>
       @if (storeResolved() && showFooterNav()) {
         <app-footer-nav
           [class.sheet-open]="isCartSheetOpen() || isAccountMenuOpen()"
           [inert]="isCartSheetOpen() || isAccountMenuOpen()"
           [items]="footerItems()"
           (select)="onFooterSelect($event)"
           (heightChange)="onFooterHeightChange($event)" />
       }
        @if (isAccountMenuOpen()) {
          <div class="account-menu-backdrop" role="presentation" (click)="closeAccountMenu()"></div>
          <nav id="account-menu" class="account-menu" role="menu" aria-label="Menu da conta" (click)="$event.stopPropagation()">
           <button type="button" role="menuitem" (click)="openAccountProfile()">
             <ion-icon name="person-outline" aria-hidden="true"></ion-icon>
             <span>Cadastro</span>
           </button>
           <button type="button" role="menuitem" (click)="logoutCustomer()">
             <ion-icon name="log-out-outline" aria-hidden="true"></ion-icon>
             <span>Sair</span>
           </button>
         </nav>
       }
       <app-cart-sheet [isOpen]="isCartSheetOpen()" (close)="closeCartSheet()" (next)="goToCart()" />
    </main>
  `,
  styles: [`
     .app-shell {
      font-family: var(--app-font);
      overflow: hidden;
      position: relative;
      min-height: 100dvh;
      height: 100dvh;
      background: var(--app-shell-bg);
      display: flex;
       flex-direction: column;
     }

     /* The route owns scrolling; the footer is a real flex item below it. */
    .store-route {
      flex: 1 1 0;
      min-height: 0;
      width: 100%;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    /* When the footer is present, the routed storefront surface carries the
       product surface color so no shell/background gap shows between the last
       product and the fixed footer. Scoped to .has-footer so footer-less
       routes and the hero/route canvases keep their own backgrounds. */
    .store-route.has-footer {
      background: var(--app-surface, #fff);
    }

     @media (min-width: 900px) {
       .app-shell {
         width: 100%;
         max-width: 430px;
         min-height: calc(100dvh - 56px);
         height: calc(100dvh - 56px);
         margin: 0 auto;
         box-shadow: 0 30px 90px rgba(0, 0, 0, .18);
       }

       /* On desktop the footer returns to the shell flex flow, so the measured
          clearance is not needed and is reset to avoid double spacing. */
       .store-route {
         --store-footer-clearance: 0px;
       }
     }

     .account-menu-backdrop {
       position: fixed;
       inset: 0;
       z-index: 55;
       background: rgba(22, 22, 22, .18);
     }

     .account-menu {
       position: absolute;
       right: max(12px, calc(50% - 203px));
       bottom: calc(72px + max(8px, env(safe-area-inset-bottom, 0px)));
       z-index: 60;
       display: grid;
       width: min(190px, calc(100% - 24px));
       padding: 8px;
       border: 1px solid var(--app-border-light, #eadfd6);
       border-radius: 18px;
       background: var(--app-surface, #fff);
       box-shadow: var(--shadow-md);
       animation: account-menu-rise .18s ease-out both;
     }

     .account-menu button {
       display: flex;
       align-items: center;
       gap: 10px;
       min-height: 46px;
       padding: 0 12px;
       border: 0;
       border-radius: 12px;
       background: transparent;
       color: var(--app-ink, #161616);
       font: inherit;
       font-size: 14px;
       font-weight: 700;
       text-align: left;
       cursor: pointer;
     }

     .account-menu button:hover,
     .account-menu button:focus-visible { background: var(--app-brand-soft, #FDECEE); color: var(--app-brand, #D54A51); outline: none; }
     .account-menu ion-icon { font-size: 20px; color: var(--app-brand, #D54A51); }

     @keyframes account-menu-rise { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
     @media (prefers-reduced-motion: reduce) { .account-menu { animation: none; } }

  `],
})
export class StoreShellComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly storeService = inject(StoreService);
  readonly cart = inject(CartService);
  readonly auth = inject(AuthService);
  readonly storeContext = inject(StoreContextService);
  private readonly checkout = inject(CheckoutService);
  private readonly tracking = inject(CustomerOrderTrackingService);

  readonly storeResolved = signal(false);
  readonly isCartSheetOpen = signal(false);
  readonly isAccountMenuOpen = signal(false);
  readonly footerClearance = signal<number>(72);

  private readonly storeSlug = signal('');
  private readonly currentUrl = signal(this.router.url);
  private routerEventsSubscription?: Subscription;
  private storeResolutionSubscription?: Subscription;
  private trackingStarted = false;

  readonly activeOrdersCount = computed(() =>
    this.tracking.activeOrders().filter((order) => order.storeId === this.storeContext.storeId()).length,
  );

  readonly trackedOrdersCount = computed(() =>
    this.tracking.trackedOrders().filter((order) => order.storeId === this.storeContext.storeId()).length,
  );

  readonly footerItems = computed<FooterNavItem[]>(() => [
    { id: 'cardapio', icon: 'storefront-outline', label: 'Cardapio', active: this.isStoreHome() },
    { id: 'carrinho', icon: 'bag-check-outline', label: 'Carrinho', active: this.currentUrl().includes('/carrinho'), badge: this.cart.totalItems() },
    {
      id: 'pedidos',
      icon: 'receipt-outline',
      label: 'Pedidos',
      active: this.currentUrl().includes('/pedidos'),
      disabled: this.trackedOrdersCount() === 0,
      badge: this.activeOrdersCount(),
      badgeLabel: 'pedidos',
    },
    { id: 'conta', icon: 'person-circle-outline', label: 'Conta', disabled: !this.auth.customerProfile(), ariaExpanded: this.isAccountMenuOpen(), ariaControls: 'account-menu' },
  ]);

  ngOnInit(): void {
    document.body.classList.add('store-page-active');
    if (this.router.events) {
      this.routerEventsSubscription = this.router.events
        .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
        .subscribe((event) => this.currentUrl.set(event.urlAfterRedirects));
    }
    this.storeResolutionSubscription = this.route.paramMap
      .pipe(
        switchMap((params) => {
          const storePath = params.get('storePath');
          this.resetStoreContext();
          if (!storePath) return EMPTY;
          return this.storeService.getStoreByPath(storePath).pipe(
            catchError(() => EMPTY),
          );
        }),
      )
      .subscribe((store) => {
        this.storeSlug.set(store.slug);
        this.storeContext.storeId.set(store.id);
        this.storeContext.storeName.set(store.name);
        this.storeContext.phoneNumber.set(store.phoneNumber);
        this.storeContext.isOpen.set(store.isOpenNow);
        this.storeResolved.set(true);
        if (!this.trackingStarted) {
          this.trackingStarted = true;
          void this.tracking.start();
        }
      });
  }

  private resetStoreContext(): void {
    this.storeResolved.set(false);
    this.storeSlug.set('');
    this.storeContext.storeId.set(null);
    this.storeContext.storeName.set(null);
    this.storeContext.phoneNumber.set(null);
    this.storeContext.isOpen.set(true);
  }

  isActive(path: string): boolean {
    const url = this.router.url;
    if (path === '/') return !url.includes('/carrinho') && !url.includes('/pedido') && !url.includes('/checkout') && !url.includes('/pedidos') && !url.includes('/conta');
    return url.includes(`/${this.storeSlug}${path}`);
  }

  isStoreHome(): boolean {
    const url = this.router.url;
    const slug = this.storeSlug();
    if (!slug) return false;
     return url === `/${slug}` || url === `/${slug}/`;
  }

  showFooterNav(): boolean {
    const isPayment = this.router.url.includes('/checkout/pagamento');
    return !this.router.url.includes('/checkout/') || isPayment;
  }

  navigate(path: string): void {
    const segments = path.split('/').filter((segment) => segment.length > 0);
    this.router.navigate(['/', this.currentSlug(), ...segments]);
  }

  private currentSlug(): string {
    return this.storeSlug() || this.route.snapshot?.paramMap?.get('storePath') || '';
  }

  onFooterSelect(id: string): void {
    if (id === 'cardapio') this.navigate('');
    if (id === 'carrinho') this.isCartSheetOpen.set(true);
    if (id === 'pedidos') this.navigate('pedidos');
    if (id === 'conta') this.openAccountMenu();
  }

  onFooterHeightChange(height: number): void {
    this.footerClearance.set(height);
  }

  exposedFooterClearance(): number {
    return this.storeResolved() && this.showFooterNav() ? this.footerClearance() : 0;
  }

  openAccountMenu(): void {
    this.isAccountMenuOpen.set(true);
    setTimeout(() => this.focusAccountMenuFirstItem());
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.isAccountMenuOpen()) {
      this.closeAccountMenu();
      setTimeout(() => this.focusAccountTrigger());
    }
  }

  private focusAccountMenuFirstItem(): void {
    document.querySelector<HTMLElement>('#account-menu button')?.focus();
  }

  private focusAccountTrigger(): void {
    document.querySelector<HTMLElement>('[data-footer-id="conta"]')?.focus();
  }

  closeCartSheet(): void {
    this.isCartSheetOpen.set(false);
  }

  goToCart(): void {
    if (this.cart.isEmpty()) return;
    this.closeCartSheet();
    this.navigate('carrinho');
  }

  closeAccountMenu(): void {
    this.isAccountMenuOpen.set(false);
  }

  openAccountProfile(): void {
    this.closeAccountMenu();
    this.navigate('conta/cadastro');
  }

  logoutCustomer(): void {
    this.auth.logout();
    this.checkout.resetCheckout();
    this.closeAccountMenu();
    this.router.navigate(['/', this.currentSlug()]);
  }

  ngOnDestroy(): void {
    this.routerEventsSubscription?.unsubscribe();
    this.storeResolutionSubscription?.unsubscribe();
    if (this.trackingStarted) {
      this.tracking.stop();
    }
    document.body.classList.remove('store-page-active');
  }
}
