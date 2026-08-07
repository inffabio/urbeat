import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  IonContent,
  IonCard,
  IonCardHeader,
  IonCardTitle,
  IonCardContent,
  IonItem,
  IonInput,
  IonButton,
  IonText,
  IonSpinner,
} from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { LoginRequest } from '../../shared/models/auth.model';

@Component({
  selector: 'app-admin-login-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    IonContent,
    IonCard,
    IonCardHeader,
    IonCardTitle,
    IonCardContent,
    IonItem,
    IonInput,
    IonButton,
    IonText,
    IonSpinner,
  ],
  templateUrl: './admin-login-page.component.html',
  styleUrls: ['./admin-login-page.component.scss'],
})
export class AdminLoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly email = signal('');
  readonly password = signal('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  async onSubmit(): Promise<void> {
    if (!this.email() || !this.password()) {
      this.error.set('Por favor, preencha todos os campos.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    const request: LoginRequest = {
      email: this.email(),
      password: this.password(),
    };

    this.auth.loginAdmin(request).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigateByUrl('/painel/landing-page');
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.message || 'Credenciais inválidas ou acesso negado.');
      },
    });
  }
}
