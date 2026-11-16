import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfigSubnavComponent } from './config-subnav.component';
import { AdminStoreSessionService } from '../../core/services/admin-store-session.service';

describe('ConfigSubnavComponent', () => {
  let fixture: ComponentFixture<ConfigSubnavComponent>;
  const adminSession = {
    session: jest.fn(() => null),
    isAdminMode: jest.fn(() => false),
  };

  beforeEach(async () => {
    adminSession.session.mockReturnValue(null);
    adminSession.isAdminMode.mockReturnValue(false);
    await TestBed.configureTestingModule({
      imports: [ConfigSubnavComponent],
      providers: [
        provideRouter([]),
        { provide: AdminStoreSessionService, useValue: adminSession },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ConfigSubnavComponent);
    fixture.detectChanges();
  });

  it('renders the configuration links as Bootstrap violet navigation pills', () => {
    const nav = fixture.nativeElement.querySelector('nav');
    const links = fixture.nativeElement.querySelectorAll('a');

    expect(nav.classList.contains('nav')).toBe(true);
    expect(nav.classList.contains('nav-pills')).toBe(true);
    expect(fixture.nativeElement.querySelectorAll('.nav-item')).toHaveLength(5);
    expect(fixture.nativeElement.querySelectorAll('.nav-link')).toHaveLength(5);
    expect(links[0].getAttribute('routerlink')).toBe('/app/configuracoes/horarios');
  });

  it('hides the printing tab in delegated administrator mode', () => {
    adminSession.isAdminMode.mockReturnValue(true);
    adminSession.session.mockReturnValue({
      token: 'opaque-token',
      contextId: 'context-1',
      storeId: 'store-1',
      storeName: 'Loja',
      storeSlug: 'loja',
      adminUserId: 'admin-1',
      expiresAtUtc: '2026-11-16T12:30:00Z',
      returnUrl: '/admin/lojas',
    });
    fixture = TestBed.createComponent(ConfigSubnavComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('.nav-item')).toHaveLength(4);
    expect(fixture.nativeElement.textContent).not.toContain('Impressão');
  });
});
