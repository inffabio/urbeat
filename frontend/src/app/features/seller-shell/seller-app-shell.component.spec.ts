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
});
