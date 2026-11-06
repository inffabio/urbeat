import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { IonContent, IonIcon, IonSpinner } from '@ionic/angular/standalone';
import { AccountRole } from '../../shared/models/auth.model';

@Component({
  selector: 'app-forgot-password-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, IonContent, IonIcon, IonSpinner, RouterModule],
  template: `
    <ion-content [fullscreen]="true" class="ion-padding">
      <div class="login-shell">
        <div class="login-form-side">
          <div class="form-wrapper">
            <img src="/images/logo_urbeat.png" alt="urbeat" class="login-logo" width="150" />
            <h1 class="login-title">Recuperar senha</h1>
            <p class="login-subtitle">Escolha qual acesso deseja recuperar. Cliente e lojista têm senhas independentes.</p>

            <div class="role-switch" role="group" aria-label="Tipo de acesso">
              <button type="button" class="role-option" [class.selected]="role() === 'Customer'"
                [attr.aria-pressed]="role() === 'Customer'" (click)="role.set('Customer')">
                <ion-icon name="person-outline"></ion-icon>
                Sou cliente
              </button>
              <button type="button" class="role-option" [class.selected]="role() === 'Seller'"
                [attr.aria-pressed]="role() === 'Seller'" (click)="role.set('Seller')">
                <ion-icon name="storefront-outline"></ion-icon>
                Sou lojista
              </button>
            </div>

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
    .role-switch { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 20px; }
    .role-option { min-height: 44px; display: inline-flex; align-items: center; justify-content: center; gap: 6px; border: 1px solid var(--c-border); border-radius: var(--radius-lg); background: var(--app-surface); color: var(--c-text); font-size: 14px; font-weight: 500; cursor: pointer; }
    .role-option.selected { border-color: var(--c-accent); color: var(--c-accent); background: var(--app-brand-soft); }
    .role-option ion-icon { font-size: 18px; }
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
  private readonly route = inject(ActivatedRoute);

  readonly form = this.fb.nonNullable.group({ email: ['', [Validators.required, Validators.email]] });
  readonly loading = signal(false);
  readonly role = signal<AccountRole>(this.readInitialRole());

  submit() {
    if (this.form.invalid || this.loading()) return;
    const role = this.role();
    this.loading.set(true);
    this.auth.forgotPassword({ email: this.form.getRawValue().email, role }).subscribe({
      next: () => {
        this.loading.set(false);
        // The backend returns a uniform response regardless of whether the account exists, so the
        // user is always sent to the "check your e-mail" step to avoid account enumeration.
        const email = this.form.getRawValue().email;
        this.router.navigate(['/recuperar-senha/email-enviado'], { queryParams: { email, role } });
      },
      error: () => {
        this.loading.set(false);
        this.toast.showError('Erro ao enviar o e-mail. Tente novamente.');
      }
    });
  }

  private readInitialRole(): AccountRole {
    return this.route.snapshot.queryParamMap.get('role') === 'Customer' ? 'Customer' : 'Seller';
  }
}
