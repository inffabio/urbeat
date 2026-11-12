import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router, Routes } from '@angular/router';
import { readFileSync } from 'fs';
import { resolve } from 'path';
import { AuthService } from '../../core/services/auth.service';
import { routes } from '../../app.routes';
import { SellerShellFacade } from './seller-shell.facade';
import { SellerAppShellComponent } from './seller-app-shell.component';

@Component({ standalone: true, template: '' })
class StubRouteComponent {}

function stubLazyRoutes(routeList: Routes): Routes {
  return routeList.map((route) => {
    const stubbed: Routes[number] = { ...route };
    if (stubbed.loadComponent) {
      stubbed.component = StubRouteComponent;
      delete stubbed.loadComponent;
    }
    if (stubbed.children) {
      stubbed.children = stubLazyRoutes(stubbed.children);
    }
    return stubbed;
  });
}

describe('SellerAppShellComponent', () => {
  let fixture: ComponentFixture<SellerAppShellComponent>;
  let facadeMock: any;
  let authMock: { logout: jest.Mock; getToken: jest.Mock; isLoggedIn: jest.Mock };

  beforeEach(async () => {
    facadeMock = {
      init: jest.fn().mockResolvedValue(undefined),
      enableSound: jest.fn().mockResolvedValue(undefined),
      disableSound: jest.fn(),
      reset: jest.fn(),
      storeName: jest.fn(() => 'Loja Teste'),
      store: jest.fn(() => ({ isOpen: true, slug: 'loja-teste' })),
      unreadCount: jest.fn(() => 2),
      ordersCount: jest.fn(() => 2),
      loading: jest.fn(() => false),
      soundEnabled: jest.fn(() => false),
      soundNeedsActivation: jest.fn(() => true),
      newOrderPulse: jest.fn(() => null),
      realtimeConnected: jest.fn(() => true),
      printerWarning: jest.fn(() => null),
    };
    // Seller JWT payload used by authGuard while navigating in the tests.
    const sellerToken = `header.${btoa(JSON.stringify({ role: 'Seller' }))}.signature`;
    authMock = {
      logout: jest.fn(),
      getToken: jest.fn(() => sellerToken),
      isLoggedIn: jest.fn(() => true),
    };

    await TestBed.configureTestingModule({
      imports: [SellerAppShellComponent],
      providers: [
        provideRouter(stubLazyRoutes(routes)),
        { provide: SellerShellFacade, useValue: facadeMock },
        { provide: AuthService, useValue: authMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SellerAppShellComponent);
  });

  it('should initialize the seller shell facade', () => {
    fixture.detectChanges();

    expect(facadeMock.init).toHaveBeenCalled();
  });

  it('keeps the sound control in the sidebar instead of a dashboard shortcut', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.activation-banner')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Ativar som de pedidos');
    expect(fixture.nativeElement.querySelector('.sound-btn')).not.toBeNull();
  });

  it('should render the documented dashboard navigation entries', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Dashboard');
    expect(fixture.nativeElement.textContent).toContain('Pedidos');
    expect(fixture.nativeElement.textContent).toContain('Cardápio');
    expect(fixture.nativeElement.textContent).toContain('Clientes');
    expect(fixture.nativeElement.textContent).toContain('Mensalidade');
    expect(fixture.nativeElement.textContent).toContain('Instalar');
    expect(fixture.nativeElement.textContent).toContain('Configurações');
  });

  it('keeps the parent navigation item active inside dashboard sections', () => {
    fixture.detectChanges();

    expect(fixture.componentInstance.isNavItemActive({ label: 'Cardápio', route: '/app/cardapio/categorias' })).toBe(false);
    Object.defineProperty(TestBed.inject(Router), 'url', { configurable: true, get: () => '/app/cardapio/produtos' });

    expect(fixture.componentInstance.isNavItemActive({ label: 'Cardápio', route: '/app/cardapio/categorias' })).toBe(true);
  });

  it('should render the documented support and mobile navigation affordances', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Precisa de ajuda?');
    expect(fixture.nativeElement.textContent).toContain('Fale com nosso suporte');
    expect(fixture.nativeElement.textContent).toContain('Painel do Restaurante');
    expect(fixture.nativeElement.querySelector('.mobile-menu-btn')).not.toBeNull();
  });

  it('should reset seller shell state on logout', () => {
    fixture.detectChanges();

    fixture.componentInstance.logout();

    expect(facadeMock.reset).toHaveBeenCalled();
  });

  it('resets the seller shell facade when the shell is destroyed (session expiry)', () => {
    fixture.detectChanges();

    fixture.destroy();

    expect(facadeMock.reset).toHaveBeenCalled();
  });

  it('should show realtime fallback when seller notifications are disconnected', () => {
    facadeMock.realtimeConnected.mockReturnValue(false);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Conexao em tempo real instavel');
    expect(fixture.nativeElement.textContent).toContain('Use Atualizar se um pedido nao aparecer automaticamente.');
  });

  it('should show subscription blocked banner when backend marks the store as blocked', () => {
    facadeMock.store.mockReturnValue({ isOpen: false, isSubscriptionBlocked: true });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Mensalidade bloqueada');
    expect(fixture.nativeElement.textContent).toContain('Regularizar mensalidade');
  });

  it('renders a scrollable sidebar-nav in both desktop and mobile sidebars, separate from seller-main', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.desktop-sidebar .sidebar-nav')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.mobile-sidebar .sidebar-nav')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.seller-main')).not.toBeNull();
  });

  it('uses the current schedule status instead of the manual store flag', () => {
    facadeMock.store.mockReturnValue({ isOpen: true, isOpenNow: false });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.store-card strong')?.textContent).toContain('Loja fechada');
    expect(fixture.nativeElement.querySelector('.store-card')?.classList.contains('open')).toBe(false);
  });

  it('shows the sound toggle as disabled with the muted icon and Som desligado', () => {
    fixture.detectChanges();

    const toggle = fixture.nativeElement.querySelector('.sound-btn');
    expect(toggle.textContent).toContain('Som desligado');
    expect(toggle.querySelector('ion-icon').getAttribute('name')).toBe('volume-mute-outline');
  });

  it('shows the sound toggle as enabled with the high-volume icon and Som ligado', () => {
    facadeMock.soundEnabled.mockReturnValue(true);
    facadeMock.soundNeedsActivation.mockReturnValue(false);
    fixture.detectChanges();

    const toggle = fixture.nativeElement.querySelector('.sound-btn');
    expect(toggle.textContent).toContain('Som ligado');
    expect(toggle.querySelector('ion-icon').getAttribute('name')).toBe('volume-high-outline');
  });

  it('enables sound through the facade when toggled from the disabled state', () => {
    facadeMock.soundEnabled.mockReturnValue(false);
    facadeMock.soundNeedsActivation.mockReturnValue(false);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.sound-btn').click();

    expect(facadeMock.enableSound).toHaveBeenCalled();
    expect(facadeMock.disableSound).not.toHaveBeenCalled();
  });

  it('disables sound through the facade when toggled from the enabled state', () => {
    facadeMock.soundEnabled.mockReturnValue(true);
    facadeMock.soundNeedsActivation.mockReturnValue(false);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.sound-btn').click();

    expect(facadeMock.disableSound).toHaveBeenCalled();
    expect(facadeMock.enableSound).not.toHaveBeenCalled();
  });

  describe('sidebar sound toggle', () => {
    it('activates audio from the sidebar when sound is enabled but needs browser activation', async () => {
      facadeMock.soundEnabled.mockReturnValue(true);
      facadeMock.soundNeedsActivation.mockReturnValue(true);

      fixture.componentInstance.toggleSound();
      await Promise.resolve();

      expect(facadeMock.enableSound).toHaveBeenCalled();
      expect(facadeMock.disableSound).not.toHaveBeenCalled();
    });

    it('disables an already activated sound from the sidebar', () => {
      facadeMock.soundEnabled.mockReturnValue(true);
      facadeMock.soundNeedsActivation.mockReturnValue(false);

      fixture.componentInstance.toggleSound();

      expect(facadeMock.disableSound).toHaveBeenCalled();
      expect(facadeMock.enableSound).not.toHaveBeenCalled();
    });

    it('enables sound from the sidebar when it is disabled', () => {
      facadeMock.soundEnabled.mockReturnValue(false);
      facadeMock.soundNeedsActivation.mockReturnValue(false);

      fixture.componentInstance.toggleSound();

      expect(facadeMock.enableSound).toHaveBeenCalled();
      expect(facadeMock.disableSound).not.toHaveBeenCalled();
    });

    it('unlocks audio from the mobile sidebar on the first tap without muting', async () => {
      facadeMock.soundEnabled.mockReturnValue(true);
      facadeMock.soundNeedsActivation.mockReturnValue(true);
      fixture.detectChanges();

      fixture.nativeElement.querySelector('.mobile-menu-btn').click();
      fixture.detectChanges();

      const mobileToggle = fixture.nativeElement.querySelector('.mobile-sidebar .sound-btn') as HTMLButtonElement;
      expect(mobileToggle).not.toBeNull();
      expect(mobileToggle.textContent).toContain('Som ligado');

      mobileToggle.click();
      await Promise.resolve();

      expect(facadeMock.enableSound).toHaveBeenCalled();
      expect(facadeMock.disableSound).not.toHaveBeenCalled();
    });
  });

  describe('collapsed sidebar sound toggle accessible name', () => {
    function collapsedSoundButton(): HTMLButtonElement {
      fixture.componentInstance.sidebarCollapsed.set(true);
      fixture.detectChanges();

      return fixture.nativeElement.querySelector('.desktop-sidebar .sound-btn') as HTMLButtonElement;
    }

    it('announces activation while the sound is off', () => {
      facadeMock.soundEnabled.mockReturnValue(false);
      facadeMock.soundNeedsActivation.mockReturnValue(false);

      const toggle = collapsedSoundButton();

      expect(toggle.textContent).toContain('Som desligado');
      expect(toggle.getAttribute('aria-label')).toBe('Ativar som de pedidos');
      expect(toggle.getAttribute('title')).toBe('Ativar som de pedidos');
    });

    it('announces the pending browser unlock while sound is enabled but not activated', () => {
      facadeMock.soundEnabled.mockReturnValue(true);
      facadeMock.soundNeedsActivation.mockReturnValue(true);

      const toggle = collapsedSoundButton();

      expect(toggle.getAttribute('aria-label')).toBe('Liberar som de pedidos');
      expect(toggle.getAttribute('title')).toBe('Liberar som de pedidos');
    });

    it('announces muting once the sound is enabled and already unlocked', () => {
      facadeMock.soundEnabled.mockReturnValue(true);
      facadeMock.soundNeedsActivation.mockReturnValue(false);

      const toggle = collapsedSoundButton();

      expect(toggle.getAttribute('aria-label')).toBe('Silenciar som de pedidos');
      expect(toggle.getAttribute('title')).toBe('Silenciar som de pedidos');
    });

    it('keeps relying on the visible copy while the sidebar is expanded', () => {
      facadeMock.soundEnabled.mockReturnValue(true);
      facadeMock.soundNeedsActivation.mockReturnValue(false);
      fixture.detectChanges();

      const toggle = fixture.nativeElement.querySelector('.desktop-sidebar .sound-btn') as HTMLButtonElement;

      expect(toggle.textContent).toContain('Som ligado');
      expect(toggle.getAttribute('aria-label')).toBeNull();
      expect(toggle.getAttribute('title')).toBe('');
    });

    it('keeps the accessible name in sync when the collapsed state changes', () => {
      facadeMock.soundEnabled.mockReturnValue(false);
      facadeMock.soundNeedsActivation.mockReturnValue(false);
      fixture.detectChanges();

      const ariaLabel = () =>
        (fixture.nativeElement.querySelector('.desktop-sidebar .sound-btn') as HTMLButtonElement)
          .getAttribute('aria-label');

      expect(ariaLabel()).toBeNull();

      fixture.componentInstance.sidebarCollapsed.set(true);
      fixture.detectChanges();
      expect(ariaLabel()).toBe('Ativar som de pedidos');

      facadeMock.soundEnabled.mockReturnValue(true);
      fixture.detectChanges();
      expect(ariaLabel()).toBe('Silenciar som de pedidos');

      fixture.componentInstance.sidebarCollapsed.set(false);
      fixture.detectChanges();
      expect(ariaLabel()).toBeNull();
    });
  });

  it('declares the sound controls in the sidebar and the detached window only', () => {
    const template = readFileSync(resolve(__dirname, './seller-app-shell.component.html'), 'utf8');

    expect(template).not.toContain('class="activation-banner"');
    expect(template).toContain('class="mini-btn sound-btn"');
    expect(template).toContain('class="detached-sound-bar"');
  });

  it('links every dashboard logo variant to the storefront in a new tab', () => {
    fixture.detectChanges();

    const links = Array.from(fixture.nativeElement.querySelectorAll('a.storefront-logo-link')) as HTMLAnchorElement[];

    expect(links).toHaveLength(3);
    for (const link of links) {
      expect(link.getAttribute('href')).toBe('/loja-teste');
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
      expect(link.getAttribute('title')).toBe('Clique para ir para loja');
      expect(link.getAttribute('aria-label')).toBe('Clique para ir para loja');
    }
  });

  it('generates the storefront href through the router and resolves it to the public :storePath route', async () => {
    const router = TestBed.inject(Router);
    const serializeSpy = jest.spyOn(router, 'serializeUrl');

    fixture.detectChanges();

    expect(serializeSpy).toHaveBeenCalled();
    const url = fixture.componentInstance.storefrontUrl();
    expect(url).toBe('/loja-teste');

    await router.navigateByUrl(url!);

    let storePath: string | null = null;
    let route = router.routerState.root;
    while (route.firstChild) {
      route = route.firstChild;
      if (route.routeConfig?.path === ':storePath') {
        storePath = route.snapshot.paramMap.get('storePath');
      }
    }

    expect(storePath).toBe('loja-teste');
  });

  it('omits the storefront href on every logo variant when the store slug is unavailable', () => {
    facadeMock.store.mockReturnValue(null);
    fixture.detectChanges();

    const links = Array.from(fixture.nativeElement.querySelectorAll('a.storefront-logo-link')) as HTMLAnchorElement[];

    expect(links).toHaveLength(3);
    for (const link of links) {
      expect(link.hasAttribute('href')).toBe(false);
      expect(link.getAttribute('href')).toBeNull();
    }
  });

  it('keeps the logo fallback when the store has no logo', () => {
    fixture.detectChanges();

    const marks = fixture.nativeElement.querySelectorAll('.brand-mark, .mobile-brand-mark');

    expect(marks).toHaveLength(3);
    expect(Array.from(marks).every((mark: Element) => mark.textContent?.trim() === 'H')).toBe(true);
  });

  describe('detached orders route', () => {
    it('keeps the dashboard chrome on regular seller routes', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/app/pedidos');
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.seller-shell').classList.contains('is-detached-orders')).toBe(false);
      expect(fixture.nativeElement.querySelector('.desktop-sidebar')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.mobile-topbar')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
    });

    it('renders only the routed orders page and its own header on /app/pedidos-separados', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/app/pedidos-separados');
      fixture.detectChanges();

      const shell = fixture.nativeElement.querySelector('.seller-shell');
      expect(shell).not.toBeNull();
      expect(shell.classList.contains('is-detached-orders')).toBe(true);
      expect(fixture.nativeElement.querySelector('.desktop-sidebar')).toBeNull();
      expect(fixture.nativeElement.querySelector('.mobile-sidebar')).toBeNull();
      expect(fixture.nativeElement.querySelector('.mobile-sidebar-backdrop')).toBeNull();
      expect(fixture.nativeElement.querySelector('.mobile-topbar')).toBeNull();
      expect(fixture.nativeElement.querySelector('.activation-banner')).toBeNull();
      expect(fixture.nativeElement.querySelector('.detached-sound-bar')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.subscription-banner')).toBeNull();
      expect(fixture.nativeElement.querySelector('.realtime-fallback')).toBeNull();
      // The sr-only live region is not visual chrome and keeps announcing new orders from SignalR.
      expect(fixture.nativeElement.querySelector('.new-order-live')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
      expect(authMock.logout).not.toHaveBeenCalled();
    });

    it('offers accessible activation and mute controls inside the detached window', async () => {
      facadeMock.soundEnabled.mockReturnValue(true);
      facadeMock.soundNeedsActivation.mockReturnValue(true);
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/app/pedidos-separados');
      fixture.detectChanges();

      const bar = fixture.nativeElement.querySelector('.detached-sound-bar') as HTMLElement | null;
      expect(bar).not.toBeNull();
      expect(bar!.getAttribute('role')).toBe('group');
      expect(bar!.getAttribute('aria-label')).toBe('Som de novos pedidos');

      const labels = Array.from(bar!.querySelectorAll('button'))
        .map((button) => button.textContent?.trim() ?? '');
      // Each window is its own document: audio still needs a gesture here.
      expect(labels).toContain('Ativar som de pedidos');
      expect(labels).toContain('Silenciar');

      const activate = Array.from(bar!.querySelectorAll('button'))
        .find((button) => button.textContent?.includes('Ativar som'))!;
      activate.click();
      expect(facadeMock.enableSound).toHaveBeenCalledTimes(1);

      const mute = Array.from(bar!.querySelectorAll('button'))
        .find((button) => button.textContent?.includes('Silenciar'))!;
      mute.click();
      expect(facadeMock.disableSound).toHaveBeenCalledTimes(1);
      // The dashboard has no activation banner at all, including here.
      expect(fixture.nativeElement.querySelector('.activation-banner')).toBeNull();
    });

    it('offers turning the sound back on when the detached window starts muted', async () => {
      facadeMock.soundEnabled.mockReturnValue(false);
      facadeMock.soundNeedsActivation.mockReturnValue(false);
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/app/pedidos-separados');
      fixture.detectChanges();

      const bar = fixture.nativeElement.querySelector('.detached-sound-bar') as HTMLElement;
      const buttons = Array.from(bar.querySelectorAll('button'));
      expect(buttons.map((button) => button.textContent?.trim())).toEqual(['Ativar som']);

      buttons[0].click();
      expect(facadeMock.enableSound).toHaveBeenCalledTimes(1);
      expect(facadeMock.disableSound).not.toHaveBeenCalled();
    });

    it('does not duplicate the sound controls on regular seller routes', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/app/pedidos');
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.detached-sound-bar')).toBeNull();
      expect(fixture.nativeElement.querySelector('.activation-banner')).toBeNull();
      expect(fixture.nativeElement.querySelector('.sound-btn')).not.toBeNull();
    });

    it('restores the dashboard chrome after leaving the detached route', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/app/pedidos-separados');
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.desktop-sidebar')).toBeNull();

      await router.navigateByUrl('/app/dashboard');
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.seller-shell').classList.contains('is-detached-orders')).toBe(false);
      expect(fixture.nativeElement.querySelector('.desktop-sidebar')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.mobile-topbar')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
    });
  });
});
