import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterOutlet } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { switchMap } from 'rxjs';

import { StoreContextService } from '../../core/services/store-context.service';
import { StoreService } from '../../core/services/store.service';

@Component({
  selector: 'app-store-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, IonIcon],
  template: `
    <main class="app-shell">
      <section class="store-route">
        <router-outlet />
      </section>
      @if (storeResolved() && showFooterNav()) {
        <footer class="footer-nav" aria-label="Navegação principal">
          <a
            [class.active]="isActive('/')"
            (click)="navigate('')"
            [attr.aria-current]="isActive('/') ? 'page' : undefined"
          >
            <ion-icon name="storefront-outline" aria-hidden="true"></ion-icon>
            <span>Cardápio</span>
          </a>
          <a
            class="disabled"
            aria-disabled="true"
          >
            <ion-icon name="receipt-outline" aria-hidden="true"></ion-icon>
            <span>Pedidos</span>
          </a>
          <a
            [class.active]="isActive('/carrinho')"
            (click)="navigate('carrinho')"
          >
            <ion-icon name="bag-check-outline" aria-hidden="true"></ion-icon>
            <span>Carrinho</span>
          </a>
          <a
            class="disabled"
            aria-disabled="true"
          >
            <ion-icon name="person-circle-outline" aria-hidden="true"></ion-icon>
            <span>Conta</span>
          </a>
        </footer>
      }
    </main>
  `,
  styles: [`
    .app-shell {
      font-family: var(--app-font);
      overflow-x: hidden;
      position: relative;
      min-height: 100vh;
      background: var(--app-shell-bg);
      display: flex;
      flex-direction: column;
    }

    .store-route {
      flex: 1;
      min-height: 0;
    }

    .footer-nav {
      min-height: 78px;
      background: rgba(255, 255, 255, .96);
      border-top: 1px solid rgba(234, 223, 214, .9);
      box-shadow: 0 -14px 30px rgba(0, 0, 0, .06);
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      align-items: center;
      padding: 7px 4px 9px;
      position: relative;
      z-index: 1;
      border-radius: 0;
      flex-shrink: 0;
      box-sizing: border-box;
      margin-bottom: max(28px, env(safe-area-inset-bottom, 0px));
    }

    .footer-nav a {
      min-height: 54px;
      display: grid;
      place-items: center;
      gap: 4px;
      color: #505258;
      font-size: 12px;
      font-weight: 500;
      text-decoration: none;
      cursor: pointer;
      transition: color .18s ease;
    }

    .footer-nav a ion-icon {
      font-size: 22px;
      line-height: 1;
    }

    .footer-nav a.active,
    .footer-nav a.active ion-icon,
    .footer-nav a:hover,
    .footer-nav a:hover ion-icon {
      color: var(--app-brand);
      outline: none;
    }

    .footer-nav a:active {
      transform: scale(.96);
    }

    .footer-nav a.disabled {
      opacity: .4;
      cursor: not-allowed;
      pointer-events: none;
    }

    @media (max-width: 400px) {
      .footer-nav a { font-size: 11px; }
      .footer-nav a ion-icon { font-size: 21px; }
    }
  `],
})
export class StoreShellComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly storeService = inject(StoreService);
  readonly storeContext = inject(StoreContextService);

  readonly storeResolved = signal(false);

  private storeSlug = '';

  ngOnInit(): void {
    document.body.classList.add('store-page-active');
    this.route.paramMap
      .pipe(
        switchMap((params) => {
          const storePath = params.get('storePath');
          if (!storePath) throw new Error('no storePath');
          return this.storeService.getStoreByPath(storePath);
        }),
      )
      .subscribe({
        next: (store) => {
          this.storeSlug = store.slug;
          this.storeContext.storeName.set(store.name);
          this.storeContext.phoneNumber.set(store.phoneNumber);
          this.storeContext.isOpen.set(store.isOpenNow);
          this.storeResolved.set(true);
        },
        error: () => {
          this.storeResolved.set(true);
        },
      });
  }

  isActive(path: string): boolean {
    const url = this.router.url;
    if (path === '/') return !url.includes('/carrinho') && !url.includes('/pedido') && !url.includes('/checkout') && !url.includes('/pedidos') && !url.includes('/conta');
    return url.includes(`/${this.storeSlug}${path}`);
  }

  isStoreHome(): boolean {
    const url = this.router.url;
    const slug = this.storeSlug;
    if (!slug) return false;
    return url === `/${slug}` || url === `/${slug}/`;
  }

  showFooterNav(): boolean {
    return !this.isStoreHome() && !this.router.url.includes('/checkout/');
  }

  navigate(path: string): void {
    if (path === '') {
      this.router.navigate(['/', this.storeSlug]);
    } else {
      this.router.navigate(['/', this.storeSlug, path]);
    }
  }

  ngOnDestroy(): void {
    document.body.classList.remove('store-page-active');
  }
}
