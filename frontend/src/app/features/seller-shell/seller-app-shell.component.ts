import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterModule, RouterOutlet } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  cardOutline,
  chevronBackOutline,
  chevronForwardOutline,
  closeOutline,
  clipboardOutline,
  downloadOutline,
  gridOutline,
  logOutOutline,
  menuOutline,
  logoWhatsapp,
  peopleOutline,
  printOutline,
  restaurantOutline,
  settingsOutline,
  timeOutline,
  carOutline,
  volumeHighOutline,
  volumeMuteOutline,
} from 'ionicons/icons';
import { AuthService } from '../../core/services/auth.service';
import { SellerShellFacade } from './seller-shell.facade';

addIcons({
  'card-outline': cardOutline,
  'chevron-back-outline': chevronBackOutline,
  'chevron-forward-outline': chevronForwardOutline,
  'close-outline': closeOutline,
  'clipboard-outline': clipboardOutline,
  'download-outline': downloadOutline,
  'grid-outline': gridOutline,
  'log-out-outline': logOutOutline,
  'menu-outline': menuOutline,
  'logo-whatsapp': logoWhatsapp,
  'people-outline': peopleOutline,
  'print-outline': printOutline,
  'restaurant-outline': restaurantOutline,
  'settings-outline': settingsOutline,
  'time-outline': timeOutline,
  'car-outline': carOutline,
  'volume-high-outline': volumeHighOutline,
  'volume-mute-outline': volumeMuteOutline,
});

@Component({
  selector: 'app-seller-app-shell',
  standalone: true,
  imports: [
    CommonModule,
    IonIcon,
    RouterModule,
    RouterOutlet,
  ],
  templateUrl: './seller-app-shell.component.html',
  styleUrl: './seller-app-shell.component.scss',
})
export class SellerAppShellComponent implements OnInit {
  readonly facade = inject(SellerShellFacade);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly sidebarCollapsed = signal(false);
  readonly mobileMenuOpen = signal(false);

  readonly menuItems = [
    { label: 'Dashboard', route: '/app/dashboard', icon: 'grid-outline' },
    { label: 'Pedidos', route: '/app/pedidos', icon: 'clipboard-outline', badge: true },
    { label: 'Cardápio', route: '/app/cardapio/categorias', icon: 'restaurant-outline' },
    { label: 'Clientes', route: '/app/clientes', icon: 'people-outline' },
  ];

  readonly systemItems = [
    { label: 'Mensalidade', route: '/app/mensalidade', icon: 'card-outline' },
    { label: 'Instalar', route: '/app/instalar', icon: 'download-outline' },
    { label: 'Configurações', route: '/app/configuracoes/informacoes', icon: 'settings-outline' },
  ];

  ngOnInit(): void {
    void this.facade.init();
  }

  async enableSound(): Promise<void> {
    await this.facade.enableSound();
  }

  toggleSound(): void {
    if (this.facade.soundEnabled()) this.facade.disableSound();
    else void this.facade.enableSound();
  }

  toggleSidebar(): void {
    this.sidebarCollapsed.update((value) => !value);
  }

  openMobileMenu(): void {
    this.mobileMenuOpen.set(true);
  }

  closeMobileMenu(): void {
    this.mobileMenuOpen.set(false);
  }

  isDashboardRoute(): boolean {
    return this.router.url === '/app/dashboard' || this.router.url.startsWith('/app/dashboard?');
  }

  isNavItemActive(item: { route: string }): boolean {
    const currentUrl = this.router.url.split('?')[0].replace(/\/$/, '');
    const route = item.route.replace(/\/$/, '');

    if (route === '/app/dashboard') return currentUrl === route;
    if (route === '/app/cardapio/categorias') return currentUrl.startsWith('/app/cardapio/');
    if (route === '/app/configuracoes/informacoes') return currentUrl.startsWith('/app/configuracoes/');
    return currentUrl === route;
  }

  logout(): void {
    this.closeMobileMenu();
    this.facade.reset();
    this.auth.logout();
    this.router.navigate(['/login-vendedor']);
  }
}
