import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { SellerShellFacade } from './seller-shell.facade';
import { SellerAppShellComponent } from './seller-app-shell.component';

describe('SellerAppShellComponent', () => {
  let fixture: ComponentFixture<SellerAppShellComponent>;
  let facadeMock: any;

  beforeEach(async () => {
    facadeMock = {
      init: jest.fn().mockResolvedValue(undefined),
      enableSound: jest.fn().mockResolvedValue(undefined),
      disableSound: jest.fn(),
      reset: jest.fn(),
      storeName: jest.fn(() => 'Loja Teste'),
      store: jest.fn(() => ({ isOpen: true })),
      unreadCount: jest.fn(() => 2),
      ordersCount: jest.fn(() => 2),
      loading: jest.fn(() => false),
      soundEnabled: jest.fn(() => false),
      soundNeedsActivation: jest.fn(() => true),
      newOrderPulse: jest.fn(() => null),
      realtimeConnected: jest.fn(() => true),
      printerWarning: jest.fn(() => null),
    };

    await TestBed.configureTestingModule({
      imports: [SellerAppShellComponent],
      providers: [
        provideRouter([]),
        { provide: SellerShellFacade, useValue: facadeMock },
        { provide: AuthService, useValue: { logout: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SellerAppShellComponent);
  });

  it('should initialize the seller shell facade', () => {
    fixture.detectChanges();

    expect(facadeMock.init).toHaveBeenCalled();
  });

  it('should show activate sound action when audio needs activation', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Ativar som de pedidos');
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
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.sound-btn').click();

    expect(facadeMock.enableSound).toHaveBeenCalled();
    expect(facadeMock.disableSound).not.toHaveBeenCalled();
  });

  it('disables sound through the facade when toggled from the enabled state', () => {
    facadeMock.soundEnabled.mockReturnValue(true);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.sound-btn').click();

    expect(facadeMock.disableSound).toHaveBeenCalled();
    expect(facadeMock.enableSound).not.toHaveBeenCalled();
  });

  it('hides the activation banner when audio does not need activation', () => {
    facadeMock.soundNeedsActivation.mockReturnValue(false);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Ativar som de pedidos');
    expect(fixture.nativeElement.querySelector('.activation-banner')).toBeNull();
  });

  it('requests playback unlock when the activation banner is clicked', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.activation-banner')).not.toBeNull();

    fixture.nativeElement.querySelector('.activation-banner').click();

    expect(facadeMock.enableSound).toHaveBeenCalled();
  });
});
