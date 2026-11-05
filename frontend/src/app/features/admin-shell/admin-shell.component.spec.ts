import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router, Routes } from '@angular/router';
import { routes } from '../../app.routes';
import { AuthService } from '../../core/services/auth.service';
import { AdminShellComponent } from './admin-shell.component';

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

describe('AdminShellComponent', () => {
  let fixture: ComponentFixture<AdminShellComponent>;
  let authMock: { getToken: jest.Mock; logout: jest.Mock };
  let router: Router;

  beforeEach(async () => {
    const adminToken = `header.${btoa(
      JSON.stringify({ role: 'Admin', email: 'admin@urbeat.local' }),
    )}.signature`;
    authMock = {
      getToken: jest.fn(() => adminToken),
      logout: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [AdminShellComponent],
      providers: [
        provideRouter(stubLazyRoutes(routes)),
        { provide: AuthService, useValue: authMock },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(AdminShellComponent);
    fixture.detectChanges();
  });

  it('renders the real urbeat logo at the top of the sidebar', () => {
    const images = Array.from(
      fixture.nativeElement.querySelectorAll('img'),
    ) as HTMLImageElement[];

    expect(images.length).toBeGreaterThan(0);
    expect(images.every((image) => image.getAttribute('src') === '/images/logo_urbeat.png')).toBe(
      true,
    );
    expect(images.every((image) => image.getAttribute('alt') === 'urbeat')).toBe(true);
  });

  it('renders the exclusive administrative navigation', () => {
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Visão geral');
    expect(text).toContain('Planos');
    expect(text).toContain('Configurações');
    expect(text).toContain('Sair');
    expect(text).not.toContain('Pedidos');
    expect(text).not.toContain('Cardápio');
  });

  it('shows the signed-in administrator identity', () => {
    expect(fixture.nativeElement.textContent).toContain('admin@urbeat.local');
  });

  it('toggles the accessible mobile menu', () => {
    const menuButton = fixture.nativeElement.querySelector(
      '[aria-label="Abrir menu administrativo"]',
    ) as HTMLButtonElement;

    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
    menuButton.click();
    fixture.detectChanges();

    expect(menuButton.getAttribute('aria-expanded')).toBe('true');
    expect(
      fixture.nativeElement
        .querySelector('.admin-mobile-sidebar')
        .classList.contains('is-open'),
    ).toBe(true);
  });

  it('logs out and returns to the admin login', () => {
    const navigate = jest.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    fixture.componentInstance.logout();

    expect(authMock.logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/painel/login');
  });
});
