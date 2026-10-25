import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-driver-shell',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './driver-shell.component.html',
  styleUrl: './driver-shell.component.scss',
})
export class DriverShellComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/entregador/login']);
  }
}
