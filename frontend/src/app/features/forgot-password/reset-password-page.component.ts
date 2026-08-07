import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { IonContent, IonIcon, IonSpinner } from '@ionic/angular/standalone';

@Component({
  selector: 'app-reset-password-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, IonContent, IonIcon, IonSpinner, RouterModule],
  template: `
    <ion-content [fullscreen]="true" class="ion-padding">
      <div class="login-shell">
        <div class="login-form-side">
          <div class="form-wrapper">
            @if (tokenValid() === null) {
              <div style="text-align:center;padding:40px"><ion-spinner name="crescent"></ion-spinner></div>
            } @else if (tokenValid() === false) {
              <ion-icon name="warning-outline" style="font-size:64px;color:var(--c-accent);display:block;margin:0 auto 16px"></ion-icon>
              <h1 class="login-title">Link inválido ou expirado</h1>
              <p class="login-subtitle">Este link de recuperação não é mais válido. Solicite um novo link.</p>
              <button class="btn-submit" routerLink="/recuperar-senha" style="margin-bottom:12px">Solicitar novo link</button>
              <p class="signup-call"><a routerLink="/login-vendedor">← Voltar para o login</a></p>
            } @else {
              <img src="/images/logo_v2.png" alt="Urbeat" class="login-logo" width="150" />
              <h1 class="login-title">Criar nova senha</h1>
              <p class="login-subtitle">Digite sua nova senha nos campos abaixo.</p>
              <form [formGroup]="form" (ngSubmit)="submit()" class="login-form">
                <div class="input-group">
                  <label class="input-label">Nova senha</label>
                  <div class="input-box">
                    <ion-icon name="lock-closed-outline" class="input-icon"></ion-icon>
                    <input [type]="showPwd() ? 'text' : 'password'" formControlName="newPassword" placeholder="Mínimo 8 caracteres" />
                    <ion-icon [name]="showPwd() ? 'eye-off-outline' : 'eye-outline'" class="input-icon-eye" (click)="showPwd.set(!showPwd())"></ion-icon>
                  </div>
                  @if (form.get('newPassword')?.touched) {
                    <div class="strength-bar"><div [style.width]="strengthPercent()" [style.background]="strengthColor()" style="height:4px;border-radius:2px;transition:all 0.3s"></div></div>
                    <span style="font-size:11px;color:var(--c-muted)">{{ strengthLabel() }}</span>
                  }
                </div>
                <div class="input-group">
                  <label class="input-label">Confirmar nova senha</label>
                  <div class="input-box">
                    <ion-icon name="lock-closed-outline" class="input-icon"></ion-icon>
                    <input [type]="showPwd() ? 'text' : 'password'" formControlName="confirmPassword" placeholder="Repita a senha" />
                  </div>
                  @if (form.hasError('mismatch') && form.get('confirmPassword')?.touched) {
                    <span class="field-error">As senhas não coincidem.</span>
                  }
                </div>
                <button type="submit" class="btn-submit" [disabled]="form.invalid || loading()">
                  @if (loading()) { <ion-spinner name="crescent" style="width:18px;height:18px;margin-right:8px"></ion-spinner> Salvando... } @else { Salvar nova senha }
                </button>
              </form>
            }
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
    :host { --login-bg: var(--app-bg-warm); --c-text: var(--app-text-primary); --c-muted: var(--app-text-secondary); --c-accent: var(--app-brand); --c-border: var(--app-line); }
    .login-shell { display: flex; min-height: 100vh; }
    .login-form-side { flex: 1; display: flex; align-items: center; justify-content: center; padding: 40px 24px; background: var(--login-bg); }
    .form-wrapper { width: 100%; max-width: 420px; }
    .login-logo { display: block; margin: 0 auto 16px; }
    .login-title { font-family: var(--ion-font-family); font-weight: 700; font-size: 26px; color: var(--c-text); margin: 0 0 8px; text-align: center; }
    .login-subtitle { font-size: 14px; color: var(--c-muted); text-align: center; margin: 0 0 24px; line-height: 1.5; }
    .login-form { display: flex; flex-direction: column; gap: 16px; }
    .input-group { display: flex; flex-direction: column; gap: 4px; }
    .input-label { font-size: 13px; font-weight: 500; color: var(--c-text); }
    .input-box { display: flex; align-items: center; gap: 8px; background: var(--app-surface); border: 1px solid var(--c-border); border-radius: var(--radius-lg); padding: 12px 14px; }
    .input-icon { font-size: 18px; color: var(--c-muted); flex-shrink: 0; }
    .input-icon-eye { font-size: 18px; color: var(--c-muted); cursor: pointer; flex-shrink: 0; }
    .input-box input { flex: 1; border: none; outline: none; font-size: 14px; background: transparent; color: var(--c-text); }
    .field-error { font-size: 12px; color: var(--app-brand); }
    .strength-bar { margin-top: 4px; background: var(--app-line); border-radius: 2px; height: 4px; }
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
export class ResetPasswordPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly token = signal('');
  readonly tokenValid = signal<boolean | null>(null);
  readonly loading = signal(false);
  readonly showPwd = signal(false);

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8), Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*]).{8,}$/)]],
    confirmPassword: ['', Validators.required]
  }, { validators: this.passwordsMatch });

  private passwordsMatch(g: any) {
    return g.get('newPassword')?.value === g.get('confirmPassword')?.value ? null : { mismatch: true };
  }

  readonly strengthScore = computed(() => {
    const p = this.form.get('newPassword')?.value || '';
    let s = 0;
    if (p.length >= 8) s++;
    if (/[a-z]/.test(p)) s++;
    if (/[A-Z]/.test(p)) s++;
    if (/\d/.test(p)) s++;
    if (/[!@#$%^&*]/.test(p)) s++;
    return s;
  });
  readonly strengthPercent = computed(() => (this.strengthScore() / 5) * 100 + '%');
  readonly strengthColor = computed(() => {
    const s = this.strengthScore();
    if (s <= 2) return 'var(--app-brand)'; if (s <= 3) return '#f59e0b'; return 'var(--app-success-green)';
  });
  readonly strengthLabel = computed(() => {
    const s = this.strengthScore();
    if (s <= 2) return 'Fraca'; if (s <= 3) return 'Média'; return 'Forte';
  });

  constructor() {
    const t = this.route.snapshot.queryParams['token'];
    if (!t) { this.tokenValid.set(false); return; }
    this.token.set(t);
    this.auth.validateResetToken(t).subscribe({
      next: (res) => this.tokenValid.set(res.valid),
      error: () => this.tokenValid.set(false)
    });
  }

  submit() {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    const { newPassword, confirmPassword } = this.form.getRawValue();
    this.auth.resetPassword({ token: this.token(), newPassword, confirmPassword }).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/recuperar-senha/sucesso']);
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.showError(err?.error?.message || 'Não foi possível redefinir a senha.');
      }
    });
  }
}
