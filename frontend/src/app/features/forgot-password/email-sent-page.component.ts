import { Component, inject, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { IonContent, IonIcon } from '@ionic/angular/standalone';

@Component({
  selector: 'app-email-sent-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, RouterModule],
  template: `
    <ion-content [fullscreen]="true" class="ion-padding">
      <div class="login-shell">
        <div class="login-form-side">
          <div class="form-wrapper" style="text-align:center">
            <ion-icon name="mail-outline" style="font-size:64px;color:var(--c-accent);margin-bottom:16px"></ion-icon>
            <h1 class="login-title">Verifique seu e-mail</h1>
            <p class="login-subtitle">Enviamos um link de recuperação para <strong>{{ email() }}</strong>. Verifique também sua caixa de spam.</p>

            <button class="btn-outline" [disabled]="cooldown() > 0" (click)="resend()" style="margin-bottom:16px">
              {{ cooldown() > 0 ? 'Reenviar em ' + cooldown() + 's' : 'Reenviar e-mail' }}
            </button>

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
    .login-title { font-family: var(--ion-font-family); font-weight: 700; font-size: 26px; color: var(--c-text); margin: 0 0 8px; }
    .login-subtitle { font-size: 14px; color: var(--c-muted); margin: 0 0 24px; line-height: 1.5; }
    .btn-outline { width: 100%; background: var(--app-surface); color: var(--c-accent); border: 2px solid var(--c-accent); border-radius: 999px; padding: 12px; font-size: 14px; font-weight: 600; cursor: pointer; }
    .btn-outline:disabled { opacity: 0.5; cursor: not-allowed; }
    .signup-call { text-align: center; margin-top: 20px; font-size: 13px; color: var(--c-muted); }
    .signup-call a { color: var(--c-accent); text-decoration: none; font-weight: 500; }
    .login-visual-side { flex: 1; background: linear-gradient(135deg, var(--app-brand-soft), var(--app-surface-soft)); position: relative; overflow: hidden; display: flex; align-items: center; justify-content: center; }
    @media (max-width: 768px) { .login-visual-side { display: none; } }
    .blob { position: absolute; border-radius: 50%; opacity: 0.10; }
    .shape-1 { width: 500px; height: 500px; background: var(--c-accent); top: -100px; right: -150px; }
    .shape-2 { width: 350px; height: 350px; background: var(--app-brand-dark); bottom: -80px; left: -80px; }
  `]
})
export class EmailSentPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly email = signal('');
  readonly cooldown = signal(0);
  private interval: any;

  constructor() {
    this.email.set(this.route.snapshot.queryParams['email'] || '');
    effect(() => { if (this.cooldown() > 0) this.startCountdown(); });
  }

  resend() {
    if (this.cooldown() > 0) return;
    this.auth.forgotPassword({ email: this.email() }).subscribe({
      next: () => {
        this.toast.showSuccess('E-mail reenviado com sucesso!');
        this.cooldown.set(60);
      },
      error: () => this.toast.showError('Erro ao reenviar. Tente novamente.')
    });
  }

  private startCountdown() {
    clearInterval(this.interval);
    this.interval = setInterval(() => {
      this.cooldown.update(v => {
        if (v <= 1) { clearInterval(this.interval); return 0; }
        return v - 1;
      });
    }, 1000);
  }
}
