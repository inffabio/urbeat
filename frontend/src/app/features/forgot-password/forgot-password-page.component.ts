import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { IonContent, IonIcon, IonSpinner } from '@ionic/angular/standalone';

@Component({
  selector: 'app-forgot-password-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, IonContent, IonIcon, IonSpinner, RouterModule],
  template: `
    <ion-content [fullscreen]="true" class="ion-padding">
      <div class="login-shell">
        <div class="login-form-side">
          <div class="form-wrapper">
            <img src="/images/logo_v2.png" alt="Urbeat" class="login-logo" width="150" />
            <h1 class="login-title">Recuperar senha</h1>
            <p class="login-subtitle">Informe o e-mail cadastrado na sua conta. Enviaremos um link para você criar uma nova senha.</p>

            <form [formGroup]="form" (ngSubmit)="submit()" class="login-form">
              <div class="input-group">
                <label for="email" class="input-label">E-mail</label>
                <div class="input-box">
                  <ion-icon name="mail-outline" class="input-icon"></ion-icon>
                  <input id="email" type="email" formControlName="email" placeholder="seu@email.com" autocomplete="email" />
                </div>
                @if (form.get('email')?.touched && form.get('email')?.invalid) {
                  <span class="field-error">Informe um e-mail válido.</span>
                }
              </div>

              <button type="submit" class="btn-submit" [disabled]="form.invalid || loading()">
                @if (loading()) {
                  <ion-spinner name="crescent" style="width:18px;height:18px;margin-right:8px"></ion-spinner>
                  Enviando...
                } @else {
                  Enviar link de recuperação
                }
              </button>
            </form>

            <p class="signup-call"><a routerLink="/login-vendedor">← Voltar para o login</a></p>
          </div>
        </div>
        <div class="login-visual-side">
          <div class="blob shape-1"></div>
          <div class="blob shape-2"></div>
        </div>
      </div>
    </ion-content>
  `,
  styles: [`
    :host { --login-bg: var(--app-bg-warm); --login-card: var(--app-surface); --c-text: var(--app-text-primary); --c-muted: var(--app-text-secondary); --c-accent: var(--app-brand); --c-border: var(--app-line); }
    .login-shell { display: flex; min-height: 100vh; }
    .login-form-side { flex: 1; display: flex; align-items: center; justify-content: center; padding: 40px 24px; background: var(--login-bg); }
    .form-wrapper { width: 100%; max-width: 420px; }
    .login-logo { display: block; margin: 0 auto 16px; }
    .login-title { font-family: var(--ion-font-family); font-weight: 700; font-size: 26px; color: var(--c-text); margin: 0 0 8px; text-align: center; }
    .login-subtitle { font-size: 14px; color: var(--c-muted); text-align: center; margin: 0 0 28px; line-height: 1.5; }
    .login-form { display: flex; flex-direction: column; gap: 16px; }
    .input-group { display: flex; flex-direction: column; gap: 4px; }
    .input-label { font-size: 13px; font-weight: 500; color: var(--c-text); }
    .input-box { display: flex; align-items: center; gap: 8px; background: var(--app-surface); border: 1px solid var(--c-border); border-radius: var(--radius-lg); padding: 12px 14px; }
    .input-icon { font-size: 18px; color: var(--c-muted); flex-shrink: 0; }
    .input-box input { flex: 1; border: none; outline: none; font-size: 14px; background: transparent; color: var(--c-text); }
    .field-error { font-size: 12px; color: var(--app-brand); }
    .btn-submit { width: 100%; background: var(--c-accent); color: #fff; border: none; border-radius: 999px; padding: 14px; font-size: 15px; font-weight: 600; cursor: pointer; display: flex; align-items: center; justify-content: center; }
    .btn-submit:disabled { opacity: 0.5; cursor: not-allowed; }
    .signup-call { text-align: center; margin-top: 20px; font-size: 13px; color: var(--c-muted); }
    .signup-call a { color: var(--c-accent); text-decoration: none; font-weight: 500; }
    .login-visual-side { flex: 1; background: linear-gradient(135deg, var(--app-brand-soft), var(--app-surface-soft)); position: relative; overflow: hidden; display: flex; align-items: center; justify-content: center; }
    @media (max-width: 768px) { .login-visual-side { display: none; } }
    .blob { position: absolute; border-radius: 50%; opacity: 0.10; }
    .shape-1 { width: 500px; height: 500px; background: var(--c-accent); top: -100px; right: -150px; }
    .shape-2 { width: 350px; height: 350px; background: var(--app-brand-dark); bottom: -80px; left: -80px; }
  `]
})
export class ForgotPasswordPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({ email: ['', [Validators.required, Validators.email]] });
  readonly loading = signal(false);

  submit() {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.auth.forgotPassword({ email: this.form.getRawValue().email }).subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.found) {
          const email = this.form.getRawValue().email;
          this.router.navigate(['/recuperar-senha/email-enviado'], { queryParams: { email } });
        } else {
          this.toast.showWarning('E-mail não encontrado em nossa base de dados.');
        }
      },
      error: () => {
        this.loading.set(false);
        this.toast.showError('Erro ao enviar o e-mail. Tente novamente.');
      }
    });
  }
}
