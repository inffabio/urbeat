import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { isDeliveryDriver } from '../../core/utils/jwt.helper';

@Component({
  selector: 'app-driver-login-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './driver-login-page.component.html',
  styleUrl: './driver-login-page.component.scss',
})
export class DriverLoginPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  readonly loginForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  ngOnInit(): void {
    const token = this.auth.getToken();
    if (token && this.auth.isLoggedIn() && isDeliveryDriver(token)) {
      void this.router.navigateByUrl('/entregador/pedidos');
    }
  }

  togglePassword(): void {
    this.showPassword.update((value) => !value);
  }

  submit(): void {
    if (this.loginForm.invalid || this.loading()) {
      this.loginForm.markAllAsTouched();
      return;
    }

    const { email, password } = this.loginForm.getRawValue();
    this.loading.set(true);
    this.error.set(null);

    this.auth.loginDriver({ email: email.trim(), password }).subscribe({
      next: () => {
        this.loading.set(false);
        void this.router.navigateByUrl('/entregador/pedidos');
      },
      error: (err) => {
        this.loading.set(false);
        const status = err?.status as number | undefined;
        const backendMessage = err?.error?.error ?? err?.error?.detail ?? '';
        if (status === 403) {
          this.error.set('Sua conta não possui acesso ao portal do entregador. Aceite o convite da loja primeiro.');
          return;
        }
        if (status === 423) {
          this.error.set('Conta temporariamente bloqueada. Tente novamente mais tarde.');
          return;
        }
        this.error.set(backendMessage || 'E-mail ou senha incorretos.');
      },
    });
  }
}
