import { CommonModule } from '@angular/common';
import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  cardOutline,
  closeOutline,
  gridOutline,
  logOutOutline,
  menuOutline,
  settingsOutline,
} from 'ionicons/icons';
import { AuthService } from '../../core/services/auth.service';
import { decodeJwt } from '../../core/utils/jwt.helper';

interface AdminNavItem {
  label: string;
  route: string;
  icon: string;
  exact?: boolean;
}

addIcons({
  'card-outline': cardOutline,
  'close-outline': closeOutline,
  'grid-outline': gridOutline,
  'log-out-outline': logOutOutline,
  'menu-outline': menuOutline,
  'settings-outline': settingsOutline,
});

@Component({
  selector: 'app-admin-shell',
  standalone: true,
  imports: [CommonModule, IonIcon, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './admin-shell.component.html',
  styleUrl: './admin-shell.component.scss',
})
export class AdminShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly mobileMenuOpen = signal(false);

  readonly menuItems: AdminNavItem[] = [
    { label: 'Visão geral', route: '/admin/visao-geral', icon: 'grid-outline', exact: true },
    { label: 'Planos', route: '/admin/planos', icon: 'card-outline' },
    { label: 'Configurações', route: '/admin/configuracoes', icon: 'settings-outline' },
  ];

  readonly adminEmail = computed(() => {
    const token = this.auth.getToken();
    if (!token) return '';
    const payload = decodeJwt(token) as { email?: string } | null;
    return payload?.email ?? '';
  });

  toggleMobileMenu(): void {
    this.mobileMenuOpen.update((open) => !open);
  }

  closeMobileMenu(): void {
    this.mobileMenuOpen.set(false);
  }

  logout(): void {
    this.closeMobileMenu();
    this.auth.logout();
    void this.router.navigateByUrl('/painel/login');
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closeMobileMenu();
  }
}
