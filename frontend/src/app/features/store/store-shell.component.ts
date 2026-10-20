import { Component, OnInit, OnDestroy, computed, inject, signal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { EMPTY, Observable, catchError, filter, Subscription, switchMap } from 'rxjs';

import { StorePublicDetails } from '../../shared/models/store.model';
import { StoreContextService } from '../../core/services/store-context.service';
import { StoreService } from '../../core/services/store.service';
import { CartService } from '../../core/services/cart.service';
import { AuthService } from '../../core/services/auth.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { CartCheckoutActionService } from '../../core/services/cart-checkout-action.service';
import { FooterNavComponent, FooterNavItem } from '../../shared/components/footer-nav/footer-nav.component';
import { CartSheetComponent } from '../../shared/components/cart-sheet/cart-sheet.component';
import { StickyActionBarComponent } from '../../shared/components/sticky-action-bar/sticky-action-bar.component';

type StoreResolutionError = 'not-found' | 'unavailable' | 'inaccessible';

@Component({
  selector: 'app-store-shell',
  standalone: true,
    imports: [CommonModule, RouterOutlet, IonIcon, FooterNavComponent, CartSheetComponent, StickyActionBarComponent],
  template: `
     <main
       class="app-shell"
       [style.--store-footer-clearance.px]="exposedFooterClearance()">
       <div class="store-stage">
         <section class="store-route" [class.has-footer]="storeResolved() && showFooterNav()" [class.store-home]="isStoreHome()" [attr.inert]="isCartSheetOpen() ? '' : null">
           @if (storeResolutionError(); as resolutionError) {
             <div
               class="store-resolution-error"
               [class.is-not-found]="resolutionError === 'not-found'"
               role="alert">
               @if (resolutionError === 'not-found') {
                 <div class="store-not-found-art" aria-hidden="true">
                   <svg viewBox="0 0 220 176" fill="none" xmlns="http://www.w3.org/2000/svg" focusable="false" aria-hidden="true">
                     <circle cx="110" cy="96" r="74" fill="var(--app-brand-soft, #FDECEE)" />
                     <circle cx="42" cy="44" r="3.5" fill="var(--app-brand, #D54A51)" opacity=".18" />
                     <circle cx="188" cy="126" r="4.5" fill="var(--app-brand, #D54A51)" opacity=".14" />
                     <ellipse cx="110" cy="158" rx="57" ry="6.5" fill="var(--app-border-light, #eadfd6)" />
                     <rect x="52" y="76" width="116" height="80" rx="14" fill="var(--app-surface, #fff)" stroke="var(--app-brand, #D54A51)" stroke-width="2.5" />
                     <path d="M48 62a10 10 0 0 1 9.7-10h104.6a10 10 0 0 1 9.7 10l5.4 16H42.6L48 62Z" fill="var(--app-brand, #D54A51)" />
                     <path d="M70 54 66 76M90 54 88 76M130 54 132 76M150 54 154 76" stroke="var(--app-surface, #fff)" stroke-width="6" stroke-linecap="round" opacity=".9" />
                     <rect x="66" y="94" width="24" height="24" rx="6" fill="var(--app-brand-soft, #FDECEE)" stroke="var(--app-brand, #D54A51)" stroke-width="2" />
                     <rect x="130" y="94" width="24" height="24" rx="6" fill="var(--app-brand-soft, #FDECEE)" stroke="var(--app-brand, #D54A51)" stroke-width="2" />
                     <path d="M71 112 78 100M135 112 142 100" stroke="var(--app-surface, #fff)" stroke-width="2.5" stroke-linecap="round" />
                     <path d="M94 104a12 12 0 0 1 12-12h8a12 12 0 0 1 12 12v52H94v-52Z" fill="var(--app-brand-soft, #FDECEE)" stroke="var(--app-brand, #D54A51)" stroke-width="2" />
                     <circle cx="118" cy="128" r="2.4" fill="var(--app-brand, #D54A51)" />
                     <g class="art-pin">
                       <path d="M110 11c-9.4 0-17 7.6-17 17 0 8.4 5.6 15.6 10.9 21.6 2.4 2.7 4.8 4.9 6.1 6.1 1.3-1.2 3.7-3.4 6.1-6.1C121.4 43.6 127 36.4 127 28c0-9.4-7.6-17-17-17Z" fill="var(--app-brand, #D54A51)" />
                       <circle cx="110" cy="28" r="6.5" fill="var(--app-surface, #fff)" />
                       <circle cx="110" cy="28" r="2.6" fill="var(--app-brand, #D54A51)" />
                     </g>
                   </svg>
                 </div>
               } @else {
                 <ion-icon [name]="storeResolutionIcon()" aria-hidden="true"></ion-icon>
               }
               <h1>
                 @if (resolutionError === 'not-found') {
                   Loja não encontrada
                 } @else if (resolutionError === 'unavailable') {
                   Não foi possível carregar a loja
                 } @else {
                   Loja indisponível
                 }
               </h1>
               <p>
                 @if (resolutionError === 'not-found') {
                   Este endereço não corresponde a uma loja disponível. Confira o link ou entre em contato com o estabelecimento.
                 } @else if (resolutionError === 'unavailable') {
                   Verifique sua conexão e tente novamente.
                 } @else {
                   Não foi possível acessar esta loja agora. Volte ao início ou tente novamente mais tarde.
                 }
               </p>
                <div class="store-resolution-actions">
                  @if (resolutionError === 'unavailable') {
                    <button type="button" class="btn-retry" (click)="retryStoreResolution()">Tentar novamente</button>
                  }
                  <button type="button" class="btn-home" (click)="goHome()">Voltar ao início</button>
                </div>
              </div>
            } @else if (storeResolved()) {
              <router-outlet />
            }
         </section>
         <app-cart-sheet [isOpen]="isCartSheetOpen()" (close)="closeCartSheet()" (next)="goToCart()" (cleared)="onCartCleared()" />
       </div>
       @if (showCartCheckoutAction()) {
         <app-sticky-action-bar
           placement="inline"
           appearance="primary"
           icon="bag-outline"
           label="Continuar"
           [attr.inert]="isCartSheetOpen() ? '' : null"
           [detail]="cartCheckoutAction.detail()"
           [disabled]="cartCheckoutAction.disabled()"
           (action)="cartCheckoutAction.requestContinue()" />
       }
       @if (storeResolved() && showFooterNav()) {
         <app-footer-nav
           [class.sheet-open]="isCartSheetOpen() || isAccountMenuOpen()"
           [inert]="isCartSheetOpen() || isAccountMenuOpen()"
           [attr.inert]="isCartSheetOpen() ? '' : null"
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

     /* The stage owns the area above the footer. The cart sheet is anchored to
        its bottom, so the sheet can never slide behind the in-flow footer and
        does not depend on an asynchronously measured footer height. */
    .store-stage {
      position: relative;
      flex: 1 1 0;
      min-height: 0;
      width: 100%;
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

    .store-resolution-error {
      display: flex;
      flex: 1 1 0;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      justify-content: safe center;
      gap: 12px;
      width: 100%;
      min-height: 0;
      padding: 32px 24px calc(32px + env(safe-area-inset-bottom, 0px));
      overflow-y: auto;
      background: var(--app-surface, #fff);
      color: var(--app-ink, #161616);
      text-align: center;
    }

    .store-resolution-error ion-icon {
      font-size: 48px;
      color: var(--app-brand, #D54A51);
    }

    /* Personalized 404 artwork: a bordeaux storefront with a floating location
       pin, built with inline SVG so it follows the theme tokens and never
       depends on an external asset. */
    .store-not-found-art {
      width: clamp(150px, 46vw, 200px);
      max-width: 100%;
      margin: 0 auto 2px;
      line-height: 0;
    }

    .store-not-found-art svg {
      display: block;
      width: 100%;
      height: auto;
      overflow: visible;
    }

    .store-not-found-art .art-pin {
      animation: store-pin-float 3.6s ease-in-out infinite;
    }

    /* Translation only, so the motion never depends on a transform origin
       inside the SVG view box. */
    @keyframes store-pin-float {
      0%, 100% { translate: 0 0; }
      50% { translate: 0 -4px; }
    }

    @media (prefers-reduced-motion: reduce) {
      .store-not-found-art .art-pin { animation: none; }
    }

    /* Short landscape screens: shrink the art so the message and the return
       action stay visible without forcing a scroll. */
    @media (max-height: 560px) {
      .store-not-found-art { width: 124px; }
    }

    .store-resolution-error h1 {
      margin: 0;
      font-size: 20px;
      line-height: 1.25;
    }

    .store-resolution-error p {
      max-width: 34ch;
      margin: 0;
      color: var(--app-text-secondary, #5a5a63);
      font-size: 15px;
      line-height: 1.5;
    }

    .store-resolution-actions {
      display: flex;
      flex-direction: column;
      gap: 8px;
      width: min(280px, 100%);
      margin-top: 8px;
    }

    .store-resolution-actions button {
      min-height: 44px;
      padding: 0 20px;
      border-radius: var(--radius-full, 999px);
      font: inherit;
      font-weight: 700;
      cursor: pointer;
    }

    .store-resolution-actions button:focus-visible {
      outline: 2px solid var(--app-brand, #D54A51);
      outline-offset: 2px;
    }

    .btn-retry {
      border: 0;
      background: var(--app-brand, #D54A51);
      color: #fff;
    }

    .btn-home {
      border: 1px solid var(--app-border-medium, #d6d0cc);
      background: transparent;
      color: var(--app-ink, #161616);
    }

    /* The 404 state has a single primary action, so its return button carries
       the bordeaux fill. Transient/inaccessible states keep the retry (when
       present) as the only bordeaux action. */
    .store-resolution-error.is-not-found .btn-home {
      border-color: transparent;
      background: var(--app-brand, #D54A51);
      color: #fff;
    }

    .store-resolution-error.is-not-found .btn-home:active {
      background: var(--app-brand-dark, #B63A41);
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
  readonly cartCheckoutAction = inject(CartCheckoutActionService);

  readonly storeResolved = signal(false);
  readonly storeResolutionError = signal<StoreResolutionError | null>(null);
  readonly storeResolutionIcon = computed(() =>
    this.storeResolutionError() === 'inaccessible' ? 'lock-closed-outline' : 'cloud-offline-outline',
  );
  readonly isCartSheetOpen = signal(false);
  readonly isAccountMenuOpen = signal(false);
  readonly footerClearance = signal<number>(72);

  private readonly storeSlug = signal('');
  private readonly currentUrl = signal(this.router.url);
  private routerEventsSubscription?: Subscription;
  private storeResolutionSubscription?: Subscription;
  private retrySubscription?: Subscription;
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
          this.retrySubscription?.unsubscribe();
          this.resetStoreContext();
          if (!storePath) return EMPTY;
          return this.resolveStore(storePath);
        }),
      )
      .subscribe((store) => this.applyResolvedStore(store));
  }

  private resolveStore(storePath: string): Observable<StorePublicDetails> {
    return this.storeService.getStoreByPath(storePath).pipe(
      catchError((error: unknown) => {
        this.storeResolutionError.set(this.classifyStoreResolutionError(error));
        return EMPTY;
      }),
    );
  }

  private classifyStoreResolutionError(error: unknown): StoreResolutionError {
    if (!(error instanceof HttpErrorResponse)) {
      return 'unavailable';
    }
    if (error.status === 404) {
      return 'not-found';
    }
    if (error.status === 0 || error.status === 408 || error.status === 429 || error.status >= 500) {
      return 'unavailable';
    }
    return 'inaccessible';
  }

  private applyResolvedStore(store: StorePublicDetails): void {
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
  }

  retryStoreResolution(): void {
    const storePath = this.route.snapshot?.paramMap?.get('storePath') ?? '';
    this.retrySubscription?.unsubscribe();
    this.resetStoreContext();
    if (!storePath) {
      this.storeResolutionError.set('not-found');
      return;
    }
    this.retrySubscription = this.resolveStore(storePath).subscribe((store) => this.applyResolvedStore(store));
  }

  goHome(): void {
    void this.router.navigate(['/']);
  }

  private resetStoreContext(): void {
    this.storeResolved.set(false);
    this.storeResolutionError.set(null);
    this.cartCheckoutAction.reset();
    this.isCartSheetOpen.set(false);
    this.isAccountMenuOpen.set(false);
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

  showCartCheckoutAction(): boolean {
    return this.storeResolved()
      && this.cartCheckoutAction.visible()
      && this.router.url.includes('/carrinho');
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

  onCartCleared(): void {
    this.closeCartSheet();
    this.navigate('');
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
    this.retrySubscription?.unsubscribe();
    if (this.trackingStarted) {
      this.tracking.stop();
    }
    document.body.classList.remove('store-page-active');
  }
}
