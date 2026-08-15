import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { IonContent, IonIcon, IonSpinner } from '@ionic/angular/standalone';

@Component({
  selector: 'app-seller-login-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, IonContent, IonIcon, IonSpinner, RouterModule],
  templateUrl: './seller-login-page.component.html',
  styleUrl: './seller-login-page.component.scss',
  host: { '[class.urbeat-onboarding]': 'true' },
})
export class SellerLoginPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly storeService = inject(StoreService);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  readonly loginForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  togglePassword(): void {
    this.showPassword.update((val) => !val);
  }

  onSubmit(): void {
    if (this.loginForm.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set(null);

    const { email, password } = this.loginForm.getRawValue();

    this.auth.loginSeller({ email, password }).subscribe({
      next: () => {
        this.loading.set(false);
        this.redirectAfterLogin();
      },
      error: (err) => {
        this.loading.set(false);
        
        const errorCode = err.error?.code;
        const errorDetail = err.error?.detail || err.error?.error || '';

        // "Se por acaso o vendedor não tiver confirmado o email. Quando ele tentar fazer o login verifque se o email (usuario) existe e envie o email para o usuario pedindo para confirmar. após a confirmação redirecione-o para a primeira tela de cadastro da loja." -> Handled by email-confirm which redirects to /configurar-loja.
        if (errorCode === 'EMAIL_NOT_CONFIRMED' || errorDetail.includes('not confirmed')) {
          // Trigger the resend email function
          this.auth.resendConfirmation({ email }).subscribe({
            next: () => {
               // Redireciona para a tela de confirmação avisando o usuário
               this.router.navigate(['/confirmacao-email'], { queryParams: { email } });
            },
            error: () => {
               // Mesmo se falhar, enviamos o usuário lá pois ele pode tentar reenviar a partir de lá.
               this.router.navigate(['/confirmacao-email'], { queryParams: { email } });
            }
          });
          return;
        }

        // Generic error handling
        const backendError = err?.error?.error || err?.error?.detail || '';
        if (err.status === 401) {
          this.toastService.showError(backendError || 'E-mail ou senha incorretos.');
        } else {
          this.toastService.showError(backendError || 'Não foi possível fazer login. Verifique suas credenciais.');
        }
      },
    });
  }

  /**
   * Após login: se a loja existe e o wizard está completo (publicável),
   * vai para o dashboard. Se está no meio do wizard ou não tem loja,
   * continua no wizard (/configurar-loja).
   */
  private redirectAfterLogin(): void {
    this.storeService.getMyStore().subscribe({
      next: (store) => {
        if (!store?.id) {
          this.router.navigate(['/configurar-loja']);
          return;
        }
        this.storeService.getStorePublishSummary(store.id).subscribe({
          next: (summary) => {
            if (summary?.canPublish) {
              this.router.navigate(['/app/dashboard']);
            } else {
              this.router.navigate(['/configurar-loja']);
            }
          },
          error: () => this.router.navigate(['/configurar-loja']),
        });
      },
      error: () => this.router.navigate(['/configurar-loja']),
    });
  }
}
